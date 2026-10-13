using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DesktopAppDemo.Models;
using DesktopAppDemo.Utils;
using DesktopAppDemo.Views;
using WithSalt.FFmpeg.Recorder.Interface;
using FlashCap;
using Microsoft.Extensions.Logging;
using NLog;
using SkiaSharp;
using WithSalt.FFmpeg.Recorder;
using WithSalt.FFmpeg.Recorder.Models;

namespace DesktopAppDemo.ViewModels
{
    internal partial class MainWindowViewModel : ViewModelBase
    {
        private readonly ILogger<MainWindowViewModel> _logger;
        private IFFmpegArgumentsBuilder? _currentProcessor;
        private CancellationTokenSource? _cts;
        private Task? _mainTask;
        private Task? _frameConsumerTask;
        private readonly SemaphoreSlim _recordingGate = new(1, 1);
        private volatile bool _isClosing;
        private BitmapSwitcher _bitmapSwitcher = new BitmapSwitcher();

        //Frame Task
        private int _uiFrameCount = 0;
        private readonly Stopwatch _lastUiFpsUpdate = Stopwatch.StartNew();
        private int _currentUiFps = 0;
        private static readonly long PreviewIntervalTicks = Stopwatch.Frequency / 60;
        private long _nextPreviewTimestamp;
        private SKFont? _overlayFont;
        private readonly SKPaint _overlayPaint = new() { Color = SKColors.White, IsAntialias = true };
        private readonly SKPaint _overlayStrokePaint = new()
        {
            Color = SKColors.Black, Style = SKPaintStyle.Stroke, IsAntialias = true
        };
        private long _overlaySecond = -1;
        private int _overlayFps = -1;
        private string _overlayTimeText = string.Empty, _overlayFpsText = string.Empty;

