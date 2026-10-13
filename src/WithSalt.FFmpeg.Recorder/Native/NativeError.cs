using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace WithSalt.FFmpeg.Recorder.Native;

internal static unsafe class NativeError
{
    internal static int Check(int result, string operation)
    {
        if (result >= 0) return result;
        byte* buffer = stackalloc byte[1024];
        int formatted = ffmpeg.av_strerror(result, buffer, 1024);
        string detail = formatted >= 0 ? Marshal.PtrToStringUTF8((nint)buffer) ?? "Unknown error" : "Unknown error";
        throw new FFmpegException(operation, result, detail);
    }
}
