using System.Globalization;
using FFmpeg.AutoGen;
using SkiaSharp;
using WithSalt.FFmpeg.Recorder.Models;

namespace WithSalt.FFmpeg.Recorder.Native;

internal sealed unsafe class NativeInput : IDisposable
{
    private AVFormatContext* _format;
    private AVCodecContext* _codec;
    private AVPacket* _packet;
    private AVFrame* _frame;
    private AVStream* _stream;
    private int _streamIndex;
    private readonly NativeInterrupt _interrupt;
    private readonly CancellationToken _token;
    private readonly SKRect? _crop;
    private double? _firstTimestamp;
    private double _lastTime, _lastDuration;
    private bool _hasFrame;
    private double _fallbackDuration;
    private double _secondsPerTick;
    internal TimeSpan? Duration { get; private set; }

    internal NativeInput(InputSpec spec, LatencyOptimizationLevel latency, CancellationToken token, int? decoderThreads = null)
    {
        _token = token;
        _crop = spec.Crop;
        _interrupt = new NativeInterrupt(token, spec.TimeoutSeconds);
        try { Open(spec, latency, decoderThreads); }
        catch { Dispose(); throw; }
    }

    private void Open(InputSpec spec, LatencyOptimizationLevel latency, int? decoderThreads)
    {
        AVInputFormat* inputFormat = null;
        if (spec.Format is not null)
        {
            FFmpegNative.EnsureDevices();
            inputFormat = ffmpeg.av_find_input_format(spec.Format);
            if (inputFormat == null)
                throw new NotSupportedException($"This FFmpeg build does not contain the '{spec.Format}' input device.");
        }

        _format = ffmpeg.avformat_alloc_context();
        if (_format == null) throw new OutOfMemoryException("Cannot allocate FFmpeg input context.");
        _format->interrupt_callback = _interrupt.Callback;
        AVDictionary* dictionary = null;
        try
        {
            static void Set(AVDictionary** target, string key, string value)
                => NativeError.Check(ffmpeg.av_dict_set(target, key, value, 0), "set input option");
            if (!spec.IsFile && latency != LatencyOptimizationLevel.None)
            {
                Set(&dictionary, "fflags", "nobuffer+discardcorrupt");
                Set(&dictionary, "probesize", latency == LatencyOptimizationLevel.High ? "128" : "3000000");
                if (latency == LatencyOptimizationLevel.High)
                {
                    Set(&dictionary, "analyzeduration", "0");
                    Set(&dictionary, "avioflags", "direct");
                    if (spec.Url.StartsWith("rtsp:", StringComparison.OrdinalIgnoreCase))
                    {
                        Set(&dictionary, "reorder_queue_size", "0");
                        Set(&dictionary, "buffer_size", "8192");
                    }
                }
            }
            if (spec.TimeoutSeconds > 0)
            {
                string micros = ((long)spec.TimeoutSeconds * 1_000_000).ToString(CultureInfo.InvariantCulture);
                Set(&dictionary, spec.Url.StartsWith("rtsp:", StringComparison.OrdinalIgnoreCase) ? "timeout" : "rw_timeout", micros);
            }
            // Explicit options take precedence over latency presets.
            foreach (var option in spec.Options) Set(&dictionary, option.Key, option.Value);
            _interrupt.Begin();
            var context = _format;
            int result = ffmpeg.avformat_open_input(&context, spec.Url, inputFormat, &dictionary);
            _format = context; // avformat_open_input frees and nulls this pointer on failure.
            _interrupt.Check();
            NativeError.Check(result, "open input");
        }
        finally { ffmpeg.av_dict_free(&dictionary); }

        _interrupt.Begin();
        int probe = ffmpeg.avformat_find_stream_info(_format, null);
        _interrupt.Check();
        NativeError.Check(probe, "read stream information");
        AVCodec* decoder = null;
        _streamIndex = NativeError.Check(
            ffmpeg.av_find_best_stream(_format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &decoder, 0), "find a video stream");
        _stream = _format->streams[_streamIndex];
        _secondsPerTick = _stream->time_base.den > 0
            ? (double)_stream->time_base.num / _stream->time_base.den : double.NaN;
        for (int i = 0; i < _format->nb_streams; i++)
            if (i != _streamIndex) _format->streams[i]->discard = AVDiscard.AVDISCARD_ALL;

        _codec = ffmpeg.avcodec_alloc_context3(decoder);
        if (_codec == null) throw new OutOfMemoryException("Cannot allocate FFmpeg decoder.");
        NativeError.Check(ffmpeg.avcodec_parameters_to_context(_codec, _stream->codecpar), "configure decoder");
        _codec->pkt_timebase = _stream->time_base;
        if (!spec.IsFile && latency != LatencyOptimizationLevel.None)
        {
            _codec->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
            _codec->thread_count = 1;
            _codec->thread_type = ffmpeg.FF_THREAD_SLICE;
        }
        if (decoderThreads.HasValue) _codec->thread_count = decoderThreads.Value;
        NativeError.Check(ffmpeg.avcodec_open2(_codec, decoder, null), "open decoder");
        _packet = ffmpeg.av_packet_alloc();
        _frame = ffmpeg.av_frame_alloc();
        if (_packet == null || _frame == null) throw new OutOfMemoryException("Cannot allocate packet/frame.");

        var rate = ffmpeg.av_guess_frame_rate(_format, _stream, null);
        _fallbackDuration = rate.num > 0 && rate.den > 0 ? (double)rate.den / rate.num : 1.0 / 30;
        if (spec.IsFile)
        {
            double duration = _stream->duration > 0 && _stream->duration != ffmpeg.AV_NOPTS_VALUE
                ? Seconds(_stream->duration) : (double)_format->duration / ffmpeg.AV_TIME_BASE;
            if (double.IsFinite(duration) && duration > 0 && duration < TimeSpan.MaxValue.TotalSeconds)
                Duration = TimeSpan.FromSeconds(duration);
        }
    }

