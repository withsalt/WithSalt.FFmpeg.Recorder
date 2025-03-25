using System;
using System.Collections.Generic;
using System.Text;

namespace WithSalt.FFmpeg.Recorder.Models
{
    public enum RtmpLiveType
    {
        /// <summary>
        /// 自动探测（不指定时的默认值）
        /// </summary>
        Any = 0,

        /// <summary>
        /// 直播模式
        /// </summary>
        Live = 1,

        /// <summary>
        /// 适用于录播
        /// </summary>
        Recorded = 2,
    }
}
