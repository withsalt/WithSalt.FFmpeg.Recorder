using System;
using System.Collections.Generic;
using System.Linq;
using FFMpegCore;
using FFMpegCore.Arguments;
using FFMpegCore.Enums;
using WithSalt.FFmpeg.Recorder.Interface;

namespace WithSalt.FFmpeg.Recorder.Builder
{
    internal class HttpInputArgumentsBuilder : BaseStreamInputArgumentsBuilder, IHttpInputArgumentsBuilder
    {
        private List<IArgument> _inputArgumentList = new List<IArgument>();

        public HttpInputArgumentsBuilder(Uri uri) : base(uri)
        {
            List<IArgument> lowDelayArguments = CreateLowDelayArguments(probeSize: 128);
            //直接访问输入数据（绕过缓存层），减少内存拷贝带来的延迟
            lowDelayArguments.Add(new CustomArgument("-avioflags direct"));
            _lowDelayArguments.AddRange(lowDelayArguments);

            _inputArgumentList.Add(new DisableChannelArgument(Channel.Audio));
        }

        public IHttpInputArgumentsBuilder WithProbeSize(uint probeSize)
        {
            if (probeSize == 0)
                return this;

            if (_inputArgumentList.Count == 0)
                return this;

            for (int i = 0; i < _inputArgumentList.Count; i++)
            {
                if (_inputArgumentList[i].Text.StartsWith("-probesize", StringComparison.OrdinalIgnoreCase))
                {
                    _inputArgumentList[i] = new CustomArgument($"-probesize {probeSize}");
                    return this;
                }
            }
            return this;
        }

        public IHttpInputArgumentsBuilder WithTimeout(uint timeout = 3)
        {
            if (timeout == 0)
                return this;

            _inputArgumentList.Add(new CustomArgument($"-timeout {timeout * 1000000}"));
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
