using WithSalt.FFmpeg.Recorder.Interface;
using WithSalt.FFmpeg.Recorder.Native;

namespace WithSalt.FFmpeg.Recorder.Builder;

internal sealed class CameraInputArgumentsBuilder : BaseInputArgumentsBuilder, ICameraInputArgumentsBuilder
{
    private string? _device;
    private uint _width = 1280, _height = 720;
    private double? _framerate;
    public ICameraInputArgumentsBuilder WithDeviceName(string deviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        Change(() => _device = deviceName);
        return this;
    }
    public ICameraInputArgumentsBuilder WithVideoSize(uint width, uint height)
    {
        if (width == 0 || height == 0 || (ulong)width * height > int.MaxValue / 4)
            throw new ArgumentOutOfRangeException(nameof(width), "Invalid camera resolution.");
        Change(() => { _width = width; _height = height; });
        return this;
    }
    public ICameraInputArgumentsBuilder WithFramerate(double framerate = 30)
    {
        ValidateFramerate(framerate);
        Change(() => _framerate = framerate);
        return this;
    }
    protected override InputSpec[] CreateInputs()
    {
        if (_device is null) throw new ArgumentException("Specify a camera with WithDeviceName.");
        return [DeviceInputFactory.Camera(NativePlatform.OS, _device, _width, _height, _framerate)];
    }
}
