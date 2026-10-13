using System;
using WithSalt.FFmpeg.Recorder.Models;

namespace WithSalt.FFmpeg.Recorder.Interface.StreamInputs
{
    public interface IRtmpInputArgumentsBuilder : IFFmpegArgumentsBuilder
    {
        /// <summary>
        /// 控制 FFmpeg 在分析输入流时读取的初始数据量参数，单位：字节
        /// </summary>
        /// <param name="probeSize"></param>
        /// <returns></returns>
        /// <remarks>
        /// 显式值覆盖低延迟预设；最小值为32字节，0表示恢复预设。无法识别流时应增大此值。
        /// </remarks>
        IRtmpInputArgumentsBuilder WithProbeSize(uint probeSize);

        /// <summary>
        /// 设置 RTMP 流的直播类型（可选）
        /// </summary>
        /// <param name="liveType"></param>
        /// <returns></returns>
        IRtmpInputArgumentsBuilder WithLiveType(RtmpLiveType liveType);
    }
}
