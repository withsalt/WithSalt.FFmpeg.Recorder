using System.Diagnostics;
using System.Text.Json;
using FFmpeg.AutoGen;
using WithSalt.FFmpeg.Recorder;
using WithSalt.FFmpeg.Recorder.Models;
using WithSalt.FFmpeg.Recorder.Native;

internal static unsafe class PerformanceBenchmarks
{
    internal static void Run()
    {
        FFmpegNative.EnsureInitialized();
        foreach (var mode in new[] { FpsMode.Passthrough, FpsMode.Drop, FpsMode.CFR })
        {
            var scheduler = new FrameRateScheduler(mode, mode == FpsMode.Passthrough ? null : 30);
            Measure($"scheduler-{mode}", 1_000_000, () =>
            {
                long emissions = 0;
                for (int i = 0; i < 1_000_000; i++)
                    foreach (var emission in scheduler.Push(i / 60.0)) emissions++;
                return emissions;
            });
        }
        foreach (var (width, height, count) in new[] { (1920, 1080, 600), (3840, 2160, 200) })
        foreach (var format in new[] { AVPixelFormat.AV_PIX_FMT_YUV420P, AVPixelFormat.AV_PIX_FMT_BGRA })
#if PERFORMANCE_BASELINE
        foreach (var delivery in new[] { "output" })
#else
        foreach (var delivery in new[] { "output", "borrowed", "filtered" })
#endif
        {
            AVFrame* frame = ffmpeg.av_frame_alloc();
            if (frame == null) throw new OutOfMemoryException();
            try
            {
                frame->width = width;
                frame->height = height;
                frame->format = (int)format;
                frame->color_range = AVColorRange.AVCOL_RANGE_MPEG;
                frame->colorspace = AVColorSpace.AVCOL_SPC_BT709;
                NativeError.Check(ffmpeg.av_frame_get_buffer(frame, 32), "allocate benchmark frame");
                for (uint plane = 0; plane < 4; plane++)
                    if (frame->data[plane] != null)
                        new Span<byte>(frame->data[plane], frame->linesize[plane] * (plane == 0 ? height : height / 2)).Fill(128);
                var options = new RecordingOptions([], null, null, null, FpsMode.Passthrough,
                    OutputQuality.Medium, LatencyOptimizationLevel.None, (_, bitmap) => bitmap.Dispose(), null, TimeSpan.FromSeconds(1));
#if !PERFORMANCE_BASELINE
                if (delivery == "borrowed") options = options with
                {
                    ImageHandler = null,
                    BorrowedImageHandler = (_, bitmap) => _checksum += *(byte*)bitmap.GetPixels()
                };
                if (delivery == "filtered") options = options with { FrameFilter = _ => false };
#endif
                using var output = new FrameOutput(options, CancellationToken.None);
                output.BeginInput(0, 0, null);
                for (int i = 0; i < 50; i++) output.Accept(frame, i / 60.0);
                var benchmarkFrame = frame;
                Measure($"{delivery}-{width}x{height}-{format}", count, () =>
                {
                    for (int i = 0; i < count; i++) output.Accept(benchmarkFrame, (i + 50) / 60.0);
                    return delivery == "filtered" ? 0 : count;
                });
            }
            finally { ffmpeg.av_frame_free(&frame); }
        }
    }

#if !PERFORMANCE_BASELINE
    private static long _checksum;
#endif

    private static void Measure(string name, int inputFrames, Func<long> action)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        long emitted = action();
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        double cpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            name, inputFrames, emitted, milliseconds, cpuMilliseconds,
            allocatedBytes = bytes, bytesPerInputFrame = (double)bytes / inputFrames,
            framesPerSecond = emitted / (milliseconds / 1000)
        }));
    }
}
