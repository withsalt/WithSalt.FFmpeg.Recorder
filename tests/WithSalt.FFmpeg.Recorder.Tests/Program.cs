using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using SkiaSharp;
using WithSalt.FFmpeg.Recorder;
using WithSalt.FFmpeg.Recorder.Interface;
using WithSalt.FFmpeg.Recorder.Models;
using WithSalt.FFmpeg.Recorder.Native;

// Executable integration suite so exactly the same tests can run from a self-contained
// publish on Linux/macOS without installing a .NET SDK, test host or an ffmpeg executable.
bool testDesktop = false;
bool benchmark = false;
for (int index = 0; index < args.Length; index++)
{
    if (args[index] == "--desktop") testDesktop = true;
    else if (args[index] == "--benchmark") benchmark = true;
    else if (args[index] == "--native-path" && index + 1 < args.Length)
    {
        string libraryPath = args[++index];
        FFmpegNative.Configure(options => options.LibraryPath = libraryPath);
    }
    else throw new ArgumentException("Use [--native-path <directory>] [--desktop] [--benchmark].");
}

if (benchmark) { PerformanceBenchmarks.Run(); return 0; }

using var fixtures = new Fixtures();
int passed = 0, failed = 0;
async Task Test(string name, Func<Task> test)
{
    try { await test(); Console.WriteLine($"PASS {name}"); passed++; }
    catch (Exception ex) { Console.Error.WriteLine($"FAIL {name}: {ex}"); failed++; }
}
Task Sync(Action action) { action(); return Task.CompletedTask; }
IFFmpegArgumentsBuilder FileInput(params string[] paths) => new FFmpegArgumentsBuilder()
    .WithFileInput().WithFiles(paths.Length == 0 ? [fixtures.Video] : paths).WithOutputSize(19, 11);
void VerifyBitmap(SKBitmap bitmap)
{
    Check.Equal(19, bitmap.Width);
    Check.Equal(11, bitmap.Height);
    Check.Equal(SKColorType.Bgra8888, bitmap.ColorType);
    Check.True(bitmap.RowBytes >= 19 * 4);
}
async Task<int> Count(IFFmpegArgumentsBuilder input)
{
    int count = 0;
    input.WithImageHandle((index, bitmap) =>
    {
        using (bitmap) { Check.Equal(++count, (int)index); VerifyBitmap(bitmap); }
    });
    await input.ProcessAsynchronously();
    return count;
}

await Test("Validation before native initialization", () => Sync(() =>
{
    Check.Throws<ArgumentOutOfRangeException>(() => FileInput().WithOutputFramerate(double.NaN));
    Check.Throws<ArgumentOutOfRangeException>(() => FileInput().WithOutputFramerate(double.PositiveInfinity));
    Check.Throws<ArgumentOutOfRangeException>(() => FileInput().WithOutputSize(int.MaxValue, 2));
    Check.Throws<ArgumentException>(() => new FFmpegArgumentsBuilder().WithFileInput().ProcessSynchronously());
    Check.Throws<ArgumentException>(() => FileInput().WithFpsMode(FpsMode.CFR).ProcessSynchronously());
    Check.Throws<ArgumentException>(() => FileInput().WithFpsMode(FpsMode.VFR).WithOutputFramerate(5).ProcessSynchronously());
    Check.Throws<ArgumentException>(() => FileInput().WithFpsMode(FpsMode.Passthrough).WithOutputFramerate(5).ProcessSynchronously());
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    Check.Throws<OperationCanceledException>(() => FileInput().ProcessSynchronously(cancelled.Token));
    Check.Throws<DirectoryNotFoundException>(() => FFmpegNative.Configure(o => o.LibraryPath = Path.Combine(fixtures.Folder, "missing")));
}));

