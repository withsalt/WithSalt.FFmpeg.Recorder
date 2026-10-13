using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using DesktopAppDemo.Models;
using DesktopAppDemo.Utils;
using DesktopAppDemo.Views;
using Microsoft.Extensions.Logging;
using SkiaSharp;

// Exercise the real demo window and native HTTP cancellation while the UI dispatcher
// stays responsive. Reflection avoids exposing demo internals as product APIs.
var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
AppBuilder.Configure<Application>().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithLifetime(lifetime);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole());
int failures = 0;
int passed = 0;
Dispatcher.UIThread.Post(async () =>
{
    try
    {
        foreach (string text in new[] { "0,0", "0,0,,", "0,0,0,0", " 0 , 0 , , " })
        {
            if (!ScreenParamsHelper.TryParse(text, out var message, out var rectangle)
                || rectangle != SkiaSharp.SKRect.Create(0, 0, 0, 0))
                throw new Exception($"Default desktop bounds failed for '{text}': {message}");
        }
        if (!ScreenParamsHelper.TryParse("-1920,-120,800,600", out _, out var area)
            || area != SkiaSharp.SKRect.Create(-1920, -120, 800, 600))
            throw new Exception("Explicit desktop bounds did not preserve negative monitor coordinates.");
        passed++;
        Console.WriteLine("PASS desktop automatic and explicit range parsing");

        foreach (string text in new[] { "", "abc,0", "0,abc", ",0", "0,,0", "0,0,800", "0,0,800,",
            "0,0,,600", "0,0,0,600", "0,0,800,0", "0,0,-1,600", "0,0,0,0,0",
            "0.5,0", "2147483647,0", "0,0,65536,65536" })
        {
            if (ScreenParamsHelper.TryParse(text, out var message, out var rectangle)
                || rectangle.HasValue || string.IsNullOrWhiteSpace(message))
                throw new Exception($"Invalid desktop bounds were accepted: '{text}'");
        }
        passed++;
        Console.WriteLine("PASS desktop invalid range diagnostics");

        var desktopType = typeof(MainWindow).Assembly.GetType("DesktopAppDemo.ViewModels.MainWindowViewModel")!;
        var desktopLogger = Activator.CreateInstance(typeof(Logger<>).MakeGenericType(desktopType), loggerFactory)!;
        var desktopModel = Activator.CreateInstance(desktopType, desktopLogger)!;
        desktopType.GetProperty("SelectOutputResolution")!.SetValue(desktopModel, "1920x1080");
        foreach (string text in new[] { "0,0", "0,0,,", "0,0,0,0", "0,0,800,600" })
        {
            desktopType.GetProperty("DesktopSource")!.SetValue(desktopModel, text);
            desktopType.GetMethod("BuildDesktopProcessor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(desktopModel, null);
        }
        passed++;
        Console.WriteLine("PASS demo desktop configuration with default and explicit bounds");

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var channel = (Channel<SKBitmap>)desktopType.GetField("_frameChannel", flags)!.GetValue(desktopModel)!;
        var enqueue = desktopType.GetMethod("OnImageArrived", flags)!;
        using (var oldest = new SKBitmap(2, 2))
        using (var latest = new SKBitmap(2, 2))
        {
            enqueue.Invoke(desktopModel, [1L, oldest]);
            enqueue.Invoke(desktopModel, [2L, latest]);
            if (oldest.Handle != IntPtr.Zero || latest.Handle == IntPtr.Zero
                || !channel.Reader.TryRead(out var queued) || !ReferenceEquals(queued, latest)
                || channel.Reader.TryRead(out _))
                throw new Exception("Preview must retain only the newest frame and dispose evicted pixels.");
        }
        passed++;
        Console.WriteLine("PASS preview queue disposes evicted frames");

        using (var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul)))
        {
            bitmap.Erase(new SKColor(200, 100, 50, 128));
            desktopType.GetMethod("DrawSKBitmapToImage", flags)!.Invoke(desktopModel, [bitmap]);
            var image = (Avalonia.Media.Imaging.WriteableBitmap)desktopType.GetProperty("Image")!.GetValue(desktopModel)!;
            using var locked = image.Lock();
            byte[] pixel = new byte[4];
            Marshal.Copy(locked.Address, pixel, 0, pixel.Length);
            if (!pixel.SequenceEqual(new byte[] { 25, 50, 100, 128 }))
                throw new Exception($"Preview must premultiply BGRA pixels: {string.Join(',', pixel)}.");
        }
        passed++;
        Console.WriteLine("PASS preview copy preserves premultiplied alpha");

        var nextPreview = desktopType.GetField("_nextPreviewTimestamp", flags)!;
        var admit = desktopType.GetMethod("ShouldConvertPreviewFrame", flags)!;
        nextPreview.SetValue(desktopModel, long.MaxValue);
        if ((bool)admit.Invoke(desktopModel, [1L])!)
            throw new Exception("Preview frames before the deadline must skip conversion.");
        nextPreview.SetValue(desktopModel, 0L);
        if (!(bool)admit.Invoke(desktopModel, [2L])! || (long)nextPreview.GetValue(desktopModel)! <= 0)
            throw new Exception("An admitted preview must advance the conversion deadline.");
        passed++;
        Console.WriteLine("PASS preview filters before pixel conversion");
        await ((Task)desktopType.GetMethod("ClosingAsync")!.Invoke(desktopModel, null)!).WaitAsync(deadline.Token);

        foreach (var scenario in new[] { "active-close", "active-exit", "starting-close", "stopping-close", "idle" })
        {
            var type = typeof(MainWindow).Assembly.GetType("DesktopAppDemo.ViewModels.MainWindowViewModel")!;
            var loggerType = typeof(Logger<>).MakeGenericType(type);
            var logger = Activator.CreateInstance(loggerType, loggerFactory)!;
            var viewModel = Activator.CreateInstance(type, logger)!;
            void Set(string name, object value) => type.GetProperty(name)!.SetValue(viewModel, value);
            object? GetField(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewModel);
            Task Start() => (Task)type.GetMethod("Start")!.Invoke(viewModel, null)!;
            var window = new MainWindow { DataContext = viewModel };
            lifetime.MainWindow = window;
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int closeCount = 0;
            window.Closed += (_, _) => { closeCount++; closed.TrySetResult(); };
            window.Show();
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Task? start = null;
            Task? stop = null;
            Task? recording = null;
            Task? consumer = null;
            TcpClient? client = null;
            if (scenario != "idle")
            {
                var inputs = (IEnumerable<InputTypeItem>)type.GetProperty("InputTypeList")!.GetValue(viewModel)!;
                Set("InputType", inputs.Single(input => input.Id == 2));
                Set("SelectOutputResolution", "1280x720");
                Set("StreamSource", $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/stall");
                start = Start();
                client = await listener.AcceptTcpClientAsync(deadline.Token);
                recording = (Task?)GetField("_mainTask");
                consumer = (Task?)GetField("_frameConsumerTask");
                if (recording is null || recording.IsCompleted) throw new Exception("Recording did not start.");
                if (scenario != "starting-close") await start.WaitAsync(deadline.Token);
                if (scenario == "stopping-close") stop = Start();
            }
            if (scenario == "active-exit") type.GetMethod("Exit")!.Invoke(viewModel, null);
            else window.Close();
            window.Close(); // Repeated close requests must share one cleanup.
            await closed.Task.WaitAsync(deadline.Token);
            if (start is not null) await start.WaitAsync(deadline.Token);
            if (stop is not null) await stop.WaitAsync(deadline.Token);
            if (recording is not null && !recording.IsCompletedSuccessfully) throw new Exception("Recording cancellation escaped the demo.");
            if (consumer is not null && !consumer.IsCompletedSuccessfully) throw new Exception("Frame consumer did not stop cleanly.");
            if (GetField("_cts") is not null || GetField("_mainTask") is not null || GetField("_frameConsumerTask") is not null)
                throw new Exception("Session fields were not cleared.");
            if (closeCount != 1) throw new Exception("Window closed more than once.");
            client?.Dispose();
            passed++;
            Console.WriteLine($"PASS desktop shutdown {scenario}");
        }
    }
    catch (Exception ex) { failures++; Console.Error.WriteLine(ex); }
    finally { deadline.Cancel(); }
});
Dispatcher.UIThread.MainLoop(deadline.Token);
if (passed + failures < 11)
{
    failures++;
    Console.Error.WriteLine("FAIL desktop shutdown watchdog expired before all scenarios completed.");
}
Console.WriteLine($"RESULT: desktop shutdown {passed} passed, {failures} failed");
return failures == 0 && passed == 11 ? 0 : 1;
