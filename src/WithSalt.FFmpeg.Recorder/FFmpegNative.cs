using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using WithSalt.FFmpeg.Recorder.Native;
using WithSalt.FFmpeg.Recorder.Models;

namespace WithSalt.FFmpeg.Recorder;

/// <summary>FFmpeg is initialized automatically on the first recording and retained for process lifetime.</summary>
public static class FFmpegNative
{
    private static readonly object Gate = new();
    private static string? _libraryPath;
    private static bool _started, _initialized;
    private static ExceptionDispatchInfo? _failure;
    private static NativeFunctionResolver? _resolver;

    /// <summary>Enumerates cameras exposed by the current platform's FFmpeg input device.</summary>
    /// <remarks>Initializes FFmpeg; call <see cref="Configure(Action{FFmpegNativeOptions})"/> first when custom native libraries are needed.
    /// AVFoundation on macOS does not expose this FFmpeg enumeration API; select a camera by name, index or "default".</remarks>
    public static unsafe IReadOnlyList<CameraDevice> EnumerateCameraDevices()
    {
        if (OperatingSystem.IsMacOS())
            throw new NotSupportedException("FFmpeg AVFoundation does not implement avdevice_list_input_sources. " +
                "Select a camera with WithDeviceName(\"default\"), a video device index such as \"0\", or its AVFoundation name.");
        EnsureDevices();

        string format = NativePlatform.CameraFormat(NativePlatform.OS);
        AVInputFormat* inputFormat = ffmpeg.av_find_input_format(format);
        if (inputFormat == null)
            throw new NotSupportedException($"This FFmpeg build does not contain the '{format}' input device.");

        AVDeviceInfoList* deviceList = null;
        try
        {
            int result = ffmpeg.avdevice_list_input_sources(inputFormat, null, null, &deviceList);
            NativeError.Check(result, $"enumerate {format} camera devices");

            var devices = new List<CameraDevice>();
            if (deviceList == null) return devices;

            for (int i = 0; i < deviceList->nb_devices; i++)
            {
                AVDeviceInfo* device = deviceList->devices[i];
                if (device == null || !ProvidesVideo(device)) continue;

                string? name = Marshal.PtrToStringUTF8((nint)device->device_name);
                if (string.IsNullOrWhiteSpace(name)) continue;

                devices.Add(new CameraDevice(
                    name,
                    Marshal.PtrToStringUTF8((nint)device->device_description),
                    i == deviceList->default_device));
            }

            return devices.AsReadOnly();
        }
        finally
        {
            if (deviceList != null) ffmpeg.avdevice_free_list_devices(&deviceList);
        }
    }

    private static unsafe bool ProvidesVideo(AVDeviceInfo* device)
    {
        if (device->media_types == null || device->nb_media_types == 0) return true;
        for (int i = 0; i < device->nb_media_types; i++)
            if (device->media_types[i] == AVMediaType.AVMEDIA_TYPE_VIDEO) return true;
        return false;
    }

    /// <summary>Optional configuration. Must be called before the first recording starts.</summary>
    public static void Configure(Action<FFmpegNativeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        lock (Gate)
        {
            if (_started) throw new InvalidOperationException("FFmpeg configuration is fixed after the first recording starts.");
            var options = new FFmpegNativeOptions { LibraryPath = _libraryPath };
            configure(options);
            if (!string.IsNullOrWhiteSpace(options.LibraryPath))
            {
                var fullPath = Path.GetFullPath(options.LibraryPath);
                if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException(fullPath);
                _libraryPath = fullPath;
            }
            else _libraryPath = null;
        }
    }

    internal static void EnsureInitialized()
    {
        lock (Gate)
        {
            _failure?.Throw();
            if (_initialized) return;
            _started = true;
            try
            {
                _ = NativePlatform.RuntimeIdentifier;

                _resolver = new NativeFunctionResolver(_libraryPath);
                _resolver.LoadCore();
                DynamicallyLoadedBindings.FunctionResolver = _resolver;
                DynamicallyLoadedBindings.ThrowErrorIfFunctionNotFound = true;
                DynamicallyLoadedBindings.Initialize();
                NativeError.Check(ffmpeg.avformat_network_init(), "initialize network");
                _initialized = true;
            }
            catch (Exception exception)
            {
                _failure = ExceptionDispatchInfo.Capture(exception);
                throw;
            }
        }
    }

    internal static void EnsureDevices()
    {
        lock (Gate)
        {
            EnsureInitialized();
            _resolver!.LoadDevice();
            ffmpeg.avdevice_register_all();
        }
    }

    internal static nint ResolveFunctionPointer(string library, string function)
    {
        EnsureInitialized();
        return _resolver!.GetFunctionPointer(library, function);
    }
}