await Test("Desktop automatic bounds and explicit capture options", () => Sync(() =>
{
    if (OperatingSystem.IsMacOS())
    {
        var desktop = new FFmpegArgumentsBuilder().WithDesktopInput().WithRectangle(SKRect.Create(3, 5, 64, 48));
        var create = desktop.GetType().GetMethod("CreateInputs", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var spec = ((InputSpec[])create.Invoke(desktop, null)!)[0];
        Check.Equal("avfoundation", spec.Format);
        Check.Equal("Capture screen 0:none", spec.Url);
        Check.Equal<SKRect?>(SKRect.Create(3, 5, 64, 48), spec.Crop);
        return;
    }
    string? previousDisplay = Environment.GetEnvironmentVariable("DISPLAY");
    if (!OperatingSystem.IsWindows()) Environment.SetEnvironmentVariable("DISPLAY", ":99.0");
    try
    {
        InputSpec Spec(SKRect? rectangle)
        {
            var input = new FFmpegArgumentsBuilder().WithDesktopInput().WithFramerate(60);
            if (rectangle.HasValue) input.WithRectangle(rectangle.Value);
            input.WithOutputSize(1920, 1080); // Output scaling must not change the capture bounds.
            var create = input.GetType().GetMethod("CreateInputs", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return ((InputSpec[])create.Invoke(input, null)!)[0];
        }
        foreach (SKRect? rectangle in new SKRect?[] { null, SKRect.Create(0, 0, 0, 0), SKRect.Create(-1920, -120, 0, 0) })
        {
            var spec = Spec(rectangle);
            Check.True(!spec.Options.ContainsKey("video_size"), "Automatic bounds must omit video_size.");
            Check.Equal("60", spec.Options["framerate"]);
            if (rectangle.HasValue)
            {
                if (OperatingSystem.IsWindows())
                {
                    Check.Equal(rectangle.Value.Left.ToString(System.Globalization.CultureInfo.InvariantCulture), spec.Options["offset_x"]);
                    Check.Equal(rectangle.Value.Top.ToString(System.Globalization.CultureInfo.InvariantCulture), spec.Options["offset_y"]);
                }
                else Check.Equal($":99.0+{(int)rectangle.Value.Left},{(int)rectangle.Value.Top}", spec.Url);
            }
        }
        var explicitSize = Spec(SKRect.Create(-640, -120, 640, 480));
        Check.Equal("640x480", explicitSize.Options["video_size"]);
        Check.Equal(OperatingSystem.IsWindows() ? "gdigrab" : "x11grab", explicitSize.Format);
        if (OperatingSystem.IsWindows())
        {
            Check.Equal("desktop", explicitSize.Url);
            Check.Equal("-640", explicitSize.Options["offset_x"]);
            Check.Equal("-120", explicitSize.Options["offset_y"]);
        }
        else Check.Equal(":99.0+-640,-120", explicitSize.Url);
    }
    finally
    {
        if (!OperatingSystem.IsWindows()) Environment.SetEnvironmentVariable("DISPLAY", previousDisplay);
    }
}));

await Test("Desktop rejects malformed or partially specified rectangles", () => Sync(() =>
{
    foreach (var rectangle in new[]
    {
        SKRect.Create(0, 0, 0, 480), SKRect.Create(0, 0, 640, 0),
        SKRect.Create(0, 0, -1, 480), SKRect.Create(0, 0, 640, -1),
        SKRect.Create(0.5f, 0, 640, 480), SKRect.Create(0, 0, 640.5f, 480),
        SKRect.Create(float.NaN, 0, 640, 480), SKRect.Create(0, 0, float.PositiveInfinity, 480),
        SKRect.Create(0, 0, 65536, 65536)
    })
        Check.Throws<ArgumentException>(() => new FFmpegArgumentsBuilder().WithDesktopInput().WithRectangle(rectangle));
}));

await Test("Supported runtime matrix and native library names", () => Sync(PlatformTests.RuntimeAndLibrarySelection));
await Test("FFmpeg ABI families reject unsupported and mixed versions", () => Sync(NativeResolverTests.AbiFamilies));
await Test("Native library discovery in application ffmpeg directories", () => Sync(NativeResolverTests.DirectoryDiscovery));
await Test("Platform device inputs and macOS capture regions", () => Sync(PlatformTests.DeviceInputs));
await Test("Detected native ABI and codec field offsets", () => Sync(NativeResolverTests.LoadedAbiAndCodecFields));
await Test("Native frame layout, cropping, retained pixels and output scaling", () => Sync(PlatformTests.FrameLayoutAndCropping));

await Test("Pre-canceled async execution returns a canceled task and preserves reuse", async () =>
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    var input = FileInput();
    var task = input.ProcessAsynchronously(cancellation.Token);
    await Check.ThrowsAsync<OperationCanceledException>(() => task);
    Check.True(task.IsCanceled && task.Exception is null);
    try { await task; }
    catch (OperationCanceledException ex) { Check.Equal(cancellation.Token, ex.CancellationToken); }
    Check.Equal(4, await Count(input));
});

await Test("Automatic loading, BGRA colors, stride and frame ownership", async () =>
{
    var frames = new List<SKBitmap>();
    try
    {
        await FileInput().WithImageHandle((index, bitmap) =>
        {
            Check.Equal(frames.Count + 1, (int)index);
            VerifyBitmap(bitmap);
            frames.Add(bitmap);
        }).ProcessAsynchronously();
        Check.Equal(4, frames.Count);
        Check.True(frames.Distinct().Count() == 4);
        var red = frames[0].GetPixel(5, 5);
        var green = frames[1].GetPixel(5, 5);
        Check.True(red.Red > 220 && red.Green < 25 && red.Blue < 25, $"Red conversion: {red}");
        Check.True(green.Green > 220 && green.Red < 25 && green.Blue < 25, $"Green conversion: {green}");
        Check.True(frames[2].GetPixel(5, 5).Red < 5, "Limited-range black must become RGB black.");
        Check.True(frames[3].GetPixel(5, 5).Red > 250, "Limited-range white must become RGB white.");
    }
    finally { frames.ForEach(bitmap => bitmap.Dispose()); }
});

await Test("Configuration freezes after initialization", () => Sync(() =>
    Check.Throws<InvalidOperationException>(() => FFmpegNative.Configure(_ => { }))));

await Test("BGRA padded/negative strides and copy/view ownership", () => Sync(PerformanceTests.BgraStridesAndOwnership));
await Test("Pooled pixels reserve a full SIMD tail beyond the payload", () => Sync(PerformanceTests.PooledPixelsReserveScalePadding));
await Test("Borrowed snapshots preserve pixels, alpha and independent lifetime", () => Sync(PerformanceTests.BorrowedSnapshotPreservesPixelsAndLifetime));
await Test("Reusable conversion invalidates size, format, range and matrix", () => Sync(PerformanceTests.ConverterMetadataAndFormatChanges));
await Test("Pooled pixels respect shared Skia references and cross-thread release", () => Sync(PerformanceTests.PooledPixelsRespectSkiaReferences));
await Test("Pooled pixels remain independent during concurrent conversion and release", () => Sync(PerformanceTests.PooledPixelsAcrossProducerAndConsumer));
await Test("Steady-state scheduling/borrowed output avoids managed allocation", () => Sync(PerformanceTests.AllocationFreeOutput));
await Test("Frame scheduler matches reference over fractional rates and boundaries", () => Sync(PerformanceTests.SchedulerMatchesReference));

await Test("Borrowed callback reuses bitmap and releases it at session end", async () =>
{
    SKBitmap? first = null;
    var colors = new List<SKColor>();
    await FileInput().WithImageViewHandle((_, bitmap) =>
    {
        first ??= bitmap;
        Check.True(ReferenceEquals(first, bitmap));
        VerifyBitmap(bitmap);
        colors.Add(bitmap.GetPixel(5, 5));
    }).ProcessAsynchronously();
    Check.Equal(4, colors.Count);
    Check.True(colors[0].Red > 220 && colors[1].Green > 220);
    Check.True(first!.Handle == nint.Zero, "Recorder must release the borrowed bitmap.");
});
await Test("Borrowed CFR repeats colors across filtered and unconverted input frames", async () =>
{
    var colors = new List<SKColor>();
    await FileInput().WithOutputFramerate(4).WithFrameFilter(index => index != 3)
        .WithImageViewHandle((_, bitmap) => colors.Add(bitmap.GetPixel(5, 5)))
        .ProcessAsynchronously();
    Check.Equal(7, colors.Count);
    Check.Equal(colors[0], colors[1]);
    Check.True(colors[2].Green > 220); // Current green was filtered; its duplicate must be converted.
    Check.True(colors[3].Red < 5 && colors[4].Red < 5);
    Check.True(colors[5].Red > 250 && colors[6].Red > 250);
    int count = 0;
    await FileInput().WithOutputFramerate(1).WithImageViewHandle((_, bitmap) =>
    {
        var color = bitmap.GetPixel(5, 5);
        Check.True(++count == 1 ? color.Red > 220 : color.Red < 5);
    }).ProcessAsynchronously();
    Check.Equal(2, count);
});
await Test("Frame filtering preserves output indices and progress", async () =>
{
    var indices = new List<long>();
    FFmpegProgress? last = null;
    await FileInput().WithFrameFilter(index => index % 2 == 0)
        .WithImageHandle((index, bitmap) => { using (bitmap) indices.Add(index); })
        .NotifyOnProgress(progress => last = progress, TimeSpan.FromDays(1)).ProcessAsynchronously();
    Check.True(indices.SequenceEqual(new long[] { 2, 4 }));
    Check.Equal(4L, last!.FrameIndex);
});
await Test("Borrowed and filter exceptions release buffers and preserve session reuse", async () =>
{
    SKBitmap? failed = null;
    var input = FileInput().WithImageViewHandle((_, bitmap) => { failed = bitmap; throw new CallbackFailure(); });
    await Check.ThrowsAsync<CallbackFailure>(() => input.ProcessAsynchronously());
    Check.True(failed!.Handle == nint.Zero);
    input.WithFrameFilter(_ => throw new CallbackFailure());
    await Check.ThrowsAsync<CallbackFailure>(() => input.ProcessAsynchronously());
    input.WithFrameFilter(_ => true);
    Check.Equal(4, await Count(input)); // WithImageHandle replaces the borrowed handler.
});
await Test("Borrowed cancellation and decoder thread configuration", async () =>
{
    Check.Throws<ArgumentOutOfRangeException>(() => FileInput().WithDecoderThreads(-1));
    using var cancellation = new CancellationTokenSource();
    SKBitmap? borrowed = null;
    var input = FileInput().WithDecoderThreads(1);
    input.WithImageViewHandle((_, bitmap) =>
    {
        borrowed = bitmap;
        Check.Throws<InvalidOperationException>(() => input.WithDecoderThreads(0));
        cancellation.Cancel();
    });
    await Check.ThrowsAsync<OperationCanceledException>(() => input.ProcessAsynchronously(cancellation.Token));
    Check.True(borrowed!.Handle == nint.Zero);
    Check.Equal(4, await Count(input.WithDecoderThreads(0)));
});

await Test("Native device library registration", () => Sync(FFmpegNative.EnsureDevices));

await Test("Native camera device enumeration", () => Sync(() =>
{
    if (OperatingSystem.IsMacOS())
    {
        Check.Throws<NotSupportedException>(() => FFmpegNative.EnumerateCameraDevices());
        return;
    }
    IReadOnlyList<CameraDevice> devices = FFmpegNative.EnumerateCameraDevices();
    Check.True(devices.All(device => !string.IsNullOrWhiteSpace(device.Name)));
    Check.True(devices.Count(device => device.IsDefault) <= 1);
}));

await Test("Output frames support Skia canvas drawing", async () =>
{
    await FileInput().WithImageHandle((_, bitmap) =>
    {
        using (bitmap)
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Blue);
            Check.Equal(SKColors.Blue, bitmap.GetPixel(0, 0));
        }
    }).ProcessAsynchronously();
});

