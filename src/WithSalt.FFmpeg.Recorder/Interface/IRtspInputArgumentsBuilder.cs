using System;

namespace WithSalt.FFmpeg.Recorder.Interface
{
    public interface IRtspInputArgumentsBuilder : IFFmpegArgumentsBuilder
    {
        IRtspInputArgumentsBuilder WithUri(string uriStr);

        IRtspInputArgumentsBuilder WithUri(Uri uri);

        IRtspInputArgumentsBuilder WithTcp();

        IRtspInputArgumentsBuilder WithUdp();
    }
}
