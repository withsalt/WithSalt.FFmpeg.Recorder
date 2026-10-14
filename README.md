# WithSalt.FFmpeg.Recorder

通过 FFmpeg 解码视频，输出连续的 SkiaSharp `SKBitmap` 图像帧。支持本地文件、顺序多文件、摄像头、桌面、RTSP、HTTP/HLS 和 RTMP 输入。

[English](README_en.md)


## 安装与使用

```sh
dotnet add package WithSalt.FFmpeg.Recorder
```

`WithSalt.FFmpeg.Recorder` 不包含 FFmpeg 原生库。引用方必须自行选择并安装 `FFmpeg.LGPL` 或 `FFmpeg.GPL` 或 `Sdcb.FFmpeg.runtime.windows-x64` 等ffmpeg原生库封装包（nuget上面有很多封装，任意选择一个基于ffmpeg 7.x版本以上编译的的即可）；   
以下示例使用 `FFmpeg.LGPL` 包：

```sh
dotnet add package FFmpeg.LGPL
```

一般情况下，只有Windows系统需要安装FFmpeg 原生库，Linux / macOS 建议使用发行版包管理器或 Homebrew 安装与进程架构一致的共享库及依赖。  
比如Ubuntu/Debian/Deepin等：  
```sh
apt install ffmpeg

# dotnet runtime dependencies, Libicu requires installing different versions on different system versions: libicu76 supports Debian 13x, libicu78 supports Ubuntu 26.x
apt install ca-certificates libc6 libgcc-s1 libgssapi-krb5-2 libicu76 libssl3t64 libstdc++6 tzdata zlib1g
```

应用目录内有 FFmpeg 库时，会从同一目录加载，缺库会报错。也可以在第一次执行前显式指定ffmpeg原生库路径：

```csharp
FFmpegNative.Configure(options =>
{
    options.LibraryPath = "/opt/ffmpeg/lib"; // macOS 示例：/opt/homebrew/opt/ffmpeg/lib
});
```
使用 MinGW 构建的第三方包可能还需要 `libstdc++-6.dll` 及其依赖，缺少这些文件时，即使 FFmpeg DLL 已存在也会加载失败。  
Linux / macOS 自定义目录内的依赖库必须能被系统动态链接器找到。  

另外，如果需要在Linux环境使用SkiaSharp，需要安装包：  
```sh
dotnet add package SkiaSharp.NativeAssets.Linux.NoDependencies
```

## 基本用法

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
            // 同步处理 bitmap，或转交给自己的有界队列。
            // 交给队列后应由队列消费者 Dispose；入队失败时在这里 Dispose。
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
    // 已结束本次采集并释放原生会话。
}

// 停止正在运行的采集：cancellation.Cancel();
// 同步执行：recording.ProcessSynchronously(cancellation.Token);
```

## 高性能采集

默认 `WithImageHandle` 的独立所有权保持不变。库会复用已释放的原生像素内存，每个会话最多缓存一块空闲缓冲区；仍被调用方或 Skia 共享引用持有的像素不会被覆盖。异步处理应使用有界队列，及时释放消费完成或被丢弃的位图。

完全同步的读取可以改用 `WithImageViewHandle`，接收仅在回调期间有效的只读图像视图，复用位图对象和转换缓冲区；同尺寸、正步长 BGRA 输入直接借用解码帧像素，省去整帧复制：

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
        // 在此同步读取像素。bitmap 仅在本次回调内有效。
        // 禁止 Dispose、修改、保存引用或入队；需要独立所有权时改用 WithImageHandle。
    });
await recording.ProcessAsynchronously(cancellation.Token);
```

两种图像回调互相替换，以最后一次配置为准。借用回调、帧筛选回调均在解码线程执行，异常会停止本次采集。