foreach (var quality in Enum.GetValues<OutputQuality>())
    await Test($"Scaling quality {quality}", async () => Check.Equal(4, await Count(FileInput().WithOutputQuality(quality))));

await Test("Sync execution and sequential reuse", () => Sync(() =>
{
    int count = 0;
    var input = FileInput().WithImageHandle((_, bitmap) => { bitmap.Dispose(); count++; });
    input.ProcessSynchronously();
    input.ProcessSynchronously();
    Check.Equal(8, count);
}));

await Test("Concat files with different resolutions and continuous progress", async () =>
{
    var progress = new List<FFmpegProgress>();
    int count = await Count(FileInput(fixtures.Video, fixtures.SecondVideo).NotifyOnProgress(progress.Add, TimeSpan.Zero));
    Check.Equal(8, count);
    Check.Equal(8, progress.Count);
    Check.Equal(1, progress[^1].InputIndex);
    Check.True(progress[4].MediaTime > progress[3].MediaTime);
    Check.Equal(TimeSpan.Zero, progress[4].InputTime);
    Check.True(progress[0].InputDuration.HasValue);
});

await Test("CFR duplicates independent bitmap contents and flushes tail", async () =>
{
    var frames = new List<SKBitmap>();
    var progress = new List<FFmpegProgress>();
    try
    {
        await FileInput().WithOutputFramerate(4)
            .WithImageHandle((_, bitmap) => frames.Add(bitmap))
            .NotifyOnProgress(progress.Add, TimeSpan.Zero).ProcessAsynchronously();
        Check.Equal(8, frames.Count);
        Check.True(frames.Distinct().Count() == frames.Count);
        for (int i = 0; i < 8; i += 2) Check.Equal(frames[i].GetPixel(5, 5), frames[i + 1].GetPixel(5, 5));
        for (int i = 0; i < progress.Count; i++) Check.Equal(TimeSpan.FromSeconds(i / 4.0), progress[i].MediaTime);
    }
    finally { frames.ForEach(bitmap => bitmap.Dispose()); }
});
await Test("CFR downsampling", async () => Check.Equal(2, await Count(FileInput().WithOutputFramerate(1))));
await Test("Drop never duplicates", async () => Check.Equal(4, await Count(FileInput().WithFpsMode(FpsMode.Drop).WithOutputFramerate(4))));
await Test("Drop downsampling", async () => Check.Equal(2, await Count(FileInput().WithFpsMode(FpsMode.Drop).WithOutputFramerate(1))));
await Test("VFR", async () => Check.Equal(4, await Count(FileInput().WithFpsMode(FpsMode.VFR))));
await Test("Auto with rate", async () => Check.Equal(8, await Count(FileInput().WithFpsMode(FpsMode.Auto).WithOutputFramerate(4))));

