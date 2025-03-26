using System;
using System.Collections.Generic;
using System.Text;
using FFMpegCore.Arguments;
using WithSalt.FFmpeg.Recorder.Models;

namespace WithSalt.FFmpeg.Recorder.Builder.Providers
{
    internal class LatencyOptimizationContainer
    {
        public LatencyOptimizationLevel Level { get; private set; } = LatencyOptimizationLevel.High;

        public Dictionary<LatencyOptimizationLevel, List<IArgument>> Container = new Dictionary<LatencyOptimizationLevel, List<IArgument>>()
        {
            { LatencyOptimizationLevel.None, new List<IArgument>() },
            { LatencyOptimizationLevel.Medium, new List<IArgument>() },
            { LatencyOptimizationLevel.High, new List<IArgument>() }
        };

        public LatencyOptimizationContainer()
        {
            this.Container[LatencyOptimizationLevel.Medium].AddRange(CreateDefaultLowDelayArguments(LatencyOptimizationLevel.Medium));
            this.Container[LatencyOptimizationLevel.High].AddRange(CreateDefaultLowDelayArguments(LatencyOptimizationLevel.High));
        }

        private static readonly object _locker = new object();

        public void SetLevel(LatencyOptimizationLevel level)
        {
            lock (_locker)
            {
                this.Level = level;
            }
        }

        /// <summary>
        /// 创建低延迟参数组合
        /// </summary>
        /// <param name="probeSize">值的大小影响输入流探测行为</param>
        /// <returns></returns>
        private List<IArgument> CreateDefaultLowDelayArguments(LatencyOptimizationLevel level)
        {

            List<IArgument> arguments = new List<IArgument>();
            switch (level)
            {
                case LatencyOptimizationLevel.None:
                    return arguments;
                case LatencyOptimizationLevel.Medium:
                    {
                        //禁用输入流的缓冲机制，避免数据在内存中堆积，实现实时处理 + 丢弃损坏的包
                        arguments.Add(new CustomArgument("-fflags nobuffer+discardcorrupt"));

                        //启用全局低延迟模式，强制解码器 / 复用器优先处理时效性
                        arguments.Add(new CustomArgument("-flags low_delay"));

                        //允许低延迟参数组合
                        arguments.Add(new CustomArgument($"-strict experimental"));

                        //控制输入流探测行为的关键参数，其作用直接影响流的初始化和延迟表现
                        //默认值较高（几 MB）：FFmpeg 会读取较多数据确保格式正确，但导致初始延迟增加
                        arguments.Add(new CustomArgument($"-probesize 3M"));
                    }
                    break;
                default:
                case LatencyOptimizationLevel.High:
                    {
                        //禁用输入流的缓冲机制，避免数据在内存中堆积，实现实时处理 + 丢弃损坏的包
                        arguments.Add(new CustomArgument("-fflags nobuffer+discardcorrupt"));

                        //启用全局低延迟模式，强制解码器 / 复用器优先处理时效性
                        arguments.Add(new CustomArgument("-flags low_delay"));

                        //允许低延迟参数组合
                        arguments.Add(new CustomArgument($"-strict experimental"));

                        //控制输入 / 输出线程间数据队列缓冲大小的关键参数，直接影响实时流处理的延迟和稳定性
                        //队列越大：能缓冲更多数据，抗突发流量波动能力越强，但内存占用高、延迟增加；队列越小：实时性更好、内存占用低，但容易因处理不及时导致丢帧或错误
                        arguments.Add(new CustomArgument($"-thread_queue_size 1MB"));

                        //控制输入流探测行为的关键参数，其作用直接影响流的初始化和延迟表现
                        //默认值较高（几 MB）：FFmpeg 会读取较多数据确保格式正确，但导致初始延迟增加
                        //设为较小值（如 - probesize 64）：强制 FFmpeg 快速决策，跳过深度探测，显著降低初始延迟
                        arguments.Add(new CustomArgument($"-probesize 128"));

                        //禁用格式探测延迟
                        arguments.Add(new CustomArgument("-analyzeduration 0"));

                        //直接访问输入数据（绕过缓存层），减少内存拷贝带来的延迟
                        arguments.Add(new CustomArgument("-avioflags direct"));
                    }
                    break;
            }

            return arguments;
        }
    }
}
