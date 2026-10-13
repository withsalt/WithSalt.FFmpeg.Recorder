using FFmpeg.AutoGen;
using SkiaSharp;
using System.Threading.Channels;
using WithSalt.FFmpeg.Recorder.Models;
using WithSalt.FFmpeg.Recorder.Native;

internal static unsafe class PerformanceTests
{
    private static RecordingOptions Options(FpsMode mode = FpsMode.Passthrough, double? rate = null)
        => new([], null, null, rate, mode, OutputQuality.Medium, LatencyOptimizationLevel.None,
            null, null, TimeSpan.FromSeconds(1));

    internal static void BgraStridesAndOwnership()
    {
        using var source = new SyntheticFrame(3, 2, AVPixelFormat.AV_PIX_FMT_BGRA);
        AVFrame* frame = source.Frame;
        Check.True(frame->linesize[0] > 12, "Test must exercise padded input stride.");
        var first = new SKColor(30, 60, 90, 120);
        var second = new SKColor(130, 160, 190, 220);
        for (int row = 0; row < 2; row++)
        for (int col = 0; col < 3; col++)
        {
            byte* pixel = frame->data[0] + row * frame->linesize[0] + col * 4;
            var color = row == 0 ? first : second;
            pixel[0] = color.Blue; pixel[1] = color.Green; pixel[2] = color.Red; pixel[3] = color.Alpha;
        }
        using var converter = new FrameConverter(Options());
        using var owned = converter.Convert(frame);
        Check.Equal(first, owned.GetPixel(2, 0));
        Check.Equal(second, owned.GetPixel(2, 1));
        var borrowed = converter.ConvertBorrowed(frame);
        Check.Equal((nint)frame->data[0], borrowed.GetPixels());
        Check.Equal(second, borrowed.GetPixel(2, 1));
        owned.Erase(SKColors.Blue);
        Check.Equal(first, borrowed.GetPixel(2, 0));

        AVFrame reversed = *frame;
        reversed.data[0] += reversed.linesize[0];
        reversed.linesize[0] = -reversed.linesize[0];
        using var flipped = converter.Convert(&reversed);
        Check.Equal(second, flipped.GetPixel(2, 0));
        Check.Equal(first, flipped.GetPixel(2, 1));
        var reused = converter.ConvertBorrowed(&reversed);
        Check.True(ReferenceEquals(borrowed, reused));
        Check.True(reused.GetPixels() != (nint)reversed.data[0], "Negative stride must use a converted buffer.");
        Check.Equal(second, reused.GetPixel(2, 0));
        Check.Equal(first, reused.GetPixel(2, 1));
    }

    internal static void ConverterMetadataAndFormatChanges()
    {
        using var source = new SyntheticFrame(16, 8, AVPixelFormat.AV_PIX_FMT_YUV444P);
        AVFrame* frame = source.Frame;
        using var converter = new FrameConverter(Options());
        source.Fill(16, 128, 128);
        frame->color_range = AVColorRange.AVCOL_RANGE_MPEG;
        var borrowed = converter.ConvertBorrowed(frame);
        Check.True(borrowed.GetPixel(5, 5).Red < 3);
        nint pixels = borrowed.GetPixels();
        frame->color_range = AVColorRange.AVCOL_RANGE_JPEG;
        Check.True(ReferenceEquals(borrowed, converter.ConvertBorrowed(frame)));
        Check.Equal(pixels, borrowed.GetPixels());
        Check.True(Math.Abs(borrowed.GetPixel(5, 5).Red - 16) < 3, "Color range changes must invalidate cached settings.");

        source.Fill(81, 90, 240);
        frame->color_range = AVColorRange.AVCOL_RANGE_MPEG;
        frame->colorspace = AVColorSpace.AVCOL_SPC_SMPTE170M;
        converter.ConvertBorrowed(frame);
        var bt601 = borrowed.GetPixel(5, 5);
        frame->colorspace = AVColorSpace.AVCOL_SPC_BT709;
        converter.ConvertBorrowed(frame);
        var bt709 = borrowed.GetPixel(5, 5);
        Check.True(bt709.Green > bt601.Green + 15, "Color matrix changes must invalidate cached settings.");

        using var larger = new SyntheticFrame(32, 16, AVPixelFormat.AV_PIX_FMT_YUV420P);
        larger.Fill(235, 128, 128);
        Check.True(ReferenceEquals(borrowed, converter.ConvertBorrowed(larger.Frame)));
        Check.Equal(32, borrowed.Width);
        Check.Equal(16, borrowed.Height);
        Check.True(borrowed.GetPixel(5, 5).Red > 250);
        using var bgra = new SyntheticFrame(16, 8, AVPixelFormat.AV_PIX_FMT_BGRA);
        Check.True(ReferenceEquals(borrowed, converter.ConvertBorrowed(bgra.Frame)));
        Check.Equal((nint)bgra.Frame->data[0], borrowed.GetPixels());
        converter.ConvertBorrowed(frame); // Switch from the native view back to YUV conversion.
        Check.Equal(16, borrowed.Width);
        Check.Equal(bt709, borrowed.GetPixel(5, 5));
    }

