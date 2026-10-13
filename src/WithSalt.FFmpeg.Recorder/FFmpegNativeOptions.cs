namespace WithSalt.FFmpeg.Recorder;

/// <summary>Process-wide native runtime configuration, applied before the first recording.</summary>
public sealed class FFmpegNativeOptions
{
    /// <summary>
    /// Optional directory containing FFmpeg 8.x or 9.x shared libraries from one ABI family.
    /// Null searches application native assets, ffmpeg/{rid}, ffmpeg, then the operating
    /// system's library loader (including ldconfig on Linux). The ABI is detected automatically.
    /// macOS libraries use lib{name}.{major}.dylib; Linux uses lib{name}.so.{major}.
    /// </summary>
    public string? LibraryPath { get; set; }
}