await Test("VFR scheduler drops repeated and backward timestamps", () => Sync(() =>
{
    var scheduler = new FrameRateScheduler(FpsMode.VFR, null);
    var times = new[] { 0.0, 0.1, 0.1, 0.05, 0.4 }.SelectMany(time => scheduler.Push(time)).Select(e => e.Time).ToArray();
    Check.True(times.SequenceEqual(new[] { 0.0, 0.1, 0.4 }));
}));
await Test("CFR scheduler fills irregular PTS and long gaps", () => Sync(() =>
{
    var scheduler = new FrameRateScheduler(FpsMode.CFR, 4);
    var frames = new[] { 0.0, 0.1, 0.8, 1.0 }.SelectMany(time => scheduler.Push(time)).ToArray();
    Check.True(frames.Select(e => e.Time).SequenceEqual(new[] { 0.0, 0.25, 0.5, 0.75, 1.0 }));
    Check.True(frames.Skip(1).Take(3).All(e => e.Previous));
}));

foreach (var (name, expected) in new[] { ("bframes.mp4", 12), ("hevc10.mp4", 7), ("mjpeg.avi", 5) })
    await Test($"Decode {name} including EOF drain", async () =>
        Check.Equal(expected, await Count(FileInput(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)))));

await Test("Callback exception propagates and session is reusable", async () =>
{
    SKBitmap? failedBitmap = null;
    var input = FileInput().WithImageHandle((_, bitmap) => { failedBitmap = bitmap; throw new CallbackFailure(); });
    await Check.ThrowsAsync<CallbackFailure>(() => input.ProcessAsynchronously());
    Check.True(failedBitmap?.Handle == nint.Zero, "Failed callback bitmap was not disposed.");
    Check.Equal(4, await Count(input));
});
await Test("Progress exception propagates and session is reusable", async () =>
{
    var input = FileInput().NotifyOnProgress(_ => throw new CallbackFailure(), TimeSpan.Zero);
    await Check.ThrowsAsync<CallbackFailure>(() => input.ProcessAsynchronously());
    input.NotifyOnProgress(_ => { });
    Check.Equal(4, await Count(input));
});
await Test("Cancel in callback, reject concurrent run and mutation, then reuse", async () =>
{
    using var cancellation = new CancellationTokenSource();
    var input = FileInput();
    input.WithImageHandle((_, bitmap) =>
    {
        using (bitmap)
        {
            Check.Throws<InvalidOperationException>(() => input.ProcessSynchronously());
            Check.Throws<InvalidOperationException>(() => input.WithOutputSize(1, 1));
            cancellation.Cancel();
        }
    });
    var task = input.ProcessAsynchronously(cancellation.Token);
    await Check.ThrowsAsync<OperationCanceledException>(() => task);
    Check.True(task.IsCanceled && task.Exception is null, "Cancellation must not fault the recording task.");
    Check.Equal(4, await Count(input));
});
await Test("Corrupt input propagates an FFmpeg error", async () =>
{
    var bad = Path.Combine(fixtures.Folder, "bad.mp4");
    File.WriteAllText(bad, "not a media file");
    await Check.ThrowsAsync<FFmpegException>(() => FileInput(bad).ProcessAsynchronously());
});
await Test("Progress without allocating callback bitmaps", async () =>
{
    FFmpegProgress? last = null;
    await FileInput().NotifyOnProgress(p => last = p, TimeSpan.FromDays(1)).ProcessAsynchronously();
    Check.Equal(4L, last!.FrameIndex);
});