    internal static void PooledPixelsRespectSkiaReferences()
    {
        using var source = new SyntheticFrame(16, 8, AVPixelFormat.AV_PIX_FMT_YUV444P);
        source.Fill(81, 90, 240);
        using var converter = new FrameConverter(Options());
        var original = converter.Convert(source.Frame);
        nint pixels = original.GetPixels();
        original.Dispose();
        original = converter.Convert(source.Frame);
        Check.Equal(pixels, original.GetPixels()); // Immediate disposal should reuse storage.
        using var subset = new SKBitmap();
        Check.True(original.ExtractSubset(subset, SKRectI.Create(0, 0, 8, 4)));
        var red = subset.GetPixel(3, 2);
        original.Dispose();
        source.Fill(145, 54, 34);
        using var green = converter.Convert(source.Frame);
        Check.True(green.GetPixels() != pixels, "Shared Skia pixels must not return to the pool early.");
        Check.Equal(red, subset.GetPixel(3, 2));
        var greenColor = green.GetPixel(3, 2);
        converter.Dispose(); // Outstanding owned pixels must outlive the recording session.
        Check.Equal(red, subset.GetPixel(3, 2));
        Check.Equal(greenColor, green.GetPixel(3, 2));
        Task.Run(green.Dispose).GetAwaiter().GetResult(); // Release after pool disposal on another thread.
    }

    internal static void AllocationFreeOutput()
    {
        using var source = new SyntheticFrame(16, 8, AVPixelFormat.AV_PIX_FMT_YUV420P);
        AVFrame* frame = source.Frame;
        foreach (var mode in new[] { FpsMode.Passthrough, FpsMode.Drop, FpsMode.CFR })
        foreach (bool borrowed in new[] { false, true })
        {
            var options = Options(mode, mode == FpsMode.Passthrough ? null : 30);
            if (borrowed) options = options with { BorrowedImageHandler = (_, _) => { } };
            using var output = new FrameOutput(options, CancellationToken.None);
            output.BeginInput(0, 0, null);
            for (int i = 0; i < 1000; i++) output.Accept(frame, i / 60.0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 1000; i < 11000; i++) output.Accept(frame, i / 60.0);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.True(bytes <= 256, $"{mode}, borrowed={borrowed}: allocated {bytes} bytes after warmup.");
        }
        // Backpressure must bypass conversion even when no valid pixel planes exist.
        AVFrame empty = default;
        using var rejected = new FrameOutput(Options() with
        {
            ImageHandler = (_, _) => throw new Exception("Rejected frame was delivered."),
            FrameFilter = _ => false
        }, CancellationToken.None);
        rejected.Accept(&empty, 0);
    }

