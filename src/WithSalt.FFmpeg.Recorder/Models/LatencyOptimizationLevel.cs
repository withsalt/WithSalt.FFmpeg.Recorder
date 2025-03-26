using System;
using System.Collections.Generic;
using System.Text;

namespace WithSalt.FFmpeg.Recorder.Models
{
    public enum LatencyOptimizationLevel
    {
        /// <summary>
        /// 不进行延迟优化
        /// </summary>
        None = 1,

        /// <summary>
        /// 中等延迟优化
        /// </summary>
        Medium = 2,

        /// <summary>
        /// 进行极限的延迟优化（非常激进的参数配置）
        /// </summary>
        High = 3
    }
}
