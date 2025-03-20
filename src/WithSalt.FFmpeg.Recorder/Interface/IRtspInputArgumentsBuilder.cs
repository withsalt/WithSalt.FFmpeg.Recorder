using System;

namespace WithSalt.FFmpeg.Recorder.Interface
{
    public interface IRtspInputArgumentsBuilder : IFFmpegArgumentsBuilder
    {
        IRtspInputArgumentsBuilder WithUri(string uriStr);

        IRtspInputArgumentsBuilder WithUri(Uri uri);

        IRtspInputArgumentsBuilder WithTcp();

        IRtspInputArgumentsBuilder WithUdp();

        /// <summary>
        /// 网络连接超时设置，单位：秒
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns></returns>
        IRtspInputArgumentsBuilder WithTimeout(uint timeout = 3);
    }
}
