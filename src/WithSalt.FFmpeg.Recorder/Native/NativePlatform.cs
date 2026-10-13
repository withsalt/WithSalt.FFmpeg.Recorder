using System.Runtime.InteropServices;

namespace WithSalt.FFmpeg.Recorder.Native;

internal static class NativePlatform
{
    internal static OSPlatform OS => OperatingSystem.IsWindows() ? OSPlatform.Windows
        : OperatingSystem.IsLinux() ? OSPlatform.Linux
        : OperatingSystem.IsMacOS() ? OSPlatform.OSX
        : throw new PlatformNotSupportedException("Supported operating systems: Windows, Linux and macOS.");

    internal static string RuntimeIdentifier => GetRuntimeIdentifier(OS, RuntimeInformation.ProcessArchitecture);

    internal static string GetRuntimeIdentifier(OSPlatform platform, Architecture architecture)
    {
        if (platform == OSPlatform.Windows && architecture == Architecture.X64) return "win-x64";
        if (platform == OSPlatform.OSX)
        {
            if (architecture == Architecture.X64) return "osx-x64";
            if (architecture == Architecture.Arm64) return "osx-arm64";
        }
        if (platform == OSPlatform.Linux)
        {
            if (architecture == Architecture.X64) return "linux-x64";
            if (architecture == Architecture.Arm) return "linux-arm";
            if (architecture == Architecture.Arm64) return "linux-arm64";
            if (architecture == Architecture.LoongArch64) return "linux-loongarch64";
        }
        throw new PlatformNotSupportedException(
            $"Unsupported platform {platform}/{architecture}. Supported runtimes: " +
            "win-x64, osx-x64, osx-arm64, linux-x64, linux-arm, linux-arm64, linux-loongarch64.");
    }

    internal static string LibraryFileName(string library, int version, OSPlatform platform)
    {
        if (platform == OSPlatform.Windows) return $"{library}-{version}.dll";
        if (platform == OSPlatform.OSX) return $"lib{library}.{version}.dylib";
        if (platform == OSPlatform.Linux) return $"lib{library}.so.{version}";
        throw new PlatformNotSupportedException($"Unsupported operating system: {platform}.");
    }

    internal static string LibraryFileName(string library, OSPlatform platform)
    {
        if (platform == OSPlatform.Windows) return $"{library}.dll";
        if (platform == OSPlatform.OSX) return $"lib{library}.dylib";
        if (platform == OSPlatform.Linux) return $"lib{library}.so";
        throw new PlatformNotSupportedException($"Unsupported operating system: {platform}.");
    }

    internal static string CameraFormat(OSPlatform platform)
    {
        if (platform == OSPlatform.Windows) return "dshow";
        if (platform == OSPlatform.OSX) return "avfoundation";
        if (platform == OSPlatform.Linux) return "v4l2";
        throw new PlatformNotSupportedException($"Unsupported operating system: {platform}.");
    }
}
