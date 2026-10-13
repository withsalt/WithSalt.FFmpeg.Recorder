using SkiaSharp;
using WithSalt.FFmpeg.Recorder.Models;

namespace WithSalt.FFmpeg.Recorder.Native;

internal sealed record InputSpec(string Url, string? Format, bool IsFile,
    IReadOnlyDictionary<string, string> Options, uint TimeoutSeconds = 0, SKRect? Crop = null);

internal sealed record RecordingOptions(InputSpec[] Inputs, int? Width, int? Height,
    double? Framerate, FpsMode FpsMode, OutputQuality Quality, LatencyOptimizationLevel Latency,
    Action<long, SKBitmap>? ImageHandler, Action<FFmpegProgress>? ProgressHandler, TimeSpan ProgressInterval,
    Action<long, SKBitmap>? BorrowedImageHandler = null, Func<long, bool>? FrameFilter = null, int? DecoderThreads = null);
