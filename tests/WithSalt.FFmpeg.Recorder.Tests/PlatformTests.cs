using System.Globalization;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using SkiaSharp;
using WithSalt.FFmpeg.Recorder;
using WithSalt.FFmpeg.Recorder.Models;
using WithSalt.FFmpeg.Recorder.Native;

internal static class PlatformTests
{
    internal static void RuntimeAndLibrarySelection()
    {
        foreach (var (platform, architecture, rid) in new[]
        {
            (OSPlatform.Windows, Architecture.X64, "win-x64"),
            (OSPlatform.OSX, Architecture.X64, "osx-x64"),
            (OSPlatform.OSX, Architecture.Arm64, "osx-arm64"),
            (OSPlatform.Linux, Architecture.X64, "linux-x64"),
            (OSPlatform.Linux, Architecture.Arm, "linux-arm"),
            (OSPlatform.Linux, Architecture.Arm64, "linux-arm64"),
            (OSPlatform.Linux, Architecture.LoongArch64, "linux-loongarch64")
        }) Check.Equal(rid, NativePlatform.GetRuntimeIdentifier(platform, architecture));
        Check.Throws<PlatformNotSupportedException>(() => NativePlatform.GetRuntimeIdentifier(OSPlatform.Windows, Architecture.Arm));
        Check.Throws<PlatformNotSupportedException>(() => NativePlatform.GetRuntimeIdentifier(OSPlatform.OSX, Architecture.Arm));
        Check.Throws<PlatformNotSupportedException>(() => NativePlatform.GetRuntimeIdentifier(OSPlatform.Linux, Architecture.X86));
        Check.Throws<PlatformNotSupportedException>(() => NativePlatform.GetRuntimeIdentifier(OSPlatform.FreeBSD, Architecture.X64));
        Check.Equal("avutil-61.dll", NativePlatform.LibraryFileName("avutil", 61, OSPlatform.Windows));
        Check.Equal("libavutil.61.dylib", NativePlatform.LibraryFileName("avutil", 61, OSPlatform.OSX));
        Check.Equal("libavutil.so.61", NativePlatform.LibraryFileName("avutil", 61, OSPlatform.Linux));
    }

