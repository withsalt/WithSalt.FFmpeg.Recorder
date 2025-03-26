using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using FFMpegCore;
using FFMpegCore.Arguments;
using FFMpegCore.Enums;
using WithSalt.FFmpeg.Recorder.Interface;

namespace WithSalt.FFmpeg.Recorder.Builder
{
    internal class CameraInputArgumentsBuilder : BaseInputArgumentsBuilder, ICameraInputArgumentsBuilder
    {
        private List<IArgument> _inputArgumentList = new List<IArgument>();
        public CameraInputArgumentsBuilder()
        {
            _inputArgumentList.Add(new DisableChannelArgument(Channel.Audio));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                _inputArgumentList.Add(new ForceFormatArgument("dshow"));
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                _inputArgumentList.Add(new ForceFormatArgument("v4l2"));
            else
                throw new PlatformNotSupportedException($"Unsupported system type: {RuntimeInformation.OSDescription}");
        }

        private string _deviceName = string.Empty;

        public ICameraInputArgumentsBuilder WithDeviceName(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                throw new ArgumentNullException(nameof(deviceName));
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                this._deviceName = $"video=\"{deviceName}\"";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                this._deviceName = $"\"{deviceName}\"";
            else
                throw new PlatformNotSupportedException($"Unsupported system type: {RuntimeInformation.OSDescription}");
            return this;
        }

        public ICameraInputArgumentsBuilder WithVideoSize(uint width, uint height)
        {
            _inputArgumentList.Add(new CustomArgument($"-video_size {width}x{height}"));
            return this;
        }

        public ICameraInputArgumentsBuilder WithFramerate(uint framerate = 30)
        {
            _inputArgumentList.Add(new FrameRateArgument(framerate));
            return this;
        }

        public override FFMpegArgumentProcessor Build()
        {
            if (string.IsNullOrWhiteSpace(this._deviceName))
            {
                throw new ArgumentException("Please specify the input device name.");
            }
            if (!_inputArgumentList.Any(s => s.Text.StartsWith("-video_size", StringComparison.OrdinalIgnoreCase)))
            {
                WithVideoSize(1280, 720);
            }
            _arguments = FFMpegArguments
               .FromDeviceInput(this._deviceName, opt =>
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
            return base.Build();
        }
    }
}
