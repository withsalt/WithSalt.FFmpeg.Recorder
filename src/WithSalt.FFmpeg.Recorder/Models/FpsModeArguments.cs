using System;
using System.Collections.Generic;
using System.Text;
using FFMpegCore.Arguments;

namespace WithSalt.FFmpeg.Recorder.Models
{
    internal class FpsModeArguments
    {
        public readonly static IArgument Passthrough = new CustomArgument("-fps_mode passthrough");

        public readonly static IArgument VFR = new CustomArgument("-fps_mode vfr");

        public readonly static IArgument CFR = new CustomArgument("-fps_mode cfr");

        public readonly static IArgument Auto = new CustomArgument("-fps_mode auto");
        
        public readonly static IArgument Drop = new CustomArgument("-fps_mode drop");
    }
}