    internal static void DeviceInputs()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var commaCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        commaCulture.NumberFormat.NumberDecimalSeparator = ",";
        CultureInfo.CurrentCulture = commaCulture;
        try
        {
            foreach (var (platform, device, format, url) in new[]
            {
                (OSPlatform.Windows, "Camera name", "dshow", "video=Camera name"),
                (OSPlatform.Linux, "/dev/video0", "v4l2", "/dev/video0"),
                (OSPlatform.OSX, "0", "avfoundation", "0:none"),
                (OSPlatform.OSX, "default", "avfoundation", "default:none"),
                (OSPlatform.OSX, "FaceTime HD Camera", "avfoundation", "FaceTime HD Camera:none")
            })
            {
                var camera = DeviceInputFactory.Camera(platform, device, 1280, 720, 29.97);
                Check.Equal(format, camera.Format);
                Check.Equal(url, camera.Url);
                Check.Equal("1280x720", camera.Options["video_size"]);
                Check.Equal("29.97", camera.Options["framerate"]);
            }

            foreach (SKRect? rectangle in new SKRect?[] { null, SKRect.Create(0, 0, 0, 0), SKRect.Create(3, 5, 64, 48) })
            {
                var desktop = DeviceInputFactory.Desktop(OSPlatform.OSX, rectangle, 29.97, null);
                Check.Equal("avfoundation", desktop.Format);
                Check.Equal("Capture screen 0:none", desktop.Url);
                Check.Equal("bgra", desktop.Options["pixel_format"]);
                Check.Equal("29.97", desktop.Options["framerate"]);
                Check.True(!desktop.Options.ContainsKey("video_size"));
                Check.Equal(rectangle is { Width: > 0 } ? rectangle : null, desktop.Crop);
            }
            Check.Throws<ArgumentException>(() => DeviceInputFactory.Desktop(OSPlatform.OSX, SKRect.Create(-1, 0, 64, 48), null, null));
            Check.Throws<ArgumentException>(() => DeviceInputFactory.Desktop(OSPlatform.OSX, SKRect.Create(1, 0, 0, 0), null, null));
            Check.Throws<PlatformNotSupportedException>(() => DeviceInputFactory.Desktop(OSPlatform.Linux, null, null, null));
            Check.Throws<PlatformNotSupportedException>(() => DeviceInputFactory.Desktop(OSPlatform.Linux, null, null, ""));
            var linux = DeviceInputFactory.Desktop(OSPlatform.Linux, SKRect.Create(3, 5, 64, 48), null, ":99.0");
            Check.Equal("x11grab", linux.Format);
            Check.Equal(":99.0+3,5", linux.Url);
            Check.Equal("64x48", linux.Options["video_size"]);
            Check.True(linux.Crop is null);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    internal static unsafe void FrameLayoutAndCropping()
    {
        FFmpegNative.EnsureInitialized();
        // All fields before size_t crops must retain AutoGen's offsets on both pointer sizes.
        foreach (var field in typeof(NativeFrameLayout).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
        {
            if (IntPtr.Size == 4 && field.Name is "crop_top" or "crop_bottom" or "crop_left" or "crop_right" or "private_ref" or "ch_layout" or "duration" or "alpha_mode")
                continue;
            Check.Equal(Marshal.OffsetOf<FFmpeg.AutoGen.AVFrame>(field.Name), Marshal.OffsetOf<NativeFrameLayout>(field.Name));
        }
        AVFrame* frame = ffmpeg.av_frame_alloc();
        AVFrame* retained = ffmpeg.av_frame_alloc();
        try
        {
            Check.True(frame != null && retained != null);
            frame->width = 8; frame->height = 6; frame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
            NativeError.Check(ffmpeg.av_frame_get_buffer(frame, 32), "allocate crop test frame");
            for (int y = 0; y < frame->height; y++)
            for (int x = 0; x < frame->width; x++)
            {
                byte* pixel = frame->data[0] + y * frame->linesize[0] + x * 4;
                pixel[0] = 7; pixel[1] = (byte)(y * 20); pixel[2] = (byte)(x * 20); pixel[3] = 255;
            }
            ((NativeFrameLayout*)frame)->duration = 123;
            nint frameAddress = (nint)frame;
            Check.Throws<ArgumentOutOfRangeException>(() => NativeFrameLayout.Crop((AVFrame*)frameAddress, SKRect.Create(7, 0, 2, 2)));
            NativeFrameLayout.Crop(frame, SKRect.Create(1, 1, 3, 2));
            Check.Equal(3, frame->width); Check.Equal(2, frame->height);
            NativeError.Check(ffmpeg.av_frame_ref(retained, frame), "retain cropped frame");
            ffmpeg.av_frame_unref(frame);
            // Native av_frame_ref must have copied duration from the correct ABI offset.
            Check.Equal(123L, NativeFrameLayout.Duration(retained));
            var options = new RecordingOptions([], null, null, null, FpsMode.Passthrough,
                OutputQuality.Low, LatencyOptimizationLevel.None, null, null, TimeSpan.Zero);
            using (var converter = new FrameConverter(options))
            using (var image = converter.Convert(retained))
            {
                Check.Equal(new SKColor(20, 20, 7, 255), image.GetPixel(0, 0));
                Check.Equal(new SKColor(60, 40, 7, 255), image.GetPixel(2, 1));
                var borrowed = converter.ConvertBorrowed(retained);
                Check.Equal(image.GetPixel(0, 0), borrowed.GetPixel(0, 0));
                Check.Equal(image.GetPixel(2, 1), borrowed.GetPixel(2, 1));
            }
            using (var converter = new FrameConverter(options with { Width = 6, Height = 4 }))
            using (var image = converter.Convert(retained))
            {
                Check.Equal(6, image.Width); Check.Equal(4, image.Height);
                Check.Equal(new SKColor(20, 20, 7, 255), image.GetPixel(0, 0));
                Check.Equal(new SKColor(60, 40, 7, 255), image.GetPixel(5, 3));
            }
        }
        finally { ffmpeg.av_frame_free(&frame); ffmpeg.av_frame_free(&retained); }
    }
}
