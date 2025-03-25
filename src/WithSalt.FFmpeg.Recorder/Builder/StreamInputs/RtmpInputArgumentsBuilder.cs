using System;
using System.Collections.Generic;
using System.Linq;
using FFMpegCore;
using FFMpegCore.Arguments;
using FFMpegCore.Enums;
using WithSalt.FFmpeg.Recorder.Interface.StreamInputs;
using WithSalt.FFmpeg.Recorder.Models;

namespace WithSalt.FFmpeg.Recorder.Builder.StreamInputs
{
    internal class RtmpInputArgumentsBuilder : BaseStreamInputArgumentsBuilder, IRtmpInputArgumentsBuilder
    {
        private List<IArgument> _inputArgumentList = new List<IArgument>();

        public RtmpInputArgumentsBuilder(Uri uri) : base(uri)
        {
            List<IArgument> lowDelayArguments = CreateLowDelayArguments(probeSize: 128);
            _lowDelayArguments.AddRange(lowDelayArguments);

            _inputArgumentList.Add(new DisableChannelArgument(Channel.Audio));
        }

        public IRtmpInputArgumentsBuilder WithProbeSize(uint probeSize)
        {
            ResetProbeSize(_inputArgumentList, probeSize);
            return this;
        }

        public IRtmpInputArgumentsBuilder WithLiveType(RtmpLiveType liveType)
        {
            switch (liveType)
            {
                default:
                case RtmpLiveType.Any:
                    _inputArgumentList.Add(new CustomArgument("-rtmp_live any"));
                    break;
                case RtmpLiveType.Live:
                    _inputArgumentList.Add(new CustomArgument("-rtmp_live live"));
                    break;
                case RtmpLiveType.Recorded:
                    _inputArgumentList.Add(new CustomArgument("-rtmp_live recorded"));
                    break;
            }
            return this;
        }

        public override FFMpegArgumentProcessor Build()
        {
            _filterArgumentList.Add(new CustomArgument("-map 0:v?"));

            _arguments = FFMpegArguments
               .FromUrlInput(_uri, opt =>
               {
                   foreach (var argument in _lowDelayArguments)
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
