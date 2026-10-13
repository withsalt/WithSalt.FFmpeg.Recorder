using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using WithSalt.FFmpeg.Recorder;
using WithSalt.FFmpeg.Recorder.Native;

internal static class NativeResolverTests
{
    internal static void AbiFamilies()
    {
        var ffmpeg8 = NativeAbi.FromAvutilVersion(60);
        var ffmpeg9 = NativeAbi.FromAvutilVersion(61);
        Check.Equal(8, ffmpeg8.FFmpegMajorVersion);
        Check.Equal(9, ffmpeg9.FFmpegMajorVersion);
        foreach (var (library, version8, version9) in new[]
        {
            ("avutil", 60, 61), ("avcodec", 62, 63), ("avformat", 62, 63),
            ("swresample", 6, 7), ("swscale", 9, 10), ("avfilter", 11, 12), ("avdevice", 62, 63)
        })
        {
            ffmpeg8.ValidateVersion(library, version8, "FFmpeg 8 library");
            ffmpeg9.ValidateVersion(library, version9, "FFmpeg 9 library");
            Check.Throws<NotSupportedException>(() => ffmpeg8.ValidateVersion(library, version9, "mixed library"));
            Check.Throws<NotSupportedException>(() => ffmpeg9.ValidateVersion(library, version8, "mixed library"));
        }
        Check.Throws<NotSupportedException>(() => NativeAbi.FromAvutilVersion(59));
        Check.Throws<NotSupportedException>(() => NativeAbi.FromAvutilVersion(62));
    }

    internal static void DirectoryDiscovery()
    {
        string root = Path.Combine(Path.GetTempPath(), "withsalt-resolver-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var (platform, rid) in new[]
            {
                (OSPlatform.Windows, "win-x64"), (OSPlatform.Linux, "linux-x64"),
                (OSPlatform.OSX, "osx-arm64")
            })
            {
                var directories = NativeFunctionResolver.SearchDirectories(root, rid, platform).ToArray();
                Check.Equal(Path.Combine(root, "runtimes", rid, "native"), directories[0]);
                Check.Equal(Path.Combine(root, "ffmpeg", rid), directories[^2]);
                Check.Equal(Path.Combine(root, "ffmpeg"), directories[^1]);
                if (platform == OSPlatform.OSX)
                    Check.Equal(Path.Combine(root, "runtimes", "osx", "native"), directories[1]);
            }
            var candidates = NativeFunctionResolver.SearchDirectories(root, NativePlatform.RuntimeIdentifier, NativePlatform.OS).ToArray();
            Check.Equal<string?>(null, NativeFunctionResolver.FindDirectory(candidates));
            string ffmpeg = candidates[^1], ffmpegRid = candidates[^2];
            Directory.CreateDirectory(ffmpegRid);
            string unversioned = NativePlatform.LibraryFileName("avutil", NativePlatform.OS);
            File.WriteAllBytes(Path.Combine(ffmpeg, unversioned), []);
            Check.Equal(ffmpeg, NativeFunctionResolver.FindDirectory(candidates));
            foreach (int version in new[] { 60, 61, 62 })
            {
                string file = Path.Combine(ffmpegRid, NativePlatform.LibraryFileName("avutil", version, NativePlatform.OS));
                File.WriteAllBytes(file, []);
                Check.Equal(ffmpegRid, NativeFunctionResolver.FindDirectory(candidates));
                File.Delete(file);
            }
            string rootFile = Path.Combine(root, NativePlatform.LibraryFileName("avutil", 60, NativePlatform.OS));
            File.WriteAllBytes(rootFile, []);
            Check.Equal(root, NativeFunctionResolver.FindDirectory(candidates));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    internal static unsafe void LoadedAbiAndCodecFields()
    {
        FFmpegNative.EnsureInitialized();
        var abi = NativeAbi.FromAvutilVersion((int)(ffmpeg.avutil_version() >> 16));
        Console.WriteLine($"Detected FFmpeg {abi.FFmpegMajorVersion}.x native libraries.");
        Check.Equal(abi.LibraryVersion("avcodec"), (int)(ffmpeg.avcodec_version() >> 16));
        Check.Equal(abi.LibraryVersion("avformat"), (int)(ffmpeg.avformat_version() >> 16));
        Check.Equal(abi.LibraryVersion("swscale"), (int)(ffmpeg.swscale_version() >> 16));
        Check.Equal(abi.LibraryVersion("swresample"), (int)(ffmpeg.swresample_version() >> 16));
        AVCodecContext* codec = ffmpeg.avcodec_alloc_context3(null);
        Check.True(codec != null);
        try
        {
            codec->thread_count = 3;
            codec->thread_type = ffmpeg.FF_THREAD_SLICE;
            codec->pkt_timebase = new AVRational { num = 1, den = 123 };
            codec->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
            long value = 0;
            Check.Equal(0, ffmpeg.av_opt_get_int(codec, "threads", 0, &value));
            Check.Equal(3L, value);
            Check.Equal(0, ffmpeg.av_opt_get_int(codec, "thread_type", 0, &value));
            Check.Equal((long)ffmpeg.FF_THREAD_SLICE, value);
            Check.Equal(0, ffmpeg.av_opt_get_int(codec, "flags", 0, &value));
            Check.True((value & ffmpeg.AV_CODEC_FLAG_LOW_DELAY) != 0);
            AVRational timeBase;
            Check.Equal(0, ffmpeg.av_opt_get_q(codec, "pkt_timebase", 0, &timeBase));
            Check.Equal(1, timeBase.num);
            Check.Equal(123, timeBase.den);
        }
        finally { ffmpeg.avcodec_free_context(&codec); }
    }
}
