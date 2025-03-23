using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Arguments;
using FFMpegCore.Pipes;
using SkiaSharp;
using WithSalt.FFmpeg.Recorder.Interface;
using WithSalt.FFmpeg.Recorder.Models;

namespace WithSalt.FFmpeg.Recorder.Builder
{
    public abstract class BaseInputArgumentsBuilder : IFFmpegArgumentsBuilder
    {
        protected FFMpegArguments? _arguments;

        protected List<IArgument> _outputArgumentList = new List<IArgument>();
        protected List<IArgument> _filterArgumentList = new List<IArgument>();

        private Action<long, SKBitmap>? _imageProcessHandle;

        private OutputQuality OutputQuality = OutputQuality.Medium;

        public BaseInputArgumentsBuilder()
        {
            _outputArgumentList.Add(new ForceFormatArgument("image2pipe"));
            _outputArgumentList.Add(new VideoCodecArgument("mjpeg"));
        }

        public virtual FFMpegArgumentProcessor Build()
        {
            if (_arguments == null)
            {
                throw new ArgumentNullException("FFmpegArguments", "Please set input parameters at first.");
            }
            if (!isCallWithOutputQuality)
            {
                this.WithOutputQuality(OutputQuality.Medium);
            }
            if (!isCallWithOutputFramerate)
            {
                this.WithOutputFramerate(-1);
            }

            //用于控制是否在每个数据包（packet）写入时刷新（flush）缓冲区，1=启用（默认），每个数据包都会刷新缓冲区，减少延迟。
            _outputArgumentList.Add(new CustomArgument("-flush_packets 1"));

            //以下参数用于时间戳(DTS)相关的错误，导致编码器无法正常处理视频帧
            //应用于输入流,为缺失或损坏的PTS(Presentation Time Stamp)生成新的时间戳，但尽量保留原始时间戳的相对关系
            //_outputArgumentList.Add(new CustomArgument("-fflags +genpts"));
            //应用于输出流,完全重置输出流的时间戳，使其从0开始，不考虑输入流的原始时间戳
            _outputArgumentList.Add(new CustomArgument("-reset_timestamps 1"));

            FFMpegArgumentProcessor processor = _arguments
                .OutputToPipe(new StreamPipeSink(ProcessStream), options =>
                {
                    //处理视频filter参数
                    foreach (var item in _filterArgumentList)
                    {
                        if ((this.OutputQuality == OutputQuality.High) && item.Text.Trim(' ', '\r', '\n').StartsWith("-filter_complex"))
                        {
                            string text = item.Text.Replace("yuv420p", "yuv444p");
                            options.WithArgument(new CustomArgument(text));
                        }
                        else
                        {
                            options.WithArgument(item);
                        }
                    }
                    foreach (var item in _outputArgumentList)
                    {
                        options.WithArgument(item);
                    }
                });
            return processor;
        }

        private bool isCallWithOutputQuality = false;

        /// <summary>
        /// 输出质量控制，一般选择中等就行了
        /// </summary>
        /// <param name="quality"></param>
        /// <returns></returns>
        public IFFmpegArgumentsBuilder WithOutputQuality(OutputQuality quality = OutputQuality.Medium)
        {
            try
            {
                this.OutputQuality = quality;
                switch (this.OutputQuality)
                {
                    default:
                    case OutputQuality.High:
                        _outputArgumentList.Add(new ForcePixelFormat("yuv444p"));
                        _outputArgumentList.Add(new CustomArgument("-color_range jpeg"));
                        _outputArgumentList.Add(new CustomArgument("-q:v 2"));
                        break;
                    case OutputQuality.Medium:
                        _outputArgumentList.Add(new ForcePixelFormat("yuv420p"));
                        _outputArgumentList.Add(new CustomArgument("-color_range jpeg"));
                        _outputArgumentList.Add(new CustomArgument("-q:v 10"));
                        break;
                    case OutputQuality.Low:
                        _outputArgumentList.Add(new ForcePixelFormat("yuv420p"));
                        _outputArgumentList.Add(new CustomArgument("-color_range jpeg"));
                        _outputArgumentList.Add(new CustomArgument("-q:v 25"));
                        break;
                }
            }
            finally
            {

                isCallWithOutputQuality = true;
            }
            return this;
        }

