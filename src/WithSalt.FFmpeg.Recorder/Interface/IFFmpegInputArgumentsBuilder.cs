namespace WithSalt.FFmpeg.Recorder.Interface
{
    public interface IFFmpegInputArgumentsBuilder
    {
        ICameraInputArgumentsBuilder WithCameraInput();

        IRtspInputArgumentsBuilder WithRstpInput();

        IDesktopInputArgumentsBuilder WithDesktopInput();

        IFileInputArgumentsBuilder WithFileInput();
    }
}
