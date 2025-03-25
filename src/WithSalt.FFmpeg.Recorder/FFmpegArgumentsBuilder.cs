using WithSalt.FFmpeg.Recorder.Builder;
using WithSalt.FFmpeg.Recorder.Builder.StreamInputs;
using WithSalt.FFmpeg.Recorder.Interface;
using WithSalt.FFmpeg.Recorder.Interface.StreamInputs;

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
