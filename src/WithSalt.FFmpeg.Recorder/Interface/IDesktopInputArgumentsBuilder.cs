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
        IDesktopInputArgumentsBuilder WithFramerate(double framerate = 30);

        /// <summary>
        /// 录制区域。宽高均为 0 时由原生设备自动确定采集尺寸；否则宽高必须均为正整数。
        /// </summary>
        /// <param name="rectangle">像素坐标矩形，使用 SKRect.Create(x, y, width, height) 创建。Windows 支持负坐标的副屏。
        /// macOS 使用第一块显示器内的非负像素坐标；自动范围要求原点 (0, 0)，指定区域在输出缩放前裁剪。</param>
        /// <returns></returns>
        IDesktopInputArgumentsBuilder WithRectangle(SKRect rectangle);
    }
}
