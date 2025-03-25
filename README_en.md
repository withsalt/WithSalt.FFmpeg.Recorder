# WithSalt.FFMpeg.Recorder  

A high-performance video recording framework based on FFmpeg, supporting the processing of various input sources (local videos, cameras, network streams, desktop, etc.) into continuous image frames. 

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
```csharp
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

            // Use the default FFmpeg loader
            FFmpegHelper.SetDefaultFFmpegLoador();

            Channel<(long frameIndex, SKBitmap data)> frameChannel = Channel.CreateBounded<(long frameIndex, SKBitmap data)>(
                new BoundedChannelOptions(10)
                {
                    FullMode = BoundedChannelFullMode.Wait
                });

            // Start writing task
            var cts = new CancellationTokenSource();
            var writeTask = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested && await frameChannel.Reader.WaitToReadAsync(cts.Token))
                {
                    SKBitmap? latestBitmap = null;
                    long frameIndex = 0;

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

            Console.WriteLine($"FFMpeg Command:\nffmpeg {ffmpegCmd.Arguments}");

            await ffmpegCmd.ProcessAsynchronously();
        }

        static void SaveBitmapAsImage(SKBitmap bitmap, string filePath, SKEncodedImageFormat imageFormat, int quality)
        {
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
