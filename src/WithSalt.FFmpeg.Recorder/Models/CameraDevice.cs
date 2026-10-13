namespace WithSalt.FFmpeg.Recorder.Models;

/// <summary>Describes a camera returned by FFmpeg's native device enumeration API.</summary>
public sealed record CameraDevice(string Name, string? Description, bool IsDefault);
