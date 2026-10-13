namespace WithSalt.FFmpeg.Recorder;

/// <summary>A failure returned by an FFmpeg native function.</summary>
public sealed class FFmpegException : Exception
{
    public int ErrorCode { get; }
    internal FFmpegException(string operation, int errorCode, string detail)
        : base($"FFmpeg failed to {operation}: {detail} ({errorCode}).") => ErrorCode = errorCode;
}
