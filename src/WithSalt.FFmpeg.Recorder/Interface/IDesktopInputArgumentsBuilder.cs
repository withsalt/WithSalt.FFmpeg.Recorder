using SkiaSharp;

namespace WithSalt.FFmpeg.Recorder.Interface
{
    public interface IDesktopInputArgumentsBuilder : IFFmpegArgumentsBuilder
    {
        /// <summary>
        /// 录制帧数
        /// </summary>
        /// <param name="framerate"></param>
        /// <returns></returns>
        IDesktopInputArgumentsBuilder WithFramerate(uint framerate = 30);

        /// <summary>
        /// 录制区域
        /// </summary>
        /// <param name="rectangle">基于左上角的right, bottom, left, top</param>
        /// <returns></returns>
        IDesktopInputArgumentsBuilder WithRectangle(SKRect rectangle);
    }
}
