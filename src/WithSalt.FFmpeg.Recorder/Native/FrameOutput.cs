using System.Diagnostics;
using FFmpeg.AutoGen;
using WithSalt.FFmpeg.Recorder.Models;

namespace WithSalt.FFmpeg.Recorder.Native;

internal sealed unsafe class FrameOutput : IDisposable
{
    private readonly RecordingOptions _options;
    private readonly FrameConverter _converter;
    private readonly FrameRateScheduler _scheduler;
    private readonly CancellationToken _token;
    private readonly double _progressIntervalTicks;
    private long _lastReportTimestamp;
    private AVFrame* _previous;
    private int _inputIndex, _progressInputIndex;
    private double _offset, _progressOffset, _progressTime;
    private TimeSpan? _duration, _progressDuration;
    private long _frameIndex, _reportedIndex;
    private double _retainedTime = double.NegativeInfinity;
    private bool _borrowedMatchesPrevious;

    internal FrameOutput(RecordingOptions options, CancellationToken token)
    {
        _options = options;
        _token = token;
        _converter = new FrameConverter(options);
        _scheduler = new FrameRateScheduler(options.FpsMode, options.Framerate);
        _progressIntervalTicks = options.ProgressInterval.TotalSeconds * Stopwatch.Frequency;
        if (_scheduler.NeedsPrevious && (options.ImageHandler != null || options.BorrowedImageHandler != null))
        {
            _previous = ffmpeg.av_frame_alloc();
            if (_previous == null) throw new OutOfMemoryException("Cannot allocate retained video frame.");
        }
    }

    internal void BeginInput(int index, double offset, TimeSpan? duration)
    {
        _inputIndex = index;
        _offset = offset;
        _duration = duration;
    }

    internal void Accept(AVFrame* frame, double inputTime)
    {
        // A regressing PTS must not replace the image retained for later CFR duplicates.
        double time = _offset + inputTime;
        if (_scheduler.NeedsPrevious && time <= _retainedTime) return;
        bool convertedCurrent = false;
        foreach (var emission in _scheduler.Push(time))
            if (Emit(emission.Previous ? _previous : frame, emission.Time, emission.Previous) && !emission.Previous)
                convertedCurrent = true;
        if (_previous != null)
        {
            ffmpeg.av_frame_unref(_previous);
            NativeError.Check(ffmpeg.av_frame_ref(_previous, frame), "retain a frame for framerate conversion");
            _retainedTime = time;
        }
        _borrowedMatchesPrevious = convertedCurrent;
    }

    internal void EndInput(double duration)
    {
        foreach (var emission in _scheduler.FinishInput(_offset + duration))
            Emit(_previous, emission.Time, true);
        if (_previous != null) ffmpeg.av_frame_unref(_previous);
        _borrowedMatchesPrevious = false;
    }

    private bool Emit(AVFrame* frame, double time, bool previous)
    {
        _token.ThrowIfCancellationRequested();
        ++_frameIndex;
        bool converted = false;
        if ((_options.ImageHandler != null || _options.BorrowedImageHandler != null)
            && (_options.FrameFilter == null || _options.FrameFilter(_frameIndex)))
        {
            if (_options.BorrowedImageHandler is { } borrowed)
            {
                var bitmap = previous && _borrowedMatchesPrevious
                    ? _converter.LastBorrowed : _converter.ConvertBorrowed(frame);
                borrowed(_frameIndex, bitmap);
                _borrowedMatchesPrevious = previous;
                converted = true;
            }
            else if (_options.ImageHandler is { } handler)
            {
                var bitmap = _converter.Convert(frame);
                try { handler(_frameIndex, bitmap); }
                catch { bitmap.Dispose(); throw; }
            }
        }
        _token.ThrowIfCancellationRequested();
        if (_options.ProgressHandler == null) return converted;
        _progressTime = time;
        _progressInputIndex = _inputIndex;
        _progressOffset = _offset;
        _progressDuration = _duration;
        if (_reportedIndex == 0 || Stopwatch.GetTimestamp() - _lastReportTimestamp >= _progressIntervalTicks)
            Report();
        return converted;
    }

    internal void Report()
    {
        if (_options.ProgressHandler is not { } handler || _frameIndex == 0 || _reportedIndex == _frameIndex) return;
        handler(new FFmpegProgress(_frameIndex, TimeSpan.FromSeconds(_progressTime), _progressInputIndex,
            TimeSpan.FromSeconds(Math.Max(0, _progressTime - _progressOffset)), _progressDuration));
        _reportedIndex = _frameIndex;
        _lastReportTimestamp = Stopwatch.GetTimestamp();
    }

    public void Dispose()
    {
        var previous = _previous; _previous = null;
        if (previous != null) ffmpeg.av_frame_free(&previous);
        _converter.Dispose();
    }
}
