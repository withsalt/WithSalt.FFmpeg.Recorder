using System.Diagnostics;
using System.Threading.Channels;
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

            Channel<(long frameIndex, SKBitmap data)> frameChannel = Channel.CreateBounded<(long frameIndex, SKBitmap data)>(
                new BoundedChannelOptions(1)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = true
                }, itemDropped: frame => frame.data.Dispose());

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
                            //SaveBitmapAsImage(latestBitmap, $"output/{frameIndex}.jpg", SKEncodedImageFormat.Jpeg, 100);
                        }
                    }
                    finally
                    {
                        latestBitmap?.Dispose();
                    }
                }
            });

            await FilesTest(frameChannel);
            //await CameraTest(frameChannel);
            //await DesktopTest(frameChannel);
            //await RtspTest(frameChannel);
            //await RtmpTest(frameChannel);
            //await HttpTest(frameChannel, "http://192.168.188.22:5000/pe01.mkv");
            //await HttpTest(frameChannel, "http://192.168.188.1:10000/rtp/239.93.0.58:5140");
            //await HttpTest(frameChannel, "https://d1--cn-gotcha07.bilivideo.com/live-bvc/215313/live_453064938_66485671.flv?expires=1743002728&len=0&oi=0x240e03990ea05630f9b5d313d99dc9d5&pt=web&qn=10000&trid=10003b5e219240f2e5612ebbde8a0267e40e&sigparams=cdn,expires,len,oi,pt,qn,trid&cdn=cn-gotcha07&sign=0a7b02f30780519c66d69d3230ecd707&site=33cf69dfe5ed72b532eea3cd28860dff&free_type=0&mid=0&sche=ban&trace=16&isp=ct&rg=SouthWest&pv=Sichuan&info_source=origin&sk=28fa386efcf76f3b1161ace8c290a776&source=puv3_onetier&sl=1&score=1&hot_cdn=0&p2p_type=1&origin_bitrate=473251&suffix=origin&deploy_env=prod&pp=rtmp&vd=bc&src=puv3&order=2");

            Console.WriteLine("Done.");
        }

        static async Task FilesTest(Channel<(long frameIndex, SKBitmap data)> frameChannel)
        {
            var ffmpegCmd = new FFmpegArgumentsBuilder()
                .WithFileInput()
                .WithFiles("./videos/01.mp4", "./videos/02.mp4", "./videos/04.mp4")
                //.WithFiles("F:\\Temp\\舌尖上的中国第三季第1集.CCTV1.A.Bite.Of.China.III.Ep01.HD-1080p.X264.AAC-99Mp4.mp4")
                .WithImageHandle((frameIndex, bitmap) =>
                {
                    if (!frameChannel.Writer.TryWrite((frameIndex, bitmap)))
                    {
                        bitmap.Dispose();
                    }
                })
                .WithOutputSize(1920, 1080)
                .WithOutputQuality(OutputQuality.High)
                //.NotifyOnProgress(frame => Console.WriteLine($"Frame {frame} captured."), TimeSpan.FromSeconds(1))
                ;

            await ffmpegCmd.ProcessAsynchronously();
        }

        static async Task DesktopTest(Channel<(long frameIndex, SKBitmap data)> frameChannel)
        {
            IFFmpegArgumentsBuilder ffmpegCmd = new FFmpegArgumentsBuilder()
                .WithDesktopInput()
                .WithRectangle(SKRect.Create(new SKPoint(0, 0), new SKSize(1280, 720)))
                .WithFramerate(120)
                .WithImageHandle((frameIndex, bitmap) =>
                {
                    if (!frameChannel.Writer.TryWrite((frameIndex, bitmap)))
                    {
                        Console.WriteLine("写入队列失败");

                        bitmap.Dispose();
                    }
                })
                .WithOutputQuality(OutputQuality.Medium)
                //.NotifyOnProgress(frame => Console.WriteLine($"Frame {frame} captured."), TimeSpan.FromSeconds(1))
                ;

            await ffmpegCmd.ProcessAsynchronously();
        }

        static async Task RtspTest(Channel<(long frameIndex, SKBitmap data)> frameChannel)
        {
            IFFmpegArgumentsBuilder ffmpegCmd = new FFmpegArgumentsBuilder()
                .WithStreamInput()
                .WithRtsp("rtsp://admin:admin123.@192.168.188.66:554/stream1")
                //.WithUdp()
                .WithTimeout(6)
                .WithImageHandle((frameIndex, bitmap) =>
                {
                    if (!frameChannel.Writer.TryWrite((frameIndex, bitmap)))
                    {
                        bitmap.Dispose();
                    }
                })
                //RTSP视频流使用较低质量编码可能导致图像错误
                .WithOutputQuality(OutputQuality.Medium)
                //.NotifyOnProgress(frame => Console.WriteLine($"Frame {frame} captured."), TimeSpan.FromSeconds(1))
                ;

            await ffmpegCmd.ProcessAsynchronously();
        }

        static async Task HttpTest(Channel<(long frameIndex, SKBitmap data)> frameChannel, string uri)
        {
            IFFmpegArgumentsBuilder ffmpegCmd = new FFmpegArgumentsBuilder()
                .WithStreamInput()
                .WithHttp(uri)
                .WithTimeout(10)
                .WithProbeSize(64 * 1024)
                .WithImageHandle((frameIndex, bitmap) =>
                {
                    if (!frameChannel.Writer.TryWrite((frameIndex, bitmap)))
                    {
                        bitmap.Dispose();
                    }
                })
                //RTSP视频流使用较低质量编码可能导致图像错误
                .WithOutputQuality(OutputQuality.Medium)
                //.NotifyOnProgress(frame => Console.WriteLine($"Frame {frame} captured."), TimeSpan.FromSeconds(1))
                ;

            await ffmpegCmd.ProcessAsynchronously();
        }

        static async Task RtmpTest(Channel<(long frameIndex, SKBitmap data)> frameChannel)
        {
            IFFmpegArgumentsBuilder ffmpegCmd = new FFmpegArgumentsBuilder()
                .WithStreamInput()
                .WithRtmp("rtmp://192.168.188.22:1935/live/stream?user=admin&pass=oxV7PDo0CSpKMra7IXTmlx5Xo5KOeFsk")
                .WithLiveType(RtmpLiveType.Live)
                .WithImageHandle((frameIndex, bitmap) =>
                {
                    if (!frameChannel.Writer.TryWrite((frameIndex, bitmap)))
                    {
                        bitmap.Dispose();
                    }
                })
                .WithOutputQuality(OutputQuality.Medium)
                //.NotifyOnProgress(frame => Console.WriteLine($"Frame {frame} captured."), TimeSpan.FromSeconds(1))
                ;

            await ffmpegCmd.ProcessAsynchronously();
        }

        static async Task CameraTest(Channel<(long frameIndex, SKBitmap data)> frameChannel)
        {
            IReadOnlyList<CameraDevice> devices = OperatingSystem.IsMacOS()
                ? [new CameraDevice("default", "Default AVFoundation camera", true)]
                : FFmpegNative.EnumerateCameraDevices();
            if (devices.Count == 0)
            {
                throw new InvalidOperationException("找不到视频输入设备！");
            }

            CameraDevice device = devices.FirstOrDefault(device => device.IsDefault) ?? devices[0];
            Console.WriteLine($"Using camera: {device.Description ?? device.Name} ({device.Name})");

            IFFmpegArgumentsBuilder ffmpegCmd = new FFmpegArgumentsBuilder()
                .WithCameraInput()
                .WithDeviceName(device.Name)
                .WithVideoSize(1280, 720)
                .WithFramerate(30)
                .WithImageHandle((frameIndex, bitmap) =>
                {
                    if (!frameChannel.Writer.TryWrite((frameIndex, bitmap)))
                    {
                        bitmap.Dispose();
                    }
                })
                .WithOutputQuality(OutputQuality.Medium)
                //.NotifyOnProgress(frame => Console.WriteLine($"Frame {frame} captured."), TimeSpan.FromSeconds(1))
                ;

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