    internal static void PooledPixelsAcrossProducerAndConsumer()
    {
        using var source = new SyntheticFrame(16, 8, AVPixelFormat.AV_PIX_FMT_BGRA);
        var channel = Channel.CreateBounded<(SKBitmap Bitmap, SKColor Color)>(4);
        var consumer = Task.Run(() =>
        {
            while (channel.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            while (channel.Reader.TryRead(out var item))
            {
                using (item.Bitmap)
                using (var subset = new SKBitmap())
                {
                    Check.True(item.Bitmap.ExtractSubset(subset, SKRectI.Create(0, 0, 8, 4)));
                    item.Bitmap.Dispose(); // The shared view must still keep the lease alive.
                    Thread.Yield(); // Race pixel releases against conversion and session disposal.
                    Check.Equal(item.Color, subset.GetPixel(3, 2));
                }
            }
        });
        try
        {
            using var converter = new FrameConverter(Options());
            for (int i = 0; i < 2000; i++)
            {
                var color = new SKColor((byte)i, (byte)(i >> 3), (byte)(i >> 6), 255);
                for (int row = 0; row < source.Frame->height; row++)
                for (int col = 0; col < source.Frame->width; col++)
                {
                    byte* pixel = source.Frame->data[0] + row * source.Frame->linesize[0] + col * 4;
                    pixel[0] = color.Blue; pixel[1] = color.Green; pixel[2] = color.Red; pixel[3] = color.Alpha;
                }
                var bitmap = converter.Convert(source.Frame);
                try { channel.Writer.WriteAsync((bitmap, color)).AsTask().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
                catch { bitmap.Dispose(); throw; }
            }
        }
        finally
        {
            channel.Writer.TryComplete();
            try { consumer.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
            finally { while (channel.Reader.TryRead(out var item)) item.Bitmap.Dispose(); }
        }
    }

    internal static void SchedulerMatchesReference()
    {
        const double epsilon = 1e-7;
        foreach (var mode in new[] { FpsMode.Passthrough, FpsMode.VFR, FpsMode.Drop, FpsMode.CFR })
        foreach (double rate in new[] { 1.0, 30000.0 / 1001, 60.0, 1000.0 })
        {
            var actual = new FrameRateScheduler(mode, rate);
            var random = new Random(173);
            long next = 0;
            double last = double.NegativeInfinity;
            bool hasPrevious = false;
            double time = 0;
            for (int i = 0; i < 2000; i++)
            {
                double timestamp = (i % 7) switch
                {
                    0 => Math.Max(0, time - 0.02),
                    1 => time,
                    2 => time + epsilon / 2,
                    3 => Math.Round(time * rate) / rate + epsilon,
                    _ => time + random.NextDouble() / 15
                };
                time = Math.Max(time, timestamp);
                var expected = new List<FrameEmission>();
                if (mode == FpsMode.Passthrough) expected.Add(new(timestamp, false));
                else if (timestamp > last + epsilon)
                {
                    last = timestamp;
                    if (mode == FpsMode.CFR)
                    {
                        while (next / rate < timestamp - epsilon) expected.Add(new(next++ / rate, hasPrevious));
                        if (next / rate <= timestamp + epsilon) expected.Add(new(next++ / rate, false));
                        hasPrevious = true;
                    }
                    else if (mode != FpsMode.Drop || timestamp + epsilon >= next / rate)
                    {
                        if (mode == FpsMode.Drop) next = checked((long)Math.Floor(timestamp * rate + epsilon) + 1);
                        expected.Add(new(timestamp, false));
                    }
                }
                Check.True(expected.SequenceEqual(actual.Push(timestamp)), $"Scheduler mismatch: {mode}, {rate}, {timestamp}.");
                if (i % 127 == 126)
                {
                    double end = time + 0.01;
                    expected.Clear();
                    if (mode == FpsMode.CFR && hasPrevious)
                        while (next / rate < end - epsilon) expected.Add(new(next++ / rate, true));
                    hasPrevious = false;
                    Check.True(expected.SequenceEqual(actual.FinishInput(end)), $"Input tail mismatch: {mode}, {rate}.");
                    time = end;
                }
            }
        }
    }
}

internal sealed unsafe class SyntheticFrame : IDisposable
{
    internal AVFrame* Frame { get; private set; }
    internal SyntheticFrame(int width, int height, AVPixelFormat format)
    {
        Frame = ffmpeg.av_frame_alloc();
        if (Frame == null) throw new OutOfMemoryException();
        try
        {
            Frame->width = width; Frame->height = height; Frame->format = (int)format;
            Frame->color_range = AVColorRange.AVCOL_RANGE_MPEG;
            NativeError.Check(ffmpeg.av_frame_get_buffer(Frame, 32), "allocate test frame");
            Fill(128, 128, 128);
        }
        catch { Dispose(); throw; }
    }
    internal void Fill(byte y, byte u, byte v)
    {
        for (uint plane = 0; plane < 4; plane++)
        {
            if (Frame->data[plane] == null) continue;
            int height = Frame->height;
            if (Frame->format == (int)AVPixelFormat.AV_PIX_FMT_YUV420P && plane > 0) height = (height + 1) / 2;
            new Span<byte>(Frame->data[plane], Frame->linesize[plane] * height).Fill(plane == 0 ? y : plane == 1 ? u : v);
        }
    }
    public void Dispose()
    {
        var frame = Frame; Frame = null;
        if (frame != null) ffmpeg.av_frame_free(&frame);
    }
}
