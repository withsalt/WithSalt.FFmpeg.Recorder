namespace WithSalt.FFmpeg.Recorder.Interface
{
    public interface ICameraInputArgumentsBuilder : IFFmpegArgumentsBuilder
    {
        ICameraInputArgumentsBuilder WithDeviceName(string deviceName);

        ICameraInputArgumentsBuilder WithVideoSize(uint width, uint height);

        ICameraInputArgumentsBuilder WithFramerate(double framerate = 30);
    }
}
