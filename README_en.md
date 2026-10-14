# WithSalt.FFmpeg.Recorder

Decode video through FFmpeg and output a sequence of SkiaSharp `SKBitmap` frames. Inputs include local files, sequential files, cameras, desktop capture, RTSP, HTTP/HLS and RTMP.

[中文](README.md)

## Installation and setup

```sh
dotnet add package WithSalt.FFmpeg.Recorder
```

`WithSalt.FFmpeg.Recorder` does not include FFmpeg native libraries. The consuming project must select and install a native library package such as `FFmpeg.LGPL`, `FFmpeg.GPL` or `Sdcb.FFmpeg.runtime.windows-x64`. NuGet provides many such packages; choose one built against FFmpeg 7.x or later.
The following example uses `FFmpeg.LGPL`:

```sh
dotnet add package FFmpeg.LGPL
```

On Windows, FFmpeg native libraries generally need to be installed through a package. On Linux/macOS, use your distribution's package manager or Homebrew to install shared libraries and dependencies matching the process architecture.
For example, on Ubuntu, Debian or Deepin:

```sh
apt install ffmpeg

# dotnet runtime dependencies, Libicu requires installing different versions on different system versions: libicu76 supports Debian 13x, libicu78 supports Ubuntu 26.x
apt install ca-certificates libc6 libgcc-s1 libgssapi-krb5-2 libicu76 libssl3t64 libstdc++6 tzdata zlib1g
```

When FFmpeg libraries are present in the application directory, they are loaded from the same directory; missing libraries cause an error. You can also explicitly configure the native library path before the first execution:

```csharp
FFmpegNative.Configure(options =>
{
    options.LibraryPath = "/opt/ffmpeg/lib"; // macOS example: /opt/homebrew/opt/ffmpeg/lib
});
```

Third-party packages built with MinGW may also require `libstdc++-6.dll` and its dependencies. Missing dependencies cause loading to fail even when the FFmpeg DLLs are present.  
Dependencies in custom Linux/macOS library directories must be discoverable by the system dynamic linker.  

Additionally, if you need to use SkiaSharp in a Linux environment, you need an installation package:  
```sh
dotnet add package SkiaSharp.NativeAssets.Linux.NoDependencies
```

## Basic usage

```csharp
using SkiaSharp;
using WithSalt.FFmpeg.Recorder;
using WithSalt.FFmpeg.Recorder.Models;

using var cancellation = new CancellationTokenSource();

var recording = new FFmpegArgumentsBuilder()
    .WithFileInput()
    .WithFiles("01.mp4", "02.mp4")
    .WithOutputSize(1280, 720)
    .WithOutputQuality(OutputQuality.High)
    .WithImageHandle((frameIndex, bitmap) =>
    {
        using (bitmap)
        {
            // Process the bitmap synchronously, or transfer it to your bounded queue.
            // The queue consumer must dispose it; dispose here if enqueueing fails.
        }
    })
    .NotifyOnProgress(progress =>
    {
        Console.WriteLine($"{progress.FrameIndex}: {progress.MediaTime}");
    }, TimeSpan.FromSeconds(1));

try
{
    await recording.ProcessAsynchronously(cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    // Capture has stopped and the native session has been released.
}

// To stop an active run: cancellation.Cancel();
// Synchronous alternative: recording.ProcessSynchronously(cancellation.Token);
```

## High-performance capture

The default `WithImageHandle` callback preserves independent ownership. The library reuses released native pixel memory and caches at most one idle buffer per session; pixels retained by callers or shared Skia references are never overwritten. Use a bounded queue for asynchronous processing and dispose consumed or discarded bitmaps promptly.

For synchronous readers, `WithImageViewHandle` provides a read-only image view valid only during the callback and reuses a bitmap and its conversion buffer. Same-size BGRA input with a positive stride uses a view of the decoded pixels, removing the full-frame copy:

```csharp
var recording = new FFmpegArgumentsBuilder()
    .WithDesktopInput()
    .WithRectangle(SKRect.Create(0, 0, 1920, 1080))
    .WithFramerate(60)
    .WithFpsMode(FpsMode.Drop)
    .WithOutputFramerate(30)
    .WithDecoderThreads(1)
    .WithImageViewHandle((index, bitmap) =>
    {
        // Read synchronously here. The bitmap is valid only during this callback.
        // Do not dispose, modify, retain or queue it. Use WithImageHandle for ownership.
    });
await recording.ProcessAsynchronously(cancellation.Token);
```

The two image callbacks replace each other; the last configuration takes effect. Borrowed callbacks and frame filters run on the decoding thread; exceptions stop the session.

For asynchronous processing or retaining images, prefer `WithImageHandle` to receive independently owned bitmaps. If you must copy inside a borrowed callback, use `bitmap.Resize(bitmap.Info, SKSamplingOptions.Default)` to preserve dimensions and format, check for a null result, and have the final consumer dispose the copy. SkiaSharp 3.119.0 and 4.153.1 were verified to delay disposal of a temporary shader in `Copy()` / `CopyTo()`, allowing memory to grow per frame even when each copy is disposed. See [upstream #2915](https://github.com/mono/SkiaSharp/issues/2915) and [this project's validation record](docs/memory-growth-review-2026-10-14.md).

`WithFrameFilter(index => index % 2 == 0)` filters output frames **before** image allocation and conversion, allowing on-demand sampling or deciding whether to accept the next frame based on consumer load. Returning false still advances frame indexes and progress, so indexes delivered to image callbacks may have gaps. Filtering does not skip compressed-video decoding and is not invoked without an image callback.

Reduce source dimensions/FPS and specify the desktop region first to avoid capturing the entire virtual desktop and then scaling it down. Use `FpsMode.Drop` when only the latest image is needed and `OutputQuality.Low` when scaling cost matters. `WithDecoderThreads(0)` lets FFmpeg choose the thread count; `1` reduces thread/frame-buffer overhead. Fewer threads may reduce compressed-video throughput, so test with your actual input. Live latency presets allow only slice threading to avoid additional frame-threading delay.

Three microbenchmark runs on the same machine measured a roughly 3–12× speedup in the conversion path with the default owning callback, with managed allocations dropping from about 448 to 112 bytes per frame. The borrowed path allocated 0 managed bytes per frame after warmup.

## Input configuration

Each configuration below can be extended with `WithImageHandle`, `WithOutputSize` and an execution method:

```csharp
var rtsp = new FFmpegArgumentsBuilder().WithStreamInput()
    .WithRtsp("rtsp://host/stream")
    .WithTcp() // TCP by default; use WithUdp() for UDP
    .WithTimeout(10)
    .WithProbeSize(1024 * 1024);

var http = new FFmpegArgumentsBuilder().WithStreamInput()
    .WithHttp("https://host/playlist.m3u8")
    .WithTimeout(10);

var rtmp = new FFmpegArgumentsBuilder().WithStreamInput()
    .WithRtmp("rtmp://host/live/stream")
    .WithLiveType(RtmpLiveType.Live);

var camera = new FFmpegArgumentsBuilder().WithCameraInput()
    .WithDeviceName("Camera name") // Linux: /dev/video0; macOS: default, 0 or an AVFoundation name
    .WithVideoSize(1280, 720)
    .WithFramerate(30);

var desktop = new FFmpegArgumentsBuilder().WithDesktopInput()
    .WithRectangle(SKRect.Create(0, 0, 1280, 720))
    .WithFramerate(30);
```

Call `FFmpegNative.EnumerateCameraDevices()` to enumerate cameras through FFmpeg's native `avdevice_list_input_sources` API. Pass the returned `Name` directly to `WithDeviceName`; Windows uses DirectShow and Linux uses V4L2.

FFmpeg AVFoundation on macOS does not implement this enumeration API; `EnumerateCameraDevices()` throws `NotSupportedException`. Select a camera with `WithDeviceName("default")`, a video device index such as `"0"`, or its AVFoundation name. Supply only the video selector; Recorder appends `:none` to disable audio.

Omit `WithRectangle`, or pass `SKRect.Create(0, 0, 0, 0)`, to use the native device's automatic capture bounds. Desktop Demo accepts `0,0`, `0,0,,`, and `0,0,0,0`; an explicit `x,y,width,height` area requires both dimensions to be positive integers. On Windows, automatic bounds cover the entire virtual desktop, potentially spanning multiple monitors. `WithOutputSize` scales the output and does not limit the captured region. Supply an explicit position and size to capture one monitor or a smaller area.

On macOS, desktop capture defaults to the first AVFoundation display (`Capture screen 0:none`). `WithRectangle` uses nonnegative pixel coordinates within that display and must fit its captured dimensions; cropping precedes `WithOutputSize` scaling. Automatic bounds require origin `(0, 0)`. For Retina displays, use the captured frame's pixel dimensions rather than logical point dimensions.

Pass device names directly without command-line quoting. Input availability depends on device drivers, the protocols/decoders enabled in the FFmpeg build and TLS support.

`WithTimeout` is measured in seconds and applies to opening, probing and each blocking read; zero disables the deadline. Cancellation uses FFmpeg's interrupt callback to interrupt native I/O that supports it. User callbacks, system drivers and third-party protocol implementations that ignore interrupts cannot be forcibly terminated. Await the execution task before treating capture as stopped.

## Output semantics

| Setting | Behavior |
| --- | --- |
| Passthrough | Emit each decoded frame once; progress uses normalized media time |
| VFR | Preserve variable frame intervals and drop duplicate/regressing timestamps; output framerate cannot be specified |
| CFR | Sample at the target rate, duplicating the previous frame or dropping frames as needed; output framerate is required |
| Auto | CFR when an output rate is specified; otherwise VFR |
| Drop | Never duplicate; cap the rate if specified, otherwise behave as VFR |
| WithOutputFramerate | Select CFR if no mode is specified; an explicit Passthrough/VFR mode is mutually exclusive with an output framerate |
| OutputQuality.High / Medium / Low | Lanczos / Bilinear / Fast Bilinear scaling. All levels convert directly to BGRA without a lossy MJPEG intermediate |

Framerates must be finite, greater than 0 and at most 1000. File frame extraction runs as fast as possible without wall-clock playback pacing; CFR describes the media timeline and does not guarantee callbacks arrive at equal real-time intervals. Hardware acceleration and HDR tone mapping are not implemented; higher-bit-depth input is converted to 8-bit BGRA.

Non-file inputs retain the High low-latency preset by default. Medium uses nobuffer, discardcorrupt, low_delay and a smaller probe window; High further reduces probing, enables direct I/O and adjusts RTSP buffering. Aggressive settings may prevent input detection; try `WithLatencyOptimizationLevel(LatencyOptimizationLevel.None)` first if problems occur. An explicit `WithProbeSize` overrides the preset value.

`NotifyOnProgress` reports `FFmpegProgress`: frame count, media time across files, current input index, current input media time and current file duration (if known). Live inputs do not provide duration; sequential-file inputs do not open all files in advance to calculate an overall percentage. Callbacks are throttled by elapsed real time, with a final report for the last frame on normal completion.

## Development and validation

```sh
dotnet build src/WithSalt.FFmpeg.Recorder.slnx -c Release
dotnet run --project tests/WithSalt.FFmpeg.Recorder.Tests -f net8.0 -c Release
dotnet run --project src/Demos/ConsoleAppDemo -c Release -- --file path/to/video.mp4
```

The tests are an executable integration suite: any failed assertion returns a nonzero exit code, with no ffmpeg.exe required. Pass `-- --native-path <directory>` to test explicit configuration. Self-contained publishing runs the same suite on Linux ARM and macOS without an SDK. For LoongArch64, publish with `--self-contained false -p:UseAppHost=false` and run the DLL using `dotnet` on the target machine.

## License

This project uses the [MIT License](LICENSE), which permits use, copying, modification and distribution, including commercial use, provided the original copyright and license notices are retained.

Third-party dependencies retain their own licenses. The consuming project selects and installs FFmpeg native libraries; their licenses and distribution obligations depend on the chosen build. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for dependency licenses and related details.