    private double Seconds(long pts)
        => pts * _secondsPerTick;

    internal double Decode(FrameOutput output)
    {
        while (true)
        {
            _interrupt.Begin();
            int read = ffmpeg.av_read_frame(_format, _packet);
            _interrupt.Check();
            if (read == ffmpeg.AVERROR_EOF) break;
            if (read == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                if (_token.WaitHandle.WaitOne(10)) _token.ThrowIfCancellationRequested();
                continue;
            }
            NativeError.Check(read, "read input packet");
            try
            {
                if (_packet->stream_index == _streamIndex) Send(_packet, output);
            }
            finally { ffmpeg.av_packet_unref(_packet); }
        }
        // Decoders may retain B-frames until a null packet flushes them.
        Send(null, output);
        return _hasFrame ? _lastTime + _lastDuration : 0;
    }

    private void Send(AVPacket* packet, FrameOutput output)
    {
        _token.ThrowIfCancellationRequested();
        int sent = ffmpeg.avcodec_send_packet(_codec, packet);
        if (sent == ffmpeg.AVERROR(ffmpeg.EAGAIN))
        {
            Receive(output);
            sent = ffmpeg.avcodec_send_packet(_codec, packet);
        }
        if (sent != ffmpeg.AVERROR_EOF) NativeError.Check(sent, "send packet to decoder");
        Receive(output);
    }

    private void Receive(FrameOutput output)
    {
        while (true)
        {
            _token.ThrowIfCancellationRequested();
            int received = ffmpeg.avcodec_receive_frame(_codec, _frame);
            if (received == ffmpeg.AVERROR(ffmpeg.EAGAIN) || received == ffmpeg.AVERROR_EOF) return;
            NativeError.Check(received, "decode video frame");
            try
            {
                double time;
                long pts = _frame->best_effort_timestamp;
                double timestamp = pts != ffmpeg.AV_NOPTS_VALUE ? Seconds(pts) : double.NaN;
                if (double.IsFinite(timestamp))
                {
                    _firstTimestamp ??= timestamp - (_hasFrame ? _lastTime + _lastDuration : 0);
                    time = Math.Max(0, timestamp - _firstTimestamp.Value);
                }
                else time = _hasFrame ? _lastTime + _lastDuration : 0;

                long frameDuration = NativeFrameLayout.Duration(_frame);
                double duration = frameDuration > 0 ? Seconds(frameDuration) : _fallbackDuration;
                _lastDuration = double.IsFinite(duration) && duration > 0 ? duration : _fallbackDuration;
                _lastTime = Math.Max(_lastTime, time);
                _hasFrame = true;
                if (_crop is { } crop) NativeFrameLayout.Crop(_frame, crop);
                output.Accept(_frame, time);
            }
            finally { ffmpeg.av_frame_unref(_frame); }
        }
    }

    public void Dispose()
    {
        var frame = _frame; _frame = null;
        if (frame != null) ffmpeg.av_frame_free(&frame);
        var packet = _packet; _packet = null;
        if (packet != null) ffmpeg.av_packet_free(&packet);
        var codec = _codec; _codec = null;
        if (codec != null) ffmpeg.avcodec_free_context(&codec);
        var format = _format; _format = null;
        if (format != null) ffmpeg.avformat_close_input(&format);
        GC.KeepAlive(_interrupt);
    }
}