        private readonly Channel<SKBitmap> _frameChannel = Channel.CreateBounded<SKBitmap>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            }, itemDropped: bitmap => bitmap.Dispose());

        private SKTypeface SKTypeface { get; }

        #region Binding

        private WriteableBitmap? _image = null;


        public WriteableBitmap? Image
        {
            get => _image;
            set => this.SetProperty(ref _image, value);
        }

        public ObservableCollection<InputTypeItem> InputTypeList { get; } = new ObservableCollection<InputTypeItem>()
        {
            new InputTypeItem(1, "摄像头"),
            new InputTypeItem(2, "网络流"),
            new InputTypeItem(3, "桌面"),
            new InputTypeItem(4, "视频文件"),
        };

        public ObservableCollection<string> OutputResolutions { get; } = new ObservableCollection<string>()
        {
            "3840x2160", "2560x1440", "1920x1080", "1280x720", "960x540", "640x360", "320x180"
        };

        public ObservableCollection<OutputQuality> OutputQualities { get; } = new ObservableCollection<OutputQuality>()
        {
            OutputQuality.High,
            OutputQuality.Medium,
            OutputQuality.Low,
        };

        private string? _selectOutputResolution = "1920x1080";

        public string? SelectOutputResolution
        {
            get => this._selectOutputResolution;
            set => this.SetProperty(ref _selectOutputResolution, value);
        }

        private OutputQuality? _selectOutputQuality = OutputQuality.Medium;

        public OutputQuality? SelectOutputQuality
        {
            get => this._selectOutputQuality;
            set => this.SetProperty(ref _selectOutputQuality, value);
        }

        private InputTypeItem? _inputType = null;

        public InputTypeItem? InputType
        {
            get => _inputType;
            set
            {
                switch (value?.Id)
                {
                    default:
                    case 1: //摄像头
                        {
                            IsShowStreamSource = false;
                            IsShowCamera = true;
                            IsShowDesktopSource = false;
                            IsShowFilesSource = false;

                            InitCameraDeviceList();
                        }
                        break;
                    case 2: //网络流
                        {
                            IsShowCamera = false;
                            IsShowStreamSource = true;
                            IsShowDesktopSource = false;
                            IsShowFilesSource = false;
                        }
                        break;
                    case 3:  //桌面录制
                        {
                            IsShowCamera = false;
                            IsShowStreamSource = false;
                            IsShowDesktopSource = true;
                            IsShowFilesSource = false;
                        }
                        break;
                    case 4:  //视频文件
                        {
                            IsShowCamera = false;
                            IsShowStreamSource = false;
                            IsShowDesktopSource = false;
                            IsShowFilesSource = true;
                        }
                        break;
                }

                this.SetProperty(ref _inputType, value);
            }
        }

        private bool _isRunning = false;

        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        #region Camera

        public ObservableCollection<CaptureDeviceDescriptor?> CameraDeviceList { get; } =
            new ObservableCollection<CaptureDeviceDescriptor?>();

        public CaptureDeviceDescriptor? _device = null;

        public CaptureDeviceDescriptor? CameraDevice
        {
            get => this._device;
            set
            {
                this.SetProperty(ref _device, value);
                InitCameraCharacteristics(_device);
            }
        }

        public ObservableCollection<VideoCharacteristics> CharacteristicsList { get; } =
            new ObservableCollection<VideoCharacteristics>();

        private VideoCharacteristics? _characteristics = null;

        public VideoCharacteristics? Characteristics
        {
            get => this._characteristics;
            set => this.SetProperty(ref _characteristics, value);
        }

        private bool _isShowCamera = false;

        public bool IsShowCamera
        {
            get => _isShowCamera;
            set => SetProperty(ref _isShowCamera, value);
        }

        #endregion

        #region 网络流

        private bool _isShowStreamSource = false;

        public bool IsShowStreamSource
        {
            get => _isShowStreamSource;
            set => SetProperty(ref _isShowStreamSource, value);
        }

        private string _streamSource = string.Empty;

        public string StreamSource
        {
            get => _streamSource;
            set => this.SetProperty(ref _streamSource, value);
        }

        #endregion

        #region Desktop

        private bool _isShowDesktopSource = false;

        public bool IsShowDesktopSource
        {
            get => _isShowDesktopSource;
            set => SetProperty(ref _isShowDesktopSource, value);
        }

        private string _desktopSource = "0,0";

        public string DesktopSource
        {
            get => _desktopSource;
            set => this.SetProperty(ref _desktopSource, value);
        }

        #endregion

        #region Files

        private bool _isShowFilesSource = false;

        public bool IsShowFilesSource
        {
            get => _isShowFilesSource;
            set => SetProperty(ref _isShowFilesSource, value);
        }

        private string _selectedFilePath = string.Empty;
        public string SelectedFilePath
        {
            get => _selectedFilePath;
            set => this.SetProperty(ref _selectedFilePath, value);
        }

        #endregion

        private string _btnName = string.Empty;

        public string BtnName
        {
            get => _btnName;
            set => this.SetProperty(ref _btnName, value);
        }

        private IBrush _btnBackgroundColor = Brushes.Gray;

        public IBrush BtnBackgroundColor
        {
            get => _btnBackgroundColor;
            set => this.SetProperty(ref _btnBackgroundColor, value);
        }

        private string _tips = string.Empty;

        public string Tips
        {
            get => _tips;
            set => this.SetProperty(ref _tips, value);
        }

        private MainWindow MainWindow
        {
            get
            {
                return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow
                                            ?? throw new InvalidOperationException();
            }
        }

        #endregion

        public MainWindowViewModel(ILogger<MainWindowViewModel> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            //加载默认字体
            SKTypeface = GetChineseTypeface();

            ResetUIState();
        }

#if DEBUG
#pragma warning disable CS8618

        public MainWindowViewModel()
        {
        }

