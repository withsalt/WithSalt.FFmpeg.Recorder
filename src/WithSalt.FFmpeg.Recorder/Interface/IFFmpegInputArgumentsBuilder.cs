namespace WithSalt.FFmpeg.Recorder.Interface
{
    public interface IFFmpegInputArgumentsBuilder
    {
        ICameraInputArgumentsBuilder WithCameraInput();

        IStreamInputArgumentsProxyBuilder WithStreamInput();

        IDesktopInputArgumentsBuilder WithDesktopInput();

        IFileInputArgumentsBuilder WithFileInput();
    }
}
