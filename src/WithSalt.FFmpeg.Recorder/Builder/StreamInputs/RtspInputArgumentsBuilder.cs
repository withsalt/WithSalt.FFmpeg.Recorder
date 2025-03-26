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
    internal class RtspInputArgumentsBuilder : BaseStreamInputArgumentsBuilder, IRtspInputArgumentsBuilder
    {
        private List<IArgument> _inputArgumentList = new List<IArgument>();

        public RtspInputArgumentsBuilder(Uri uri) : base(uri)
        {
            //禁用包重排序
            _latencyOptimizationContainer.Container[LatencyOptimizationLevel.High].Add(new CustomArgument("-reorder_queue_size 0"));
            //减小接收缓冲区
            _latencyOptimizationContainer.Container[LatencyOptimizationLevel.High].Add(new CustomArgument("-buffer_size 8192"));

            _inputArgumentList.Add(new DisableChannelArgument(Channel.Audio));
        }

        bool hasSetTransport = false;

        public IRtspInputArgumentsBuilder WithTcp()
        {
            if (hasSetTransport)
            {
                throw new ArgumentException("You have already specified the transmission type and cannot specify it again.");
            }
            _inputArgumentList.Add(new CustomArgument("-rtsp_transport tcp"));
            hasSetTransport = true;
            return this;
        }

        public IRtspInputArgumentsBuilder WithUdp()
        {
            if (hasSetTransport)
            {
                throw new ArgumentException("You have already specified the transmission type and cannot specify it again.");
            }
            _inputArgumentList.Add(new CustomArgument("-rtsp_transport udp"));
            hasSetTransport = true;
            return this;
        }

        public IRtspInputArgumentsBuilder WithTimeout(uint timeout = 3)
        {
            if (timeout == 0)
                return this;

            _inputArgumentList.Add(new CustomArgument($"-timeout {timeout * 1000000}"));
            return this;
        }

        public IRtspInputArgumentsBuilder WithProbeSize(uint probeSize)
        {
            ResetProbeSize(_inputArgumentList, probeSize);
            return this;
        }

        public override FFMpegArgumentProcessor Build()
        {
            if (!_inputArgumentList.Any(s => s.Text.StartsWith("-rtsp_transport", StringComparison.OrdinalIgnoreCase)))
            {
                WithTcp();
            }

            _filterArgumentList.Add(new CustomArgument("-map 0:v?"));

            _arguments = FFMpegArguments
               .FromUrlInput(_uri, opt =>
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
