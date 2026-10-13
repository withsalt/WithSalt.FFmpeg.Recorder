using System;

namespace WithSalt.FFmpeg.Recorder.Interface.StreamInputs
{
    public interface IHttpInputArgumentsBuilder : IFFmpegArgumentsBuilder
    {
        /// <summary>
        /// 网络连接超时设置，单位：秒
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns></returns>
        IHttpInputArgumentsBuilder WithTimeout(uint timeout = 3);

        /// <summary>
        /// 控制 FFmpeg 在分析输入流时读取的初始数据量参数，单位：字节
        /// </summary>
        /// <param name="probeSize"></param>
        /// <returns></returns>
        /// <remarks>
        /// 显式值覆盖低延迟预设；最小值为32字节，0表示恢复预设。无法识别流时应增大此值。
        /// </remarks>
        IHttpInputArgumentsBuilder WithProbeSize(uint probeSize);
    }
}
