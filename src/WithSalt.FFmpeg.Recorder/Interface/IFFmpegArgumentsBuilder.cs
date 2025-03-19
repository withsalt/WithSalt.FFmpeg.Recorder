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

        IFFmpegArgumentsBuilder WithOutputQuality(OutputQuality quality = OutputQuality.Medium);

        IFFmpegArgumentsBuilder WithOutputSize(int width, int height);
    }
}
