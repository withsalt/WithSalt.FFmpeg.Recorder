# WithSalt.FFMpeg.Recorder  
基于FFmpeg实现的高性能视频录制框架，将多种输入源（本地视频，摄像头、RTSP 流媒体、桌面等）处理为视频帧。  

## 核心功能  

**受支持的输入源**  
- 本地视频文件，支持多文件
- 摄像头实时捕获
- RTSP 流媒体
- 桌面屏幕录制（Windows、Linux XOrg）

**受支持的平台和操作系统**  

| OS  |  Runtime   |  x86  |  x64  |  ARM  | ARM64  | LoongArch64  |
| ------------ | ------------ | :------------: | :------------: | :------------: | :------------: | :------------: |
| Windows  | .NET Core 3.1+  |  √  |  √  |     |      |      |
| Linux    | .NET Core 3.1+  |     |  √  |  √  |  √   |  √   |

## 性能
CPU为Intel i7 12700K，不使用硬件加速，输出质量控制为中等，进行屏幕录制。可跑满60帧。  
树莓派5B，不使用硬件加速，输出质量控制为中等，本地视频处理。可跑满60帧。  

## 快速开始  

### 安装Nuget包
通过Nuget包管理器安装`WithSalt.FFmpeg.Recorder` [![NuGet Version](https://img.shields.io/nuget/v/BemfaCloud.svg?style=flat)](https://www.nuget.org/packages/BemfaCloud/)   
或  
通过Terminal安装：  
```shell
dotnet add package WithSalt.FFmpeg.Recorder
```

### 安装FFmpeg  
Windows系统下，可前往 https://github.com/BtbN/FFmpeg-Builds/releases 下载编译好的FFmpeg。然后将ffmpeg.exe放入应用程序更目录，或者按照源代码中的搜索逻辑，放入支持自动检索的目录。  
Linux系统下，通过命令：`sudo apt install ffmpeg`安装。  

### 加载FFmpeg
工欲善其事必先利其器。在调用任何API之前，让我们先告诉程序ffmpeg所在目录，以及进行一些基础配置。  
调用：  
```
//使用默认的ffmpeg加载器
FFmpegHelper.SetDefaultFFmpegLoador();
```
调用`SetDefaultFFmpegLoador`之后，程序会从以下路径开始搜索ffmpeg应用程序所在位置。  
- 优先查找与当前进程架构匹配的运行时目录（通过 GetProcessArchitecturePath() 获取架构标识），如：.\runtimes\win-x64\bin\ffmpeg.exe  
- 包含开发环境（源目录），如：.\ffmpeg.exe  
- 生产环境的路径配置，如：.\bin\ffmpeg.exe  

当以上路径均找不到时，查找环境变量Path中的逻辑。所有路径均找不到后，抛出异常。  

### 构建FFmpeg执行参数  
如果进行桌面录制  
```
FFMpegArgumentProcessor ffmpegCmd = new WithSalt.FFmpeg.Recorder.FFmpegArgumentsBuilder()
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
    .Build()
    //.NotifyOnProgress(frame => Console.WriteLine($"Frame {frame} captured."), TimeSpan.FromSeconds(1))
    ;
```
上述代码解释：使用输入源为桌面，录制的区域从左上角0,0坐标开始，录制屏幕1920x1080的区域。当图片产生之后，通过WithImageHandle回调，放入frameChannel中。  
使用其他的输入源，调用对应的API即可，比如：WithCameraInput()。  

### 完整Demo
以下是录制屏幕的完整demo  
```
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using FFMpegCore;
using FlashCap;
using SkiaSharp;
using WithSalt.FFmpeg.Recorder;
using WithSalt.FFmpeg.Recorder.Interface;
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

        static async Task DesktopTest(Channel<(long frameIndex, SKBitmap data)> frameChannel)
        {
            FFMpegArgumentProcessor ffmpegCmd = new WithSalt.FFmpeg.Recorder.FFmpegArgumentsBuilder()
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

            bitmap.Dispose();
        }
    }
}

```  

### 更多完整示例  
https://github.com/withsalt/BemfaCloud/tree/main/src/Examples

## 致谢
感谢这些伟大的开源项目。  
- FFmpeg https://git.ffmpeg.org/gitweb/ffmpeg.git
- FFMpegCore https://github.com/rosenbjerg/FFMpegCore
- SkiaSharp https://github.com/mono/SkiaSharp
- FlashCap https://github.com/kekyo/FlashCap
