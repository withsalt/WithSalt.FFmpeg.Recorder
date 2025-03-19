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
            _inputArgumentList.AddRange(CreateLowDelayArguments());
            _inputArgumentList.Add(new DisableChannelArgument(Channel.Audio));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                _inputArgumentList.Add(new ForceFormatArgument("gdigrab"));
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                _inputArgumentList.Add(new ForceFormatArgument("x11grab"));
            else
                throw new PlatformNotSupportedException($"Unsupported system type: {RuntimeInformation.OSDescription}");
        }

        public IDesktopInputArgumentsBuilder WithFramerate(uint framerate = 30)
        {
            _inputArgumentList.Add(new FrameRateArgument(framerate));
            return this;
        }

        string inputPath = string.Empty;

        public IDesktopInputArgumentsBuilder WithRectangle(SKRect rectangle)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _inputArgumentList.Add(new CustomArgument($"-offset_x {rectangle.Left}"));
                _inputArgumentList.Add(new CustomArgument($"-offset_y {rectangle.Top}"));
                if (rectangle.Right > 0 && rectangle.Bottom > 0)
                {
                    _inputArgumentList.Add(new CustomArgument($"-video_size {rectangle.Right}x{rectangle.Bottom}"));
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                inputPath = $":0.0+{rectangle.Left},{rectangle.Top}";
                if (rectangle.Width > 0 && rectangle.Height > 0)
                {
                    _inputArgumentList.Add(new CustomArgument($"-video_size {rectangle.Width}x{rectangle.Height}"));
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
