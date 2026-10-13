using SkiaSharp;
using WithSalt.FFmpeg.Recorder.Interface;
using WithSalt.FFmpeg.Recorder.Models;
using WithSalt.FFmpeg.Recorder.Native;

namespace WithSalt.FFmpeg.Recorder.Builder;

internal abstract class BaseInputArgumentsBuilder : IFFmpegArgumentsBuilder
{
    private readonly object _gate = new();
    private bool _running;
    private int? _width, _height;
    private double? _framerate;
    private FpsMode? _fpsMode;
    private OutputQuality _quality = OutputQuality.Medium;
    private LatencyOptimizationLevel _latency = LatencyOptimizationLevel.High;
    private Action<long, SKBitmap>? _imageHandler;
    private Action<long, SKBitmap>? _borrowedImageHandler;
    private Func<long, bool>? _frameFilter;
    private int? _decoderThreads;
    private Action<FFmpegProgress>? _progressHandler;
    private TimeSpan _progressInterval = TimeSpan.FromSeconds(1);

    protected abstract InputSpec[] CreateInputs();
    protected bool HasOutputSize => _width.HasValue;
    protected void Change(Action change)
    {
        lock (_gate)
        {
            if (_running) throw new InvalidOperationException("Cannot change configuration while decoding.");
            change();
        }
    }

    protected static void ValidateFramerate(double value)
    {
        if (!double.IsFinite(value) || value <= 0 || value > 1000)
            throw new ArgumentOutOfRangeException(nameof(value), "Framerate must be finite and in (0, 1000].");
    }

    /// <summary>
    /// 注册独立图像回调：位图可修改、保留或异步入队，由调用方负责释放；替换已配置的图像视图回调。
    /// </summary>
    /// <inheritdoc cref="IFFmpegArgumentsBuilder.WithImageHandle"/>
    public IFFmpegArgumentsBuilder WithImageHandle(Action<long, SKBitmap> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Change(() => { _imageHandler = action; _borrowedImageHandler = null; });
        return this;
    }

    /// <summary>
    /// 注册只读图像视图回调：仅在当前同步回调内有效，禁止释放、修改或保留；替换已配置的独立图像回调。
    /// </summary>
    /// <inheritdoc cref="IFFmpegArgumentsBuilder.WithImageViewHandle"/>
    public IFFmpegArgumentsBuilder WithImageViewHandle(Action<long, SKBitmap> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Change(() => { _borrowedImageHandler = action; _imageHandler = null; });
        return this;
    }

    public IFFmpegArgumentsBuilder WithFrameFilter(Func<long, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Change(() => _frameFilter = predicate);
        return this;
    }

    public IFFmpegArgumentsBuilder WithDecoderThreads(int threads)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(threads);
        Change(() => _decoderThreads = threads);
        return this;
    }

    public IFFmpegArgumentsBuilder WithOutputSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((long)width * height > int.MaxValue / 4)
            throw new ArgumentOutOfRangeException(nameof(width), "Output bitmap is too large.");
        Change(() => { _width = width; _height = height; });
        return this;
    }

    public IFFmpegArgumentsBuilder WithOutputFramerate(double framerate = 30)
    {
        ValidateFramerate(framerate);
        Change(() => _framerate = framerate);
        return this;
    }

    public IFFmpegArgumentsBuilder WithFpsMode(FpsMode fpsMode = FpsMode.Passthrough)
    {
        if (!Enum.IsDefined(fpsMode)) throw new ArgumentOutOfRangeException(nameof(fpsMode));
        Change(() => _fpsMode = fpsMode);
        return this;
    }

    public IFFmpegArgumentsBuilder WithOutputQuality(OutputQuality quality = OutputQuality.Medium)
    {
        if (!Enum.IsDefined(quality)) throw new ArgumentOutOfRangeException(nameof(quality));
        Change(() => _quality = quality);
        return this;
    }

    public IFFmpegArgumentsBuilder WithLatencyOptimizationLevel(LatencyOptimizationLevel level)
    {
        if (!Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));
        Change(() => _latency = level);
        return this;
    }

    public IFFmpegArgumentsBuilder NotifyOnProgress(Action<FFmpegProgress> action, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        var period = interval ?? TimeSpan.FromSeconds(1);
        if (period < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        Change(() => { _progressHandler = action; _progressInterval = period; });
        return this;
    }

    private RecordingOptions BeginRun(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_running) throw new InvalidOperationException("This input is already decoding.");
            var mode = _fpsMode ?? (_framerate.HasValue ? FpsMode.CFR : FpsMode.Passthrough);
            if (mode == FpsMode.CFR && !_framerate.HasValue)
                throw new ArgumentException("CFR requires WithOutputFramerate.");
            if (_framerate.HasValue && mode is FpsMode.VFR or FpsMode.Passthrough)
                throw new ArgumentException("VFR and Passthrough cannot specify an output framerate.");
            var options = new RecordingOptions(CreateInputs(), _width, _height, _framerate, mode,
                _quality, _latency, _imageHandler, _progressHandler, _progressInterval,
                _borrowedImageHandler, _frameFilter, _decoderThreads);
            _running = true;
            return options;
        }
    }

    private void Run(RecordingOptions options, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            FFmpegNative.EnsureInitialized();
            NativeRecording.Run(options, token);
        }
        finally { lock (_gate) _running = false; }
    }

    public void ProcessSynchronously(CancellationToken cancellationToken = default)
        => Run(BeginRun(cancellationToken), cancellationToken);

    public async Task ProcessAsynchronously(CancellationToken cancellationToken = default)
    {
        var options = BeginRun(cancellationToken);
        // Always run the finally, including cancellation between snapshot and worker start.
        // Await inside an async method so OperationCanceledException completes the public
        // task as Canceled. The worker must not be canceled before Run releases _running.
        await Task.Run(() => Run(options, cancellationToken)).ConfigureAwait(false);
    }
}