        public IFFmpegArgumentsBuilder WithOutputSize(int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Invalid width.");
            }
            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "Invalid height.");
            }
            _outputArgumentList.Add(new SizeArgument(width, height));
            return this;
        }

        private bool isCallWithOutputFramerate = false;

        /// <summary>
        /// 输出帧数控制
        /// </summary>
        /// <param name="framerate"></param>
        /// <returns></returns>
        public IFFmpegArgumentsBuilder WithOutputFramerate(double framerate = 30)
        {
            try
            {
                if (framerate == -1)
                {
                    //不处理帧率，直接传递原始时间戳
                    _outputArgumentList.Add(new CustomArgument("-fps_mode passthrough"));
                    return this;
                }
                if (framerate < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(framerate), "Invalid framerate.");
                }

                _outputArgumentList.Add(new FrameRateArgument(framerate));
                _outputArgumentList.Add(new CustomArgument("-fps_mode cfr"));
            }
            finally
            {
                isCallWithOutputFramerate = true;
            }
            return this;
        }

        public IFFmpegArgumentsBuilder WithImageHandle(Action<long, SKBitmap> action)
        {
            _imageProcessHandle = action;
            return this;
        }

        /// <summary>
        /// 创建低延迟参数组合
        /// </summary>
        /// <param name="probeSize">值的大小影响输入流探测行为</param>
        /// <returns></returns>
        protected List<IArgument> CreateLowDelayArguments(int probeSize = 64)
        {
            List<IArgument> arguments = new List<IArgument>()
            {
                //禁用输入流的缓冲机制，避免数据在内存中堆积，实现实时处理 + 丢弃损坏的包
                new CustomArgument("-fflags nobuffer+discardcorrupt"),

                //启用全局低延迟模式，强制解码器 / 复用器优先处理时效性
                new CustomArgument("-flags low_delay"),

                //允许低延迟参数组合
                new CustomArgument($"-strict experimental"),

                //直接访问输入数据（绕过缓存层），减少内存拷贝带来的延迟
                new CustomArgument("-avioflags direct"),

                //=======================================================================================
                //以下参数谨慎启用，错误的参数可能导致无法正常工作

                //控制输入流探测行为的关键参数，其作用直接影响流的初始化和延迟表现
                //默认值较高（几 MB）：FFmpeg 会读取较多数据确保格式正确，但导致初始延迟增加
                //设为较小值（如 - probesize 64）：强制 FFmpeg 快速决策，跳过深度探测，显著降低初始延迟
                new CustomArgument($"-probesize {probeSize}"),

                //禁用格式探测延迟
                new CustomArgument("-analyzeduration 0"),
            };

            return arguments;
        }

        public async Task ProcessStream(Stream input, CancellationToken cancellationToken)
        {
            const int bufferSize = 16384;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            MemoryStream jpegStream = new MemoryStream();
            bool capturing = false;
            int lastByte = -1;
            long frameIndex = 0;

            try
            {
                int bytesRead;
                while (!cancellationToken.IsCancellationRequested && (bytesRead = await input.ReadAsync(buffer, 0, bufferSize, cancellationToken)) > 0)
                {
                    int startPos = -1;
                    for (int i = 0; i < bytesRead; i++)
                    {
                        byte currentByte = buffer[i];

                        // 检测 JPEG start marker (FFD8)
                        if (!capturing && lastByte == 0xFF && currentByte == 0xD8)
                        {
                            if (jpegStream.Length != 0)
                            {
                                jpegStream.Position = 0;
                                jpegStream.SetLength(0);
                            }
                            capturing = true;
                            startPos = i - 1;
                        }
                        // 检测 JPEG end marker (FFD9)
                        else if (capturing && lastByte == 0xFF && currentByte == 0xD9)
                        {
                            if (startPos == -1) startPos = 0;
                            int lengthToWrite = i - startPos + 1;
                            await jpegStream.WriteAsync(buffer, startPos, lengthToWrite, cancellationToken);

                            capturing = false;

                            DecodeImage(jpegStream, ++frameIndex);

                            startPos = -1;
                        }

                        lastByte = currentByte;
                    }

                    if (capturing)
                    {
                        if (startPos == -1) startPos = 0;
                        int lengthToWrite = bytesRead - startPos;
                        await jpegStream.WriteAsync(buffer, startPos, lengthToWrite, cancellationToken);

                        startPos = -1;
                    }
                }

                // 如果读完了流还在捕获状态，说明最后的帧不完整，可以根据需要丢弃
                if (capturing && jpegStream.Length > 0)
                {
                    DropIncompleteJpegData(jpegStream);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, true);
                await jpegStream.DisposeAsync();
                await input.DisposeAsync();
            }
        }

        /// <summary>
        /// 处理完整的 JPEG 图像
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="frameIndex"></param>
        private void DecodeImage(MemoryStream stream, long frameIndex)
        {
            try
            {
                if (_imageProcessHandle == null)
                {
                    return;
                }
                try
                {
                    stream.Position = 0;
                    using (var skStram = new SKManagedStream(stream, false))
                    {
                        SKBitmap bitmap = SKBitmap.Decode(skStram);
                        if (bitmap == null)
                        {
                            Debug.WriteLine($"Decode jpeg memory stream failed.");
                            return;
                        }

                        if (bitmap.ColorType == SKColorType.Bgra8888)
                        {
                            _imageProcessHandle.Invoke(frameIndex, bitmap);
                        }
                        else
                        {
                            SKBitmap? dest = bitmap.Copy(SKColorType.Bgra8888);
                            if (dest == null)
                            {
                                _imageProcessHandle.Invoke(frameIndex, bitmap);
                            }
                            else
                            {
                                _imageProcessHandle.Invoke(frameIndex, dest);
                                bitmap.Dispose();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 记录解码错误
                    Debug.WriteLine($"Decode error: {ex}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing JPEG image: {ex.Message}");
            }
            finally
            {
                stream.Position = 0;
                stream.SetLength(0);
            }
        }

        /// <summary>
        /// 处理不完整的 JPEG 数据
        /// </summary>
        /// <param name="stream"></param>
        private void DropIncompleteJpegData(MemoryStream stream)
        {
            Debug.WriteLine($"Warning: Incomplete JPEG data found ({stream.Length} bytes)");

            stream.Position = 0;
            stream.SetLength(0);
        }
    }
}
