using System;
using System.Collections.Generic;
using System.Text;

namespace WithSalt.FFmpeg.Recorder.Models
{
    public enum FpsMode
    {
        /// <summary>
        /// 保留输入流的原始时间戳（输入帧率直接传递到输出，不做任何修改）
        /// </summary>
        Passthrough = 0,

        /// <summary>
        /// 动态帧率(Variable Frame Rate)，根据输入帧的时间戳动态调整输出帧率
        /// </summary>
        VFR = 1,

        /// <summary>
        /// 恒定帧率(Constant Frame Rate)，如果输入帧率不稳定，FFmpeg 会通过丢弃或重复帧来强制输出为恒定帧率。
        /// </summary>
        CFR = 2,

        /// <summary>
        /// 自动，FFmpeg 根据输入流的特性自动选择最合适的帧率模式
        /// </summary>
        Auto = 3,

        /// <summary>
        /// 丢弃
        /// </summary>
        Drop = 4
    }
}