#pragma warning restore CS8618
#endif

        #region UI Commond

        public void Exit()
        {
            MainWindow.Close();
        }

        public async Task ClosingAsync()
        {
            _isClosing = true;
            await _recordingGate.WaitAsync();
            try
            {
                await StopRecord();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "退出时清理录制资源失败");
            }
            finally
            {
                _overlayFont?.Dispose();
                _overlayPaint.Dispose();
                _overlayStrokePaint.Dispose();
                SKTypeface.Dispose();
                _recordingGate.Release();
                LogManager.Shutdown();
            }
        }

        private async Task ResetUIStateAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ResetUIState();
            });
        }

        private void ResetUIState()
        {
            this.Tips = "待命中...";
            this.BtnName = "开启";
            this.BtnBackgroundColor = Brushes.GreenYellow;
            this.IsRunning = false;

            if (Image != null)
            {
                Image.Dispose();
                Image = null;
            }
        }

        public async Task SelectFiles()
        {
            var storage = this.MainWindow.StorageProvider;
            var rt = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择视频文件",
                AllowMultiple = true,
                FileTypeFilter = new List<FilePickerFileType>()
                {
                    new FilePickerFileType("视频文件")
                    {
                        Patterns = new List<string>() { "*.mp4", "*.avi", "*.mov", "*.mkv", "*.flv", "*.wmv", "*.webm" }
                    }
                },
            });
            if (rt.Count == 0)
            {
                return;
            }
            else
            {
                this.SelectedFilePath = string.Join(';', rt.Select(s => s.Path.LocalPath));
            }
        }

        private volatile bool _isProcessing = false;

        public async Task Start()
        {
            // 防止重复点击
            if (_isProcessing || _isClosing)
                return;

            _isProcessing = true;
            await _recordingGate.WaitAsync();

            try
            {
                if (_isClosing) return;
                if (!IsRunning)
                {
                    // 输入验证
                    if (InputType?.Id == 1 && string.IsNullOrWhiteSpace(CameraDevice?.Name))
                    {
                        await MessageBox.Warning("错误", "摄像头配置无效");
                        return;
                    }

                    if (InputType?.Id == 2 && string.IsNullOrWhiteSpace(StreamSource))
                    {
                        await MessageBox.Warning("错误", "网络流地址无效");
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(this.SelectOutputResolution))
                    {
                        await MessageBox.Warning("错误", "请选择预览分辨率");
                        return;
                    }

                    await StartRecord().ConfigureAwait(false);
                }
                else
                {
                    await StopRecord().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "操作失败");
                await ResetAndShowError("错误", $"操作失败: {ex.Message}");
            }
            finally
            {
                _recordingGate.Release();
                _isProcessing = false;
            }
        }

        #endregion

        #region Startup

        private async Task StartRecord()
        {
            // 停止现有任务
            await StopRecord().ConfigureAwait(false);
            if (_isClosing) return;
            _nextPreviewTimestamp = 0;

            try
            {
                // 构建处理器
                _currentProcessor = InputType?.Id switch
                {
                    1 => BuildCameraProcessor(),
                    2 => BuildStreamProcessor(),
                    3 => BuildDesktopProcessor(),
                    4 => BuildFilesProcessor(),
                    _ => throw new InvalidOperationException("未知输入类型")
                };
                _cts = new CancellationTokenSource();

                string initMsg = Thread.CurrentThread.CurrentCulture.Name == "zh-CN" ? "初始化...请稍后..." : "Loading...Please waitting ...";
                (int width, int height) = ParseResolution();
                using (SKBitmap loadingBitmap = CreateLoadingImage(width, height, initMsg, this.SKTypeface))
                {
                    // 显示加载画面
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (this.Image != null)
                        {
                            this.Image.Dispose();
                            this.Image = null;
                        }
                        DrawSKBitmapToImage(loadingBitmap);
                    });
                }

                // 启动任务
                _frameConsumerTask = ConsumeFramesAsync(_cts.Token);
                _mainTask = ProcessFFMpegAsync(_currentProcessor, _cts);

                // 检查是否立即失败
                await Task.WhenAny(_mainTask, Task.Delay(500, _cts.Token)).ConfigureAwait(false);
                if (_mainTask.IsFaulted)
                    throw _mainTask.Exception?.InnerException ?? new Exception("启动失败");
                if (_mainTask.IsCompleted) return;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_mainTask?.IsCompleted != false) return;
                    this.BtnName = "关闭";
                    this.Tips = "工作中...";
                    this.BtnBackgroundColor = Brushes.Red;
                    this.IsRunning = true;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动失败");
                await StopRecord();
                await ResetAndShowError("错误", $"启动失败: {ex.Message}");
            }
        }

        private async Task StopRecord()
        {
            var cancellation = _cts;
            try
            {
                if (cancellation == null)
                    return;

                _logger.LogInformation("停止录制...");
                cancellation.Cancel();

                // 等待任务完成
                var tasks = new List<Task>();
                if (_mainTask != null)
                    tasks.Add(_mainTask);
                if (_frameConsumerTask != null)
                    tasks.Add(_frameConsumerTask);

                var allTasks = Task.WhenAll(tasks);
                await allTasks.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
            {
                _logger.LogInformation("录制已取消");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止录制错误");
                throw;
            }
            finally
            {
                // 工作线程结束后，即使发生错误也释放队列中剩余的帧。
                while (_frameChannel.Reader.TryRead(out var bitmap))
                    bitmap.Dispose();

                // 释放资源
                _mainTask?.Dispose();
                _frameConsumerTask?.Dispose();
                cancellation?.Dispose();

                _mainTask = null;
                _frameConsumerTask = null;
                _cts = null;
                _currentProcessor = null;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ResetUIState();
                    _bitmapSwitcher.Dispose();
                });
            }
        }

        private readonly object _imageSync = new();

        private async Task ConsumeFramesAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && await _frameChannel.Reader.WaitToReadAsync(token))
                {
                    SKBitmap? latestBitmap = null;
                    try
                    {
                        // 取出所有可用帧，只保留最后一帧
                        while (_frameChannel.Reader.TryRead(out SKBitmap? bitmap))
                        {
                            latestBitmap?.Dispose();
                            latestBitmap = bitmap;
                        }

                        if (latestBitmap != null)
                        {
                            await UpdateUIAsync(latestBitmap).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        latestBitmap?.Dispose();
                    }
                }
                _logger.LogInformation("帧处理队列已退出");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _logger.LogInformation("帧处理队列已取消");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "帧处理错误");
                await ResetAndShowError("错误", "操作失败: 帧处理错误，请检查日志中的详细信息");
            }
        }

        private async Task ProcessFFMpegAsync(IFFmpegArgumentsBuilder recording, CancellationTokenSource cancellation)
        {
            try
            {
                await recording.ProcessAsynchronously(cancellation.Token).ConfigureAwait(false);

                _logger.LogInformation("FFMpeg采集线程已停止");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                _logger.LogInformation("录制已取消");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FFMpeg处理错误");
                await ResetAndShowError("错误", "操作失败: 请检查日志中的详细信息");
            }
            finally
            {
                cancellation.Cancel();
                await ResetUIStateAsync();
            }
        }

        private async Task UpdateUIAsync(SKBitmap bitmap)
        {
            // 绘制FPS
            DrawFps(bitmap, _currentUiFps);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lock (_imageSync)
                {
                    DrawSKBitmapToImage(bitmap);

                    // 更新FPS计数器
                    _uiFrameCount++;
                    if (_lastUiFpsUpdate.ElapsedMilliseconds >= 1000)
                    {
                        _currentUiFps = _uiFrameCount;
                        _uiFrameCount = 0;
                        _lastUiFpsUpdate.Restart();
                    }
                }
            }, DispatcherPriority.Render);
        }

        private void DrawSKBitmapToImage(SKBitmap skBitmap)
        {
            if (skBitmap == null || skBitmap.Width == 0 || skBitmap.Height == 0)
                return;

            try
            {
                var tempImage = _bitmapSwitcher.CurrentBitmap;
                var info = new SKImageInfo(skBitmap.Width, skBitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                if (tempImage == null || tempImage.PixelSize.Width != info.Width || tempImage.PixelSize.Height != info.Height)
                {
                    tempImage?.Dispose();
                    tempImage = new WriteableBitmap(new PixelSize(info.Width, info.Height), new Vector(96.0, 96.0), PixelFormat.Bgra8888, AlphaFormat.Premul);
                }

                using (var lockedBitmap = tempImage.Lock())
                {
                    using var pixels = skBitmap.PeekPixels();
                    if (pixels == null || !pixels.ReadPixels(info, lockedBitmap.Address, lockedBitmap.RowBytes))
                        throw new InvalidOperationException("Cannot copy the preview pixels.");
                }

                _bitmapSwitcher.CurrentBitmap = tempImage;
                this._image = tempImage;
                OnPropertyChanged(nameof(Image));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WriteSKBitmapToBitmap error: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        private IFFmpegArgumentsBuilder BuildCameraProcessor()
        {
            (int width, int height) = ParseResolution();

            string? deviceName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? CameraDevice!.Name
                : CameraDevice!.Identity?.ToString();
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                throw new Exception("获取设备描述符失败");
            }

            IFFmpegArgumentsBuilder args = new FFmpegArgumentsBuilder()
                .WithCameraInput()
                .WithDeviceName(deviceName)
                .WithVideoSize((uint)Characteristics!.Width, (uint)Characteristics.Height)
                .WithFramerate((double)Characteristics.FramesPerSecond.Numerator / Characteristics.FramesPerSecond.Denominator)
                .WithImageHandle(OnImageArrived)
                .WithFrameFilter(ShouldConvertPreviewFrame)
                .WithOutputQuality(this.SelectOutputQuality ?? OutputQuality.High)
                .WithOutputSize(width, height);
            return args;
        }

        private IFFmpegArgumentsBuilder BuildStreamProcessor()
        {
            (int width, int height) = ParseResolution();

            if (!Uri.TryCreate(StreamSource, UriKind.Absolute, out Uri? uri))
            {
                throw new ArgumentException(nameof(StreamSource), "Invalid URI.");
            }

            if (uri.Scheme.StartsWith("rtsp", StringComparison.OrdinalIgnoreCase))
            {
                IFFmpegArgumentsBuilder args = new FFmpegArgumentsBuilder()
                    .WithStreamInput()
                    .WithRtsp(uri)
                    .WithTimeout(3)
                    .WithImageHandle(OnImageArrived)
                    .WithFrameFilter(ShouldConvertPreviewFrame)
                    .WithOutputQuality(this.SelectOutputQuality ?? OutputQuality.High)
                    .WithOutputSize(width, height);
                return args;
            }
            else if (uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                IFFmpegArgumentsBuilder args = new FFmpegArgumentsBuilder()
                    .WithStreamInput()
                    .WithHttp(uri)
                    .WithTimeout(3)
                    .WithImageHandle(OnImageArrived)
                    .WithFrameFilter(ShouldConvertPreviewFrame)
                    .WithOutputQuality(this.SelectOutputQuality ?? OutputQuality.High)
                    .WithOutputSize(width, height)
                    .WithLatencyOptimizationLevel(LatencyOptimizationLevel.Medium);
                return args;
            }
            else if (uri.Scheme.StartsWith("rtmp", StringComparison.OrdinalIgnoreCase))
            {
                IFFmpegArgumentsBuilder args = new FFmpegArgumentsBuilder()
                    .WithStreamInput()
                    .WithRtmp(uri)
                    .WithImageHandle(OnImageArrived)
                    .WithFrameFilter(ShouldConvertPreviewFrame)
                    .WithOutputQuality(this.SelectOutputQuality ?? OutputQuality.High)
                    .WithOutputSize(width, height)
                    .WithLatencyOptimizationLevel(LatencyOptimizationLevel.Medium);
                return args;
            }
            throw new Exception("Unsupported URI scheme");
        }

        private IFFmpegArgumentsBuilder BuildDesktopProcessor()
        {
            if (!ScreenParamsHelper.TryParse(this.DesktopSource, out var message, out var rectangle) || rectangle == null)
            {
                throw new ArgumentException(message);
            }

            (int width, int height) = ParseResolution();

            IFFmpegArgumentsBuilder args = new FFmpegArgumentsBuilder()
                .WithDesktopInput()
                .WithRectangle(rectangle.Value)
                .WithFramerate(60)
                .WithImageHandle(OnImageArrived)
                .WithFrameFilter(ShouldConvertPreviewFrame)
                .WithOutputQuality(this.SelectOutputQuality ?? OutputQuality.High)
                .WithOutputSize(width, height);
            return args;
        }

        private IFFmpegArgumentsBuilder BuildFilesProcessor()
        {
            if (string.IsNullOrWhiteSpace(this.SelectedFilePath))
            {
                throw new ArgumentException("未选择文件");
            }
            string[] files = this.SelectedFilePath.Split(';');

            (int width, int height) = ParseResolution();

            IFFmpegArgumentsBuilder args = new FFmpegArgumentsBuilder()
                .WithFileInput()
                .WithFiles(files)
                .WithImageHandle(OnImageArrived)
                .WithFrameFilter(ShouldConvertPreviewFrame)
                .WithOutputQuality(this.SelectOutputQuality ?? OutputQuality.High)
                .WithOutputSize(width, height);
            return args;
        }

        private bool ShouldConvertPreviewFrame(long frameIndex)
        {
            long now = Stopwatch.GetTimestamp();
            if (now < _nextPreviewTimestamp) return false;
            _nextPreviewTimestamp = now + PreviewIntervalTicks;
            return true;
        }

        private void OnImageArrived(long frameIndex, SKBitmap? bitmap)
        {
            if (bitmap == null)
            {
                return;
            }

            try
            {
                if (!_frameChannel.Writer.TryWrite(bitmap))
                {
                    bitmap.Dispose();
                }
            }
            catch (Exception ex)
            {
                bitmap.Dispose();
                _logger.LogError(ex, "帧入队失败");
            }
        }

        private async Task ResetAndShowError(string title, string content)
        {
            if (_isClosing) return;
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (_isClosing) return;
                await MessageBox.Warning(title, content);
                await ResetUIStateAsync();
            });
        }

        private (int width, int height) ParseResolution()
        {
            if (string.IsNullOrWhiteSpace(this.SelectOutputResolution))
            {
                throw new ArgumentException("预览分辨率不能为空");
            }

            string[] parts = this.SelectOutputResolution.Split('x');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int width) || !int.TryParse(parts[1], out int height))
            {
                throw new ArgumentException("分辨率格式错误");
            }

            return (width, height);
        }

        /// <summary>
        /// 获取中文字体（多平台兼容方案）
        /// </summary>
        private SKTypeface GetChineseTypeface()
        {
            string[] fontFamilies =
            {
                "Microsoft YaHei", // Windows
                "PingFang SC", // macOS
                "Noto Sans CJK SC", // Linux
                "Source Han Sans SC" // Adobe字体
            };

            foreach (var font in fontFamilies)
            {
                var tf = SKTypeface.FromFamilyName(font, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
                    SKFontStyleSlant.Upright);
                if (tf != null)
                    return tf;
            }

            using (Stream stream = AssetLoader.Open(new Uri($"avares://{typeof(ViewModels.MainWindowViewModel).Assembly.GetName().Name}/Assets/Fonts/SourceHanSansCN.otf")))
            {
                var type = SKTypeface.FromStream(stream);
                if (type != null)
                    return type;
            }

            throw new Exception("无法加载默认字体");
        }

        private SKBitmap CreateLoadingImage(int width, int height, string text, SKTypeface? typeface = null)
        {
            var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.White);

                using SKFont font = new SKFont()
                {
                    Size = GetBaseFontSize(48, bitmap.Height),
                    Typeface = typeface ?? SKTypeface.Default,
                };

                using var textPaint = new SKPaint
                {
                    Color = SKColors.Black,
                    IsAntialias = true,
                };
                SKFontMetrics metrics = font.Metrics;
                float y = bitmap.Height / 2f - (metrics.Ascent + metrics.Descent) / 2f;
                canvas.DrawText(text, bitmap.Width / 2f, y, SKTextAlign.Center, font, textPaint);
            }
            return bitmap;
        }

        /// <summary>
        /// 使用 SkiaSharp 绘制 FPS 到 SKBitmap（支持分辨率自适应）
        /// </summary>
        private void DrawFps(SKBitmap? bitmap, int fps)
        {
            if (bitmap == null) return;

            using (SKCanvas canvas = new SKCanvas(bitmap))
            {
                float fontSize = GetBaseFontSize(30, bitmap.Height);

                var font = _overlayFont ??= new SKFont(this.SKTypeface, fontSize);
                font.Size = fontSize;
                font.GetFontMetrics(out var metrics);
                var paint = _overlayPaint;
                var strokePaint = _overlayStrokePaint;
                strokePaint.StrokeWidth = Math.Max(2, fontSize / 15);

                float x = 10;
                float startY = -metrics.Ascent;
                float lineHeight = fontSize * 1.2f;

                var now = DateTime.Now;
                long second = now.Ticks / TimeSpan.TicksPerSecond;
                if (_overlaySecond != second)
                {
                    _overlayTimeText = now.ToString("yyyy-MM-dd HH:mm:ss");
                    _overlaySecond = second;
                }
                if (_overlayFps != fps)
                {
                    _overlayFpsText = $"FPS: {fps}";
                    _overlayFps = fps;
                }
                var timeText = _overlayTimeText;
                var fpsText = _overlayFpsText;

                canvas.DrawText(timeText, x, startY, font, strokePaint);
                canvas.DrawText(timeText, x, startY, font, paint);

                float fpsY = startY + lineHeight;
                canvas.DrawText(fpsText, x, fpsY, font, strokePaint);
                canvas.DrawText(fpsText, x, fpsY, font, paint);
            }
        }

        private float GetBaseFontSize(float baseFontSize, int height)
        {
            const float baseResolution = 720f;
            float fontSize = height == baseResolution
                ? baseFontSize
                : baseFontSize * (height / baseResolution);
            return fontSize;
        }
        #endregion

        #region Camera

        private void InitCameraDeviceList()
        {
            string? selectedCameraDeviceIdentity = this.CameraDevice?.Identity?.ToString();

            var devices = new CaptureDevices();
            this.CameraDeviceList.Clear();
            this.CameraDevice = null;
            this.CharacteristicsList.Clear();
            this.Characteristics = null;

            var canListDevice = devices.EnumerateDescriptors()
                .Where(d => d.Characteristics.Length >= 1 && !d.Name.Equals("Default", StringComparison.Ordinal));
            if (canListDevice?.Any() != true)
            {
                return;
            }

            foreach (var item in canListDevice)
            {
                this.CameraDeviceList.Add(item);
            }

            if (!string.IsNullOrWhiteSpace(selectedCameraDeviceIdentity) &&
                this.CameraDeviceList?.Any(s => s?.Identity?.ToString() == selectedCameraDeviceIdentity) == true)
            {
                this.CameraDevice =
                    this.CameraDeviceList?.FirstOrDefault(s => s?.Identity?.ToString() == selectedCameraDeviceIdentity);
            }
            else
            {
                this.CameraDevice = this.CameraDeviceList?.FirstOrDefault();
            }
        }

        private void InitCameraCharacteristics(CaptureDeviceDescriptor? captureDevice)
        {
            string? selectedCharacteristicsIdentity = this.Characteristics?.ToString();

            this.CharacteristicsList.Clear();
            this.Characteristics = null;
            if (captureDevice == null) return;

            VideoCharacteristics[]? characteristics = this.CameraDevice?.Characteristics;
            if (characteristics?.Any() != true)
            {
                return;
            }

            foreach (var item in characteristics)
            {
                if (item.PixelFormat == FlashCap.PixelFormats.Unknown)
                {
                    continue;
                }

                this.CharacteristicsList.Add(item);
            }

            if (!string.IsNullOrWhiteSpace(selectedCharacteristicsIdentity) &&
                this.CharacteristicsList?.Any(s => s?.ToString() == selectedCharacteristicsIdentity) == true)
            {
                this.Characteristics = this.CharacteristicsList?.FirstOrDefault(s => s?.ToString() == selectedCharacteristicsIdentity);
            }
            else
            {
                var targetCharacteristics = CharacteristicsList?.FirstOrDefault(p =>
                    p.Width == 1920 && p.Height == 1080 && p.PixelFormat != FlashCap.PixelFormats.Unknown);
                if (targetCharacteristics != null)
                {
                    this.Characteristics = targetCharacteristics;
                }
                else
                {
                    targetCharacteristics = CharacteristicsList?.FirstOrDefault(p =>
                        p.Width == 1280 && p.Height == 720 && p.PixelFormat != FlashCap.PixelFormats.Unknown);
                    if (targetCharacteristics != null)
                    {
                        this.Characteristics = targetCharacteristics;
                    }
                }
            }
        }

        #endregion
    }
}