await Test("HTTP decoding through system native protocol", async () =>
{
    await using var server = new LoopbackServer(File.ReadAllBytes(fixtures.Video), stall: false);
    var input = new FFmpegArgumentsBuilder().WithStreamInput().WithHttp(server.Url).WithTimeout(3)
        .WithLatencyOptimizationLevel(LatencyOptimizationLevel.None).WithOutputSize(19, 11);
    Check.Equal(4, await Count(input));
});

await Test("HTTP HLS playlist and segment decoding", async () =>
{
    var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "hls"))
        .ToDictionary(path => "/" + Path.GetFileName(path), File.ReadAllBytes);
    await using var server = new LoopbackServer(files);
    var input = new FFmpegArgumentsBuilder().WithStreamInput().WithHttp(server.Url).WithTimeout(3)
        .WithLatencyOptimizationLevel(LatencyOptimizationLevel.None).WithOutputSize(19, 11);
    Check.Equal(12, await Count(input));
});

await Test("Cancellation interrupts blocked HTTP open", async () =>
{
    await using var server = new LoopbackServer(null, stall: true);
    using var cancellation = new CancellationTokenSource();
    var input = new FFmpegArgumentsBuilder().WithStreamInput().WithHttp(server.Url).WithOutputSize(19, 11);
    var task = input.ProcessAsynchronously(cancellation.Token);
    await server.Accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    cancellation.Cancel();
    await Check.ThrowsAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
    Check.True(task.IsCanceled && task.Exception is null, "Cancellation while opening must not fault the recording task.");
});