需要异步处理或保留图像时，优先使用 `WithImageHandle` 接收独立位图。如果必须在借用回调内复制，可使用保持原尺寸和格式的 `bitmap.Resize(bitmap.Info, SKSamplingOptions.Default)`，检查结果非空，并由最终消费者释放副本。已验证 SkiaSharp 3.119.0 和 4.153.1 的 `Copy()` / `CopyTo()` 存在临时 shader 延迟释放问题，即使释放副本也会造成逐帧内存增长，详见[上游 #2915](https://github.com/mono/SkiaSharp/issues/2915)和[本项目验证记录](docs/memory-growth-review-2026-10-14.md)。

`WithFrameFilter(index => index % 2 == 0)` 在图像分配与转换**之前**筛选输出帧，适合按需采样或由消费者负载决定是否接受下一帧。返回 false 时仍推进帧号和进度，因此图像回调的帧号可能不连续；它不跳过压缩视频解码。未配置图像回调时不执行筛选。

优先降低源采集尺寸/帧率、明确桌面采集区域，避免先捕获整块虚拟桌面再缩小。只需最新画面时使用 `FpsMode.Drop`，缩放成本敏感时选择 `OutputQuality.Low`。`WithDecoderThreads(0)` 由 FFmpeg 自动选择线程，`1` 降低线程/帧缓冲开销；线程更少可能降低压缩视频吞吐，应按实际输入测试。直播低延迟预设仅允许 slice threading，避免新增 frame threading 延迟。

三轮同机微基准中，默认所有权输出的转换路径加速约 3–12 倍、每帧托管分配约 448 → 112 B；借用路径预热后为 0 B。

## 输入配置

以下代码中的配置对象都可继续调用 `WithImageHandle`、`WithOutputSize` 和执行方法：

```csharp
var rtsp = new FFmpegArgumentsBuilder().WithStreamInput()
    .WithRtsp("rtsp://host/stream")
    .WithTcp() // 默认 TCP；也可 WithUdp()
    .WithTimeout(10)
    .WithProbeSize(1024 * 1024);

var http = new FFmpegArgumentsBuilder().WithStreamInput()
    .WithHttp("https://host/playlist.m3u8")
    .WithTimeout(10);

var rtmp = new FFmpegArgumentsBuilder().WithStreamInput()
    .WithRtmp("rtmp://host/live/stream")
    .WithLiveType(RtmpLiveType.Live);

var camera = new FFmpegArgumentsBuilder().WithCameraInput()
    .WithDeviceName("Camera name") // Linux: /dev/video0；macOS: default、0 或 AVFoundation 名称
    .WithVideoSize(1280, 720)
    .WithFramerate(30);

var desktop = new FFmpegArgumentsBuilder().WithDesktopInput()
    .WithRectangle(SKRect.Create(0, 0, 1280, 720))
    .WithFramerate(30);
```

调用 `FFmpegNative.EnumerateCameraDevices()` 可通过 FFmpeg `avdevice_list_input_sources` 原生枚举摄像头，返回的 `Name` 可直接传给 `WithDeviceName`；Windows 使用 DirectShow，Linux 使用 V4L2。

macOS 的 FFmpeg AVFoundation 未实现该枚举 API，`EnumerateCameraDevices()` 会抛出 `NotSupportedException`。直接使用 `WithDeviceName("default")`、视频设备索引（如 `"0"`）或 AVFoundation 名称；库会自动追加 `:none` 禁用音频，传入值不含音频选择器。

桌面输入可以不调用 `WithRectangle`，或使用 `SKRect.Create(0, 0, 0, 0)`，由原生设备自动确定采集范围。Desktop Demo 支持 `0,0`、`0,0,,`、`0,0,0,0`；指定区域使用 `x,y,width,height`，宽高必须同时为正整数。Windows 自动范围覆盖整个虚拟桌面（可能包含多个显示器），`WithOutputSize` 只负责输出缩放，不限制采集区域。只录制某个屏幕或区域时，请明确指定位置和宽高。

macOS 默认采集 AVFoundation 的第一块显示器（`Capture screen 0:none`）。`WithRectangle` 使用该显示器内的非负像素坐标，区域不得超出实际采集尺寸；先裁剪再应用 `WithOutputSize`。自动范围的原点必须为 `(0, 0)`。Retina 显示器请使用采集帧的像素尺寸，而非逻辑点尺寸。

设备名称直接传入，不加命令行引号。设备驱动、FFmpeg 构建启用的协议/解码器、TLS 支持决定具体输入是否可用。

`WithTimeout` 单位为秒，作用于打开、探测以及每次阻塞读取；0 表示不设置期限。取消通过 FFmpeg interrupt callback 中断支持该回调的原生 I/O；不能强制中断用户回调、系统驱动或不响应中断的第三方协议实现。需要等待执行 Task 结束后再处理采集已经停止的状态。

## 输出语义

| 配置 | 行为 |
| --- | --- |
| Passthrough | 每个解码帧输出一次，进度使用归一化媒体时间 |
| VFR | 保留可变帧间隔，丢弃重复/倒退时间戳；不能配置输出帧率 |
| CFR | 按目标帧率采样，必要时复制上一帧或丢帧；必须配置输出帧率 |
| Auto | 有输出帧率时使用 CFR，否则使用 VFR |
| Drop | 只丢帧、不补帧；有输出帧率时限制采样率，否则与 VFR 相同 |
| WithOutputFramerate | 未指定模式时选择 CFR；显式 Passthrough/VFR 与输出帧率互斥 |
| OutputQuality.High / Medium / Low | Lanczos / Bilinear / Fast Bilinear 缩放。所有档位均直接转为 BGRA，不进行有损 MJPEG 中转 |

帧率参数必须有限、> 0 且 <= 1000。文件提帧尽快执行，不按墙上时钟播放；CFR 描述媒体时间轴，不保证回调按等间隔实际到达。没有硬件加速或 HDR 色调映射；高位深输入会转换为 8-bit BGRA。

非文件输入默认保留 High 低延迟配置。Medium 使用 nobuffer、discardcorrupt、low_delay 和较小探测窗口；High 进一步减少探测量、启用 direct I/O，并调整 RTSP 缓冲。激进设置可能无法正确识别输入，遇到问题优先使用 `WithLatencyOptimizationLevel(LatencyOptimizationLevel.None)`。显式 `WithProbeSize` 优先于预设值。

`NotifyOnProgress` 返回 `FFmpegProgress`：帧数、跨文件媒体时间、当前输入索引、当前输入媒体时间、当前文件时长（如已知）。直播不提供时长；多文件不预先打开全部文件来计算总百分比。回调按实际时间节流，并在正常结束时补报最后一帧。

## 开发与验证

```sh
dotnet build src/WithSalt.FFmpeg.Recorder.slnx -c Release
dotnet run --project tests/WithSalt.FFmpeg.Recorder.Tests -f net8.0 -c Release
dotnet run --project src/Demos/ConsoleAppDemo -c Release -- --file path/to/video.mp4
```

测试是可执行的集成测试程序，任一断言失败返回非零退出码，无需 ffmpeg.exe。可用 `-- --native-path <目录>` 测试显式配置；可为 Linux ARM、macOS 发布 self-contained 产物，在没有 SDK 的设备上运行同一测试。LoongArch64 可用 `--self-contained false -p:UseAppHost=false` 发布后，通过目标机器的 `dotnet` 运行 DLL。

## 许可证

本项目采用 [MIT 许可证](LICENSE)，允许使用、复制、修改和分发（包括商业用途），但须保留原始版权声明和许可证声明。

第三方依赖遵循各自的许可证。FFmpeg 原生库由引用方自行选择和安装，其许可证及分发义务取决于所选构建。依赖许可证与相关说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
