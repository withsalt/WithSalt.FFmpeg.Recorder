using WithSalt.FFmpeg.Recorder.Builder;
using WithSalt.FFmpeg.Recorder.Interface;

namespace WithSalt.FFmpeg.Recorder
{
    public class FFmpegArgumentsBuilder : IFFmpegInputArgumentsBuilder
    {
        public ICameraInputArgumentsBuilder WithCameraInput()
        {
            return new CameraInputArgumentsBuilder();
        }

        public IStreamInputArgumentsProxyBuilder WithStreamInput()
        {
            return new StreamInputArgumentsProxyBuilder();
        }

        public IDesktopInputArgumentsBuilder WithDesktopInput()
        {
            return new DesktopInputArgumentsBuilder();
        }

        public IFileInputArgumentsBuilder WithFileInput()
        {
            return new FileInputArgumentsBuilder();
        }
    }
}