await Test("HTTP input deadline", async () =>
{
    await using var server = new LoopbackServer(null, stall: true);
    var input = new FFmpegArgumentsBuilder().WithStreamInput().WithHttp(server.Url).WithTimeout(1);
    var timer = Stopwatch.StartNew();
    var task = input.ProcessAsynchronously();
    await Check.ThrowsAsync<TimeoutException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
    Check.True(task.IsFaulted && timer.Elapsed < TimeSpan.FromSeconds(3), "The native deadline must complete the task before the test watchdog fires.");
});

foreach (string protocol in new[] { "rtsp", "rtmp" })
    await Test($"Cancellation interrupts blocked {protocol.ToUpperInvariant()} open", async () =>
    {
        await using var server = new LoopbackServer(null, stall: true);
        using var cancellation = new CancellationTokenSource();
        var uri = new UriBuilder(server.Url) { Scheme = protocol, Path = "/live/test" }.Uri;
        var streams = new FFmpegArgumentsBuilder().WithStreamInput();
        IFFmpegArgumentsBuilder input = protocol == "rtsp" ? streams.WithRtsp(uri) : streams.WithRtmp(uri);
        var task = input.ProcessAsynchronously(cancellation.Token);
        try
        {
            await server.Accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);
            cancellation.Cancel();
            await Check.ThrowsAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
            Check.True(task.IsCanceled && task.Exception is null, "Protocol cancellation must not fault the recording task.");
        }
        finally { cancellation.Cancel(); }
    });

