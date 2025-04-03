# WithSalt.FFMpeg.Recorder  

A high-performance video recording framework based on FFmpeg, which supports extracting continuous image frames from various input sources (local videos, cameras, network streams, desktops, etc.).

## Core Features  

### **Supported Input Sources**  
- Local video files (supports multiple files)  
- Real-time camera capture  
- Streaming (RTSP, RTMP, HLS, etc.)
- Desktop screen recording (Windows, Linux XOrg)  

### **Supported Platforms and Operating Systems**  

| OS       | Runtime        | x86 | x64 | ARM | ARM64 | LoongArch64 |
|----------|---------------|:---:|:---:|:---:|:----:|:-----------:|
| Windows  | .NET Core 3.1+ |  √  |  √  |     |      |             |
| Linux    | .NET Core 3.1+ |     |  √  |  √  |  √   |      √      |

## Quick Start  

### **Install NuGet Package**  
Install `WithSalt.FFmpeg.Recorder` via NuGet Package Manager:  
[![NuGet Version](https://img.shields.io/nuget/v/WithSalt.FFmpeg.Recorder.svg?style=flat)](https://www.nuget.org/packages/WithSalt.FFmpeg.Recorder/)  

Or install via Terminal:  
```shell
dotnet add package WithSalt.FFmpeg.Recorder
```

### **Install FFmpeg**  
For Windows systems, you can download the precompiled FFmpeg from [this repository](https://github.com/BtbN/FFmpeg-Builds/releases). Then, place `ffmpeg.exe` in the application root directory or in one of the directories that support automatic search (see the next section).  

It is recommended to add a conditional compilation parameter in the project configuration to automatically copy `ffmpeg.exe` when compiling for Windows:  
```xml
<ItemGroup Condition="'$(OS)' == 'Windows_NT' OR '$(RuntimeIdentifier)' == 'win-x64'">
	<None Update="runtimes\win-x64\bin\ffmpeg.exe">
		<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
	</None>
</ItemGroup>
```
For Linux systems (e.g., Debian, Ubuntu), install FFmpeg using the command:  
```shell
sudo apt install ffmpeg
```
You can also refer to the demo examples in the project for FFmpeg path configurations.  

### **Load FFmpeg**  
Before calling any API provided by the library, we need to specify the FFmpeg directory and perform some basic configurations:  
```csharp
// Use the default FFmpeg loader
FFmpegHelper.SetDefaultFFmpegLoador();
```
After calling `SetDefaultFFmpegLoador`, the following initialization steps will be performed:  
1. The program will search for the FFmpeg executable in the following locations:  
    - The runtime directory matching the current process architecture, e.g., `.\runtimes\win-x64\bin\ffmpeg.exe`  
    - The application root directory, e.g., `<ApplicationDirectory>\ffmpeg.exe`  
    - The `bin` directory inside the application directory, e.g., `<ApplicationDirectory>\bin\ffmpeg.exe`  
    - **[Windows]** All directories listed in the system `Path` variable  
    - **[Linux]** Common FFmpeg installation directories: `/usr/bin`, `/usr/local/bin`, `/usr/share`  
      
    If FFmpeg is not found in any of these locations, an exception will be thrown.  
2. The FFmpeg working directory is set to the application root directory by default, and a `tmp` directory is created for FFmpeg temporary files.  

### **Build FFmpeg Execution Parameters**  
For desktop recording:  
```csharp
FFMpegArgumentProcessor ffmpegCmd = new FFmpegArgumentsBuilder()
    .WithDesktopInput()
    .WithRectangle(new SKRect(0, 0, 1920, 1080))
    .WithFramerate(60)
    .WithImageHandle((frameIndex, bitmap) =>
    {
        if (!frameChannel.Writer.TryWrite((frameIndex, bitmap)))
        {
            bitmap.Dispose();
        }
    })
    .WithOutputQuality(OutputQuality.Medium)
    .Build();
    //.NotifyOnProgress(frame => Console.WriteLine($"Frame {frame} captured."), TimeSpan.FromSeconds(1));
```
### Explanation:  
- **Input Source:** Desktop  
- **Recording Area:** From the top-left corner (0,0), capturing a 1920x1080 region  
- **Frame Handling:** When an image frame is generated, it is passed to `frameChannel` via `WithImageHandle`  

To use other input sources, simply call the corresponding API, such as `WithCameraInput()`.  

### **Complete Demo**  
Below is a complete demo for screen recording:  
```
using System.Diagnostics;
using System.Threading.Channels;
using FFMpegCore;
using SkiaSharp;
using WithSalt.FFmpeg.Recorder;
using WithSalt.FFmpeg.Recorder.Models;

namespace ConsoleAppDemo
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            if (Directory.Exists("output"))
            {
                Directory.Delete("output", true);
            }
            Directory.CreateDirectory("output");

            //使用默认的ffmpeg加载器
            FFmpegHelper.SetDefaultFFmpegLoador();

            Channel<(long frameIndex, SKBitmap data)> frameChannel = Channel.CreateBounded<(long frameIndex, SKBitmap data)>(
                new BoundedChannelOptions(10)
                {
                    FullMode = BoundedChannelFullMode.Wait
                });

            //计算FPS
            int uiFrameCount = 0;
            Stopwatch lastUiFpsUpdate = Stopwatch.StartNew();
            Stopwatch totalUiFpsUpdate = Stopwatch.StartNew();
            int currentUiFps = 0;
            long totalUiFps = 0;

            // 启动写入任务
            var cts = new CancellationTokenSource();
            var writeTask = Task.Run(async () =>
            {
                totalUiFpsUpdate.Restart();

                while (!cts.IsCancellationRequested && await frameChannel.Reader.WaitToReadAsync(cts.Token))
                {
                    SKBitmap? latestBitmap = null;
                    long frameIndex = 0;

                    // 取出所有可用帧，只保留最后一帧
                    while (frameChannel.Reader.TryRead(out (long frameIndex, SKBitmap bitmap) data))
                    {
                        latestBitmap?.Dispose();
                        latestBitmap = data.bitmap;
                        frameIndex = data.frameIndex;
                    }

                    try
                    {
                        if (latestBitmap != null)
                        {
                            // 更新FPS计数器
                            uiFrameCount++;
                            if (lastUiFpsUpdate.ElapsedMilliseconds >= 1000)
                            {
                                currentUiFps = uiFrameCount;
                                totalUiFps += uiFrameCount;
                                uiFrameCount = 0;
                                lastUiFpsUpdate.Restart();

                                TimeSpan totalElapsed = totalUiFpsUpdate.Elapsed;
                                int avgFps = (int)(totalUiFps / Math.Max(1, totalElapsed.TotalSeconds));

                                Console.Write($"\r{(int)totalElapsed.TotalHours:00}:{totalElapsed.Minutes:00}:{totalElapsed.Seconds:00} | Current FPS: {currentUiFps} | AVG FPS: {avgFps}   ");
                            }

                            //Console.WriteLine("收到图片帧");
                            SaveBitmapAsImage(latestBitmap, $"output/{frameIndex}.jpg", SKEncodedImageFormat.Jpeg, 100);
                        }
                    }
                    finally
                    {
                        latestBitmap?.Dispose();
                    }
                }
            });

            await DesktopTest(frameChannel);
            Console.WriteLine("Done.");
        }

        private static Action? _cancel = null;

        static async Task DesktopTest(Channel<(long frameIndex, SKBitmap data)> frameChannel)
        {
            FFMpegArgumentProcessor ffmpegCmd = new FFmpegArgumentsBuilder()
                .WithDesktopInput()
                .WithRectangle(new SKRect(0, 0, 0, 0))
                .WithFramerate(60)
                .WithImageHandle((frameIndex, bitmap) =>
                {
                    if (!frameChannel.Writer.TryWrite((frameIndex, bitmap)))
                    {
                        bitmap.Dispose();
                    }
                })
                .WithOutputQuality(OutputQuality.Medium)
                .Build()
                .CancellableThrough(out _cancel)
                //.NotifyOnProgress(frame => Console.WriteLine($"Frame {frame} captured."), TimeSpan.FromSeconds(1))
                ;

            var cmd = ffmpegCmd.Arguments;
            Console.WriteLine($"FFMpeg命令：{Environment.NewLine}ffmpeg {cmd}");

            await ffmpegCmd.ProcessAsynchronously();
        }

        /// <summary>
        /// 将 SKBitmap 保存为指定格式的图片文件
        /// </summary>
        /// <param name="bitmap">要保存的 SKBitmap 实例</param>
        /// <param name="filePath">保存的文件路径</param>
        /// <param name="imageFormat">图像格式（PNG、JPEG 等）</param>
        /// <param name="quality">编码质量（针对有损格式，如 JPEG）</param>
        static void SaveBitmapAsImage(SKBitmap bitmap, string filePath, SKEncodedImageFormat imageFormat, int quality)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            using (SKImage image = SKImage.FromBitmap(bitmap))
            using (SKData data = image.Encode(imageFormat, quality))
            using (FileStream stream = File.OpenWrite(filePath))
            {
                data.SaveTo(stream);
            }
        }
    }
}

```

## **Development Recommendations**  
1. **Use an asynchronous queue to process image frames**  
   - Processing frames (e.g., image recognition) is usually slower than FFmpeg fetching video frames. Using an asynchronous queue ensures smooth processing and allows frame-dropping strategies for better performance.  
2. **Store FFmpeg in the runtime directory**  
   - This enables automatic searching and keeps the application directory organized. Example: `runtimes\win-x64\bin\ffmpeg.exe`  

### **More Complete Examples**  
[https://github.com/withsalt/WithSalt.FFmpeg.Recorder/tree/main/src/Demos](https://github.com/withsalt/WithSalt.FFmpeg.Recorder/tree/main/src/Demos)  

## License  
- This software is licensed under the MIT open-source license.  
- This software uses FFmpeg (https://ffmpeg.org), which is protected under the LGPL/GPL license.  

## **Acknowledgments**  
Special thanks to these great open-source projects:  
- **FFmpeg**: [https://git.ffmpeg.org/gitweb/ffmpeg.git](https://git.ffmpeg.org/gitweb/ffmpeg.git)  
- **FFMpegCore**: [https://github.com/rosenbjerg/FFMpegCore](https://github.com/rosenbjerg/FFMpegCore)  
- **SkiaSharp**: [https://github.com/mono/SkiaSharp](https://github.com/mono/SkiaSharp)  
- **FlashCap**: [https://github.com/kekyo/FlashCap](https://github.com/kekyo/FlashCap)
