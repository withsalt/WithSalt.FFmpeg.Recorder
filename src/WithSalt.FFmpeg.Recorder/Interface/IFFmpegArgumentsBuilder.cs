using System;
using FFMpegCore;
using SkiaSharp;
using WithSalt.FFmpeg.Recorder.Models;

namespace WithSalt.FFmpeg.Recorder.Interface
{
    public interface IFFmpegArgumentsBuilder
    {
        FFMpegArgumentProcessor Build();

        IFFmpegArgumentsBuilder WithImageHandle(Action<long, SKBitmap> action);

        IFFmpegArgumentsBuilder WithOutputFramerate(double framerate = 30);

        /// <summary>
        /// 设置输出视频的帧率模式
        /// </summary>
        /// <param name="fpsMode"></param>
        /// <returns></returns>
        IFFmpegArgumentsBuilder WithFpsMode(FpsMode fpsMode = FpsMode.Passthrough);

        IFFmpegArgumentsBuilder WithOutputQuality(OutputQuality quality = OutputQuality.Medium);

        IFFmpegArgumentsBuilder WithOutputSize(int width, int height);

        /// <summary>
        /// 设置低延迟参数优化级别
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// 某些情况下，低延迟参数会导致录制失败
        /// </remarks>
        IFFmpegArgumentsBuilder WithLatencyOptimizationLevel(LatencyOptimizationLevel level);
    }
}
