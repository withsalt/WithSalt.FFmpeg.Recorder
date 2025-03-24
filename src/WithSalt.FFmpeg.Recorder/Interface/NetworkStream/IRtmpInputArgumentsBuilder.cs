using System;

namespace WithSalt.FFmpeg.Recorder.Interface
{
    public interface IRtmpInputArgumentsBuilder : IFFmpegArgumentsBuilder
    {
        /// <summary>
        /// 控制 FFmpeg 在分析输入流时读取的初始数据量参数，单位：字节
        /// </summary>
        /// <param name="probeSize"></param>
        /// <returns></returns>
        /// <remarks>
        /// 默认参数为64字节，这个值非常小，在无法正确识别流的情况下，可以适当增大这个值
        /// </remarks>
        IRtmpInputArgumentsBuilder WithProbeSize(uint probeSize);
    }
}
