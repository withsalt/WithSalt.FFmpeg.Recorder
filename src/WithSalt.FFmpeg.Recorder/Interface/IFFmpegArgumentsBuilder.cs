using SkiaSharp;
using WithSalt.FFmpeg.Recorder.Models;

namespace WithSalt.FFmpeg.Recorder.Interface;

/// <summary>Configures and executes native video decoding. Configure before starting a run.</summary>
public interface IFFmpegArgumentsBuilder
{
    /// <summary>Runs until EOF or cancellation. Cancellation returns a canceled task; awaiting it throws OperationCanceledException.</summary>
    Task ProcessAsynchronously(CancellationToken cancellationToken = default);
    /// <summary>Runs until EOF or cancellation. Cancellation throws OperationCanceledException.</summary>
    void ProcessSynchronously(CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册独立图像回调。每帧位图由调用方管理，适合异步分析、UI 预览或需要保留图像的场景。
    /// </summary>
    /// <param name="action">
    /// 图像回调：第一个参数为输出帧号，每次执行从 1 开始，跨输入文件连续递增，筛选丢帧时可能不连续；
    /// 第二个参数为独立的 BGRA8888 / Unpremul 位图。
    /// </param>
    /// <returns>当前构建器，可继续链式配置。</returns>
    /// <remarks>
    /// <para>
    /// 每帧拥有独立的位图和像素生命周期，可读取、修改或保存引用；后续解码不会覆盖仍被持有的像素。
    /// 回调成功返回后，由调用方负责最终调用 <c>Dispose()</c>；位图可以在采集结束后继续使用。
    /// 同步处理时可在回调内用 <c>using</c> 释放；异步处理时交给有界队列，消费完成或丢帧时都必须释放。
    /// </para>
    /// <para>
    /// 回调在解码线程同步执行，即使使用 <see cref="ProcessAsynchronously"/> 也不会异步调用回调。
    /// 慢回调会阻塞采集，禁止使用 <c>async void</c> 回调。
    /// 回调抛出异常时，库释放当前位图、停止采集，并将异常传给执行方法；此时不要继续使用该位图。
    /// </para>
    /// <para>
    /// 此方法会替换 <see cref="WithImageViewHandle"/> 配置，两种回调以最后一次配置为准。
    /// 如果只需在回调内同步读取图像，可选择 <see cref="WithImageViewHandle"/> 减少对象分配和像素复制。
    /// </para>
    /// </remarks>
    /// <example>
    /// 同步处理：使用完毕后立即释放独立位图。
    /// <code>
    /// builder.WithImageHandle((frameIndex, bitmap) =>
    /// {
    ///     using (bitmap)
    ///     {
    ///         SKColor pixel = bitmap.GetPixel(0, 0);
    ///         // 在此完成同步处理，也可以修改 bitmap 的像素。
    ///     }
    /// });
    /// </code>
    /// 异步处理：假设 frameQueue 是有界队列，消费者及队列丢帧回调均负责释放位图。
    /// <code>
    /// builder.WithImageHandle((frameIndex, bitmap) =>
    /// {
    ///     if (!frameQueue.Writer.TryWrite(bitmap))
    ///         bitmap.Dispose(); // 未交给消费者时，由生产者释放。
    /// });
    /// // 消费者读取后处理图像，并在 finally 或 using 中释放 bitmap。
    /// // 如果队列使用 DropOldest/DropNewest/DropWrite，必须配置丢帧回调释放被丢弃的位图。
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">正在执行采集，不能修改配置。</exception>
    IFFmpegArgumentsBuilder WithImageHandle(Action<long, SKBitmap> action);

    /// <summary>
    /// 注册只读图像视图回调。库复用位图和转换缓冲区，适合在回调内同步完成像素读取的高性能场景。
    /// </summary>
    /// <param name="action">
    /// 图像回调：第一个参数为输出帧号，每次执行从 1 开始，跨输入文件连续递增，筛选丢帧时可能不连续；
    /// 第二个参数为库管理的 BGRA8888 / Unpremul 图像视图，仅在当前回调期间有效。
    /// </param>
    /// <returns>当前构建器，可继续链式配置。</returns>
    /// <remarks>
    /// <para>
    /// 调用方不拥有该位图，禁止调用 <c>Dispose()</c>、修改像素、保存位图引用或直接入队。
    /// 通过该位图获取的像素指针、像素视图及共享像素的对象也不能跨回调持有。
    /// 回调返回后，库可能立即复用或释放其像素；只读和有效期是使用约定，<see cref="SKBitmap"/> 类型本身不会强制限制。
    /// </para>
    /// <para>
    /// 回调在解码线程同步执行，即使使用 <see cref="ProcessAsynchronously"/> 也不会异步调用回调。
    /// 慢回调会阻塞采集，禁止使用 <c>async void</c> 回调；回调抛出异常会停止采集，并将异常传给执行方法。
    /// </para>
    /// <para>
    /// 需要异步处理或保留图像时，优先使用 <see cref="WithImageHandle"/>。
    /// 也可以在当前回调内调用 <c>bitmap.Copy()</c> 创建独立副本，由调用方负责释放副本；逐帧复制会抵消主要复用收益。
    /// </para>
    /// <para>
    /// 同尺寸、正步长 BGRA 输入可直接查看解码帧像素，省去整帧复制；其他格式或缩放仍需像素转换。
    /// 此方法会替换 <see cref="WithImageHandle"/> 配置，两种回调以最后一次配置为准。
    /// </para>
    /// </remarks>
    /// <example>
    /// 同步读取视图：直接完成计算，不释放或保存 bitmap。
    /// <code>
    /// builder.WithImageViewHandle((frameIndex, bitmap) =>
    /// {
    ///     SKColor pixel = bitmap.GetPixel(0, 0);
    ///     // 在此同步读取像素、计算特征；可以保留独立的计算结果。
    ///     // 不要使用 using (bitmap)、Dispose、修改像素或把 bitmap 交给后台任务。
    /// });
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">正在执行采集，不能修改配置。</exception>
    IFFmpegArgumentsBuilder WithImageViewHandle(Action<long, SKBitmap> action);
    /// <summary>
    /// Evaluated before bitmap allocation/conversion for each scheduled output frame.
    /// Return false to skip image delivery; frame indices and progress still advance.
    /// Runs on the decoding thread. Exceptions stop decoding.
    /// </summary>
    IFFmpegArgumentsBuilder WithFrameFilter(Func<long, bool> predicate);
    /// <summary>
    /// Sets decoder threads (0 lets FFmpeg choose, 1 minimizes thread/buffer overhead).
    /// With live latency presets, only slice threading is allowed to avoid frame-thread delay.
    /// </summary>
    IFFmpegArgumentsBuilder WithDecoderThreads(int threads);
    IFFmpegArgumentsBuilder WithOutputFramerate(double framerate = 30);
    IFFmpegArgumentsBuilder WithFpsMode(FpsMode fpsMode = FpsMode.Passthrough);
    IFFmpegArgumentsBuilder WithOutputQuality(OutputQuality quality = OutputQuality.Medium);
    IFFmpegArgumentsBuilder WithOutputSize(int width, int height);
    IFFmpegArgumentsBuilder WithLatencyOptimizationLevel(LatencyOptimizationLevel level);
    /// <summary>Reports media time on the decoding thread, throttled by wall-clock time.</summary>
    IFFmpegArgumentsBuilder NotifyOnProgress(Action<FFmpegProgress> action, TimeSpan? interval = null);
}
