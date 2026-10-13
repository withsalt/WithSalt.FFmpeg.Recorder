using SkiaSharp;
using WithSalt.FFmpeg.Recorder.Interface;
using WithSalt.FFmpeg.Recorder.Native;

namespace WithSalt.FFmpeg.Recorder.Builder;

internal sealed class DesktopInputArgumentsBuilder : BaseInputArgumentsBuilder, IDesktopInputArgumentsBuilder
{
    private SKRect? _rectangle;
    private double? _framerate;
    public IDesktopInputArgumentsBuilder WithFramerate(double framerate = 30)
    {
        ValidateFramerate(framerate);
        Change(() => _framerate = framerate);
        return this;
    }
    public IDesktopInputArgumentsBuilder WithRectangle(SKRect rectangle)
    {
        if (!float.IsFinite(rectangle.Left) || !float.IsFinite(rectangle.Top)
            || !float.IsFinite(rectangle.Width) || !float.IsFinite(rectangle.Height)
            || rectangle.Width < 0 || rectangle.Height < 0
            || (rectangle.Width == 0) != (rectangle.Height == 0)
            || rectangle.Width * (double)rectangle.Height > int.MaxValue / 4
            || rectangle.Left < int.MinValue || rectangle.Left >= int.MaxValue
            || rectangle.Top < int.MinValue || rectangle.Top >= int.MaxValue
            || rectangle.Left != MathF.Truncate(rectangle.Left) || rectangle.Top != MathF.Truncate(rectangle.Top)
            || rectangle.Width != MathF.Truncate(rectangle.Width) || rectangle.Height != MathF.Truncate(rectangle.Height))
            throw new ArgumentException("Capture rectangle requires finite integer pixel coordinates and dimensions that are both positive or both zero for automatic sizing.", nameof(rectangle));
        Change(() => _rectangle = rectangle);
        return this;
    }
    protected override InputSpec[] CreateInputs()
        => [DeviceInputFactory.Desktop(NativePlatform.OS, _rectangle, _framerate, Environment.GetEnvironmentVariable("DISPLAY"))];
}
