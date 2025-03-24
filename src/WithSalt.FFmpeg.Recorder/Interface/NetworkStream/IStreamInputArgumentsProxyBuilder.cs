using System;

namespace WithSalt.FFmpeg.Recorder.Interface
{
    public interface IStreamInputArgumentsProxyBuilder
    {
        IRtspInputArgumentsBuilder WithRtsp(string uri);

        IRtspInputArgumentsBuilder WithRtsp(Uri uri);

        IHttpInputArgumentsBuilder WithHttp(string uri);

        IHttpInputArgumentsBuilder WithHttp(Uri uri);

        IRtmpInputArgumentsBuilder WithRtmp(string uri);

        IRtmpInputArgumentsBuilder WithRtmp(Uri uri);
    }
}
