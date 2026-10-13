using System.Globalization;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace WithSalt.FFmpeg.Recorder.Native;

internal static class DeviceInputFactory
{
    internal static InputSpec Camera(OSPlatform platform, string device, uint width, uint height, double? framerate)
    {
        var options = new Dictionary<string, string> { ["video_size"] = $"{width}x{height}" };
        if (framerate.HasValue) options["framerate"] = framerate.Value.ToString(CultureInfo.InvariantCulture);
        // AVFoundation's filename has separate video:audio selectors. Disable audio.
        string url = platform == OSPlatform.Windows ? $"video={device}"
            : platform == OSPlatform.OSX ? $"{device}:none" : device;
        if (platform == OSPlatform.OSX) options["pixel_format"] = "bgra";
        return new InputSpec(url, NativePlatform.CameraFormat(platform), false, options);
    }

    internal static InputSpec Desktop(OSPlatform platform, SKRect? rectangle, double? framerate, string? display)
    {
        var options = new Dictionary<string, string>();
        if (framerate.HasValue) options["framerate"] = framerate.Value.ToString(CultureInfo.InvariantCulture);
        if (platform == OSPlatform.OSX)
        {
            // AVFoundation captures the whole first display; crop decoded frames before output scaling.
            // video_size is a capture-mode selector, not a screen region on this device.
            options["pixel_format"] = "bgra";
            if (rectangle is { } area && (area.Left < 0 || area.Top < 0))
                throw new ArgumentException("macOS capture coordinates must be relative to the first display and nonnegative.", nameof(rectangle));
            SKRect? crop = rectangle is { } region && region.Width > 0 ? region : null;
            if (rectangle is { Width: 0 } automatic && (automatic.Left != 0 || automatic.Top != 0))
                throw new ArgumentException("macOS automatic capture bounds require origin (0, 0).", nameof(rectangle));
            return new InputSpec("Capture screen 0:none", "avfoundation", false, options, Crop: crop);
        }

        string url, format;
        if (platform == OSPlatform.Windows) { url = "desktop"; format = "gdigrab"; }
        else if (platform == OSPlatform.Linux)
        {
            url = !string.IsNullOrWhiteSpace(display) ? display
                : throw new PlatformNotSupportedException("Linux desktop capture requires an X11 DISPLAY. Wayland capture is not supported.");
            format = "x11grab";
        }
        else throw new PlatformNotSupportedException($"Unsupported operating system: {platform}.");

        if (rectangle is { } rect)
        {
            if (rect.Width > 0 && rect.Height > 0)
                options["video_size"] = $"{(int)rect.Width}x{(int)rect.Height}";
            if (platform == OSPlatform.Windows)
            {
                options["offset_x"] = ((int)rect.Left).ToString(CultureInfo.InvariantCulture);
                options["offset_y"] = ((int)rect.Top).ToString(CultureInfo.InvariantCulture);
            }
            else url += $"+{(int)rect.Left},{(int)rect.Top}";
        }
        return new InputSpec(url, format, false, options);
    }
}
