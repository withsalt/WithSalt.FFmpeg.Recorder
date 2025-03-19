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

        public IRtspInputArgumentsBuilder WithRstpInput()
        {
            return new RtspInputArgumentsBuilder();
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