await Test("Cancellation interrupts blocked HTTP read after frames", async () =>
{
    await using var server = new LoopbackServer(File.ReadAllBytes(fixtures.StreamVideo), stall: true);
    using var cancellation = new CancellationTokenSource();
    var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var input = new FFmpegArgumentsBuilder().WithStreamInput().WithHttp(server.Url).WithProbeSize(32)
        .WithLatencyOptimizationLevel(LatencyOptimizationLevel.None)
        .WithImageHandle((_, bitmap) => { bitmap.Dispose(); firstFrame.TrySetResult(); });
    var task = input.ProcessAsynchronously(cancellation.Token);
    try
    {
        await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        Check.True(!task.IsCompleted);
        cancellation.Cancel();
        await Check.ThrowsAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        Check.True(task.IsCanceled && task.Exception is null, "Cancellation while reading must not fault the recording task.");
    }
    finally { cancellation.Cancel(); }
});

await Test("Repeated session cleanup", async () =>
{
    for (int i = 0; i < 10; i++) Check.Equal(4, await Count(FileInput()));
    long before = Process.GetCurrentProcess().PrivateMemorySize64;
    for (int i = 0; i < 50; i++) Check.Equal(4, await Count(FileInput()));
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    long growth = Process.GetCurrentProcess().PrivateMemorySize64 - before;
    Console.WriteLine($"Native session stress: private memory change {growth / 1024 / 1024} MiB.");
    Check.True(growth < 64L * 1024 * 1024, "Excessive retained memory after repeated sessions.");
});

// Real desktop access is opt-in: CI/headless machines may have no capture device.
// Frames remain in memory and are disposed immediately; no screenshots are saved.
if (testDesktop)
{
    foreach (var (name, rectangle) in new (string, SKRect?)[]
    {
        ("default", null), ("zero dimensions", SKRect.Create(0, 0, 0, 0)),
        ("explicit region", SKRect.Create(0, 0, 64, 48))
    })
        await Test($"Real desktop capture: {name}, receive frames and stop", async () =>
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var desktop = new FFmpegArgumentsBuilder().WithDesktopInput().WithFramerate(60);
            if (rectangle.HasValue) desktop.WithRectangle(rectangle.Value);
            bool automatic = !rectangle.HasValue || rectangle.Value.Width == 0;
            int expectedWidth = automatic ? 1920 : 64, expectedHeight = automatic ? 1080 : 48;
            if (automatic) desktop.WithOutputSize(expectedWidth, expectedHeight);
            int frames = 0;
            desktop.WithOutputQuality(OutputQuality.High).WithImageHandle((_, bitmap) =>
            {
                using (bitmap)
                {
                    Check.Equal(expectedWidth, bitmap.Width);
                    Check.Equal(expectedHeight, bitmap.Height);
                    if (++frames == 3) cancellation.Cancel();
                }
            });
            var task = desktop.ProcessAsynchronously(cancellation.Token);
            await Check.ThrowsAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(15)));
            Check.Equal(3, frames);
            Check.True(task.IsCanceled && task.Exception is null);
        });
}

