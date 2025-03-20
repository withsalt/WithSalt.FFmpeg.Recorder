using System;
using System.Collections.Generic;
using System.Linq;
using FFMpegCore;
using FFMpegCore.Arguments;
using FFMpegCore.Enums;
using WithSalt.FFmpeg.Recorder.Interface;

namespace WithSalt.FFmpeg.Recorder.Builder
{
    internal class RtspInputArgumentsBuilder : BaseInputArgumentsBuilder, IRtspInputArgumentsBuilder
    {
        private Uri? _uri = null;
        private List<IArgument> _inputArgumentList = new List<IArgument>();

        public RtspInputArgumentsBuilder()
        {
            _inputArgumentList.AddRange(CreateLowDelayArguments(probeSize: 128));
            //禁用包重排序
            _inputArgumentList.Add(new CustomArgument("-reorder_queue_size 0"));
            //减小接收缓冲区
            _inputArgumentList.Add(new CustomArgument("-buffer_size 8192"));
            _inputArgumentList.Add(new DisableChannelArgument(Channel.Audio));
        }

        public IRtspInputArgumentsBuilder WithUri(string uriStr)
        {
            if (string.IsNullOrWhiteSpace(uriStr))
            {
                throw new ArgumentNullException(nameof(uriStr), "Invalid URI.");
            }
            if (!Uri.TryCreate(uriStr, UriKind.Absolute, out Uri? uri))
            {
                throw new ArgumentException(nameof(uriStr), "Invalid URI.");
            }
            return WithUri(uri);
        }

        public IRtspInputArgumentsBuilder WithUri(Uri uri)
        {
            if (!uri.ToString().StartsWith("rtsp", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(nameof(uri), "Invalid URI. Your input uri is not a rtsp stream.");
            }
            _uri = uri;
            return this;
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

        public override FFMpegArgumentProcessor Build()
        {
            if (_uri == null)
            {
                throw new ArgumentException("You have not specified the RTSP stream address yet. Please set the RTSP stream address using the WithUri method.");
            }
            if (!_inputArgumentList.Any(s => s.Text.StartsWith("-rtsp_transport", StringComparison.OrdinalIgnoreCase)))
            {
                WithTcp();
            }
            _arguments = FFMpegArguments
               .FromUrlInput(_uri, opt =>
               {
                   foreach (var argument in _inputArgumentList)
                   {
                       opt.WithArgument(argument);
                   }
               });
            return base.Build();
        }
    }
}
