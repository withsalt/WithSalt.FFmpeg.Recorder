namespace WithSalt.FFmpeg.Recorder.Models;

/// <param name="FrameIndex">Number of emitted frames, starting at one.</param>
/// <param name="MediaTime">Normalized output media time across all input files.</param>
/// <param name="InputIndex">Zero-based index of the current input.</param>
/// <param name="InputTime">Media time relative to the current input.</param>
/// <param name="InputDuration">Current file duration when known; null for live inputs.</param>
public sealed record FFmpegProgress(long FrameIndex, TimeSpan MediaTime, int InputIndex,
    TimeSpan InputTime, TimeSpan? InputDuration);