Console.WriteLine($"RESULT: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

sealed class CallbackFailure : Exception { }
static class Check
{
    internal static void True(bool condition, string message = "Assertion failed")
    { if (!condition) throw new Exception(message); }
    internal static void Equal<T>(T expected, T actual)
        => True(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}.");
    internal static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
    internal static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}

sealed class Fixtures : IDisposable
{
    internal string Folder { get; } = Path.Combine(Path.GetTempPath(), "withsalt-native-tests-" + Guid.NewGuid().ToString("N"));
    internal string Video => Path.Combine(Folder, "colors.y4m");
    internal string SecondVideo => Path.Combine(Folder, "colors-small.y4m");
    internal string StreamVideo => Path.Combine(Folder, "stream.y4m");
    internal Fixtures()
    {
        Directory.CreateDirectory(Folder);
        Write(Video, 16, 8);
        Write(SecondVideo, 8, 4);
        Write(StreamVideo, 16, 8, 64);
    }
    private static void Write(string path, int width, int height, int repeats = 1)
    {
        using var stream = File.Create(path);
        stream.Write(Encoding.ASCII.GetBytes($"YUV4MPEG2 W{width} H{height} F2:1 Ip A1:1 C444 XCOLORRANGE=LIMITED\n"));
        for (int i = 0; i < repeats; i++)
        foreach (var color in new[] { new byte[] { 81, 90, 240 }, new byte[] { 145, 54, 34 }, new byte[] { 16, 128, 128 }, new byte[] { 235, 128, 128 } })
        {
            stream.Write("FRAME\n"u8);
            foreach (byte component in color) stream.Write(Enumerable.Repeat(component, width * height).ToArray());
        }
    }
    public void Dispose() => Directory.Delete(Folder, recursive: true);
}

sealed class LoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    internal TaskCompletionSource Accepted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal string Url { get; }
    internal LoopbackServer(byte[]? payload, bool stall)
    {
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/video.y4m";
        _worker = Serve(payload, stall);
    }
    internal LoopbackServer(IReadOnlyDictionary<string, byte[]> files)
    {
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/index.m3u8";
        _worker = ServeFiles(files);
    }

    private async Task ServeFiles(IReadOnlyDictionary<string, byte[]> files)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                using var stream = client.GetStream();
                byte[] buffer = new byte[1];
                string request = "";
                while (!request.EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (await stream.ReadAsync(buffer, _stop.Token) == 0) break;
                    request += (char)buffer[0];
                    if (request.Length > 16384) throw new InvalidDataException("Oversized test request.");
                }
                string path = request.Split(' ')[1];
                var payload = files[path];
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n"), _stop.Token);
                await stream.WriteAsync(payload, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private async Task Serve(byte[]? payload, bool stall)
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            Accepted.TrySetResult();
            using var stream = client.GetStream();
            byte[] buffer = new byte[1];
            string request = "";
            while (!request.EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await stream.ReadAsync(buffer, _stop.Token) == 0) return;
                request += (char)buffer[0];
            }
            if (payload is not null)
            {
                int length = payload.Length + (stall ? 1000000 : 0);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {length}\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n"), _stop.Token);
                await stream.WriteAsync(payload, _stop.Token);
                await stream.FlushAsync(_stop.Token);
            }
            if (stall) await Task.Delay(Timeout.Infinite, _stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (IOException) { }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        await _worker;
        _stop.Dispose();
    }
}
