namespace WithSalt.FFmpeg.Recorder.Interface
{
    public interface ICameraInputArgumentsBuilder : IFFmpegArgumentsBuilder
    {
        /// <summary>Windows: DirectShow name. Linux: /dev/video*. macOS: AVFoundation name, video index (e.g. "0") or "default", without an audio selector.</summary>
        ICameraInputArgumentsBuilder WithDeviceName(string deviceName);

        ICameraInputArgumentsBuilder WithVideoSize(uint width, uint height);

        ICameraInputArgumentsBuilder WithFramerate(double framerate = 30);
    }
}
