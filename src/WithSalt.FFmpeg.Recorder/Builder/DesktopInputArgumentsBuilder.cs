using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FFMpegCore;
using FFMpegCore.Arguments;
using FFMpegCore.Enums;
using SkiaSharp;
using WithSalt.FFmpeg.Recorder.Interface;

namespace WithSalt.FFmpeg.Recorder.Builder
{
    internal class DesktopInputArgumentsBuilder : BaseInputArgumentsBuilder, IDesktopInputArgumentsBuilder
    {
        private List<IArgument> _inputArgumentList = new List<IArgument>();
        public DesktopInputArgumentsBuilder()
        {
            _inputArgumentList.Add(new DisableChannelArgument(Channel.Audio));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                _inputArgumentList.Add(new ForceFormatArgument("gdigrab"));
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                _inputArgumentList.Add(new ForceFormatArgument("x11grab"));
            else
                throw new PlatformNotSupportedException($"Unsupported system type: {RuntimeInformation.OSDescription}");
        }

        public IDesktopInputArgumentsBuilder WithFramerate(double framerate = 30)
        {
            if (framerate <= 0)
                throw new ArgumentOutOfRangeException(nameof(framerate), "The number of frames cannot be less than 0");
            _inputArgumentList.Add(new FrameRateArgument(framerate));
            return this;
        }

        string inputPath = string.Empty;

        public IDesktopInputArgumentsBuilder WithRectangle(SKRect rectangle)
        {
            if (rectangle.Size.Width < 0 || rectangle.Size.Height < 0)
            {
                throw new ArgumentException("Invalid rectangle size");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _inputArgumentList.Add(new CustomArgument($"-offset_x {rectangle.Location.X}"));
                _inputArgumentList.Add(new CustomArgument($"-offset_y {rectangle.Location.Y}"));
                if (rectangle.Size.Width > 0 && rectangle.Size.Height > 0)
                {
                    _inputArgumentList.Add(new CustomArgument($"-video_size {rectangle.Size.Width}x{rectangle.Size.Height}"));
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                inputPath = $":0.0+{rectangle.Location.X},{rectangle.Location.Y}";
                if (rectangle.Size.Width > 0 && rectangle.Size.Height > 0)
                {
                    _inputArgumentList.Add(new CustomArgument($"-video_size {rectangle.Size.Width}x{rectangle.Size.Height}"));
                }
            }
            else
            {
                throw new PlatformNotSupportedException($"Unsupported system type: {RuntimeInformation.OSDescription}");
            }
            return this;
        }

        public override FFMpegArgumentProcessor Build()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _arguments = FFMpegArguments.FromFileInput("desktop", false, opt =>
                {
                    foreach (var argument in _latencyOptimizationContainer.Container[_latencyOptimizationContainer.Level])
                    {
                        opt.WithArgument(argument);
                    }
                    foreach (var argument in _inputArgumentList)
                    {
                        opt.WithArgument(argument);
                    }
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string path = !string.IsNullOrWhiteSpace(inputPath) ? inputPath : ":0.0";
                _arguments = FFMpegArguments.FromFileInput(path, false, opt =>
                {
                    foreach (var argument in _latencyOptimizationContainer.Container[_latencyOptimizationContainer.Level])
                    {
                        opt.WithArgument(argument);
                    }
                    foreach (var argument in _inputArgumentList)
                    {
                        opt.WithArgument(argument);
                    }
                });
            }
            else
            {
                throw new PlatformNotSupportedException($"Unsupported system type: {RuntimeInformation.OSDescription}");
            }
            return base.Build();
        }
    }
}
