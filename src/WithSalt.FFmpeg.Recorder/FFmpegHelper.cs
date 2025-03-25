using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using FFMpegCore;

namespace WithSalt.FFmpeg.Recorder
{
    public class FFmpegHelper
    {
        private static string[] _defaultSeachFolders = CreateDefaultSeachFolders();

        /// <summary>
        /// 设置默认FFmpeg路径参数
        /// </summary>
        public static void SetDefaultFFmpegLoador()
        {
            GlobalFFOptions.Configure(options =>
            {
                options.BinaryFolder = GetBinaryFolder();
                options.TemporaryFilesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp");
                options.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                options.Encoding = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Encoding.Default : Encoding.UTF8;

                if (!Directory.Exists(options.TemporaryFilesFolder))
                {
                    Directory.CreateDirectory(options.TemporaryFilesFolder);
                }
            });
        }

        public static string GetBinaryFilePath()
        {
            string binName;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                binName = "ffmpeg.exe";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                binName = "ffmpeg";
            else
                throw new PlatformNotSupportedException($"Unsupported system type: {RuntimeInformation.OSDescription}");
            string binaryFolder = !string.IsNullOrWhiteSpace(GlobalFFOptions.Current.BinaryFolder)
                ? GlobalFFOptions.Current.BinaryFolder
                : GetBinaryFolder();
            string path = Path.Combine(binaryFolder, binName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("FFmpeg not found, please download or install ffmpeg at first.", path);
            }
            return path;
        }

        public static string GetBinaryFolder()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                //从提供的默认路径开始搜索
                foreach (var defaultFolder in _defaultSeachFolders)
                {
                    string path = Path.Combine(defaultFolder, "ffmpeg.exe");
                    if (File.Exists(path))
                    {
                        return defaultFolder;
                    }
                }
                //从系统环境变量开始搜索
                string? allPathEnvs = Environment.GetEnvironmentVariable("Path");
                if (!string.IsNullOrWhiteSpace(allPathEnvs))
                {
                    string[] allPath = allPathEnvs.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    if (allPath != null && allPath.Length > 0)
                    {
                        foreach (var pathItem in allPath)
                        {
                            string ffmpegPath = Path.Combine(pathItem, "ffmpeg.exe");
                            if (File.Exists(ffmpegPath))
                            {
                                return pathItem;
                            }
                        }
                    }
                }
                throw new FileNotFoundException($"FFmpeg not found, please download ffmpeg to the path {string.Join(" or ", _defaultSeachFolders)}.");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                //从提供的默认路径开始搜索
                foreach (var defaultFolder in _defaultSeachFolders)
                {
                    string path = Path.Combine(defaultFolder, "ffmpeg");
                    if (File.Exists(path))
                    {
                        return defaultFolder;
                    }
                }

                string[] systemPaths = new string[]
                {
                    "/usr/bin", "/usr/local/bin", "/usr/share",
                };

                foreach (var systemPath in systemPaths)
                {
                    string path = Path.Combine(systemPath, "ffmpeg");
                    if (File.Exists(path))
                    {
                        return systemPath;
                    }
                }
                throw new FileNotFoundException("FFmpeg not found, please install ffmpeg at first. In a Debian OS, you can use 'apt install ffmpeg' to install it.");
            }
            else
            {
                throw new PlatformNotSupportedException($"Unsupported system type: {RuntimeInformation.OSDescription}");
            }
        }

        #region private

        private static string GetProcessArchitecturePath()
        {
            string architecture = string.Empty;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                architecture = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X86 => "win-x86",
                    Architecture.X64 => "win-x64",
                    Architecture.Arm64 => "win-arm64",
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                architecture = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "linux-x64",
                    Architecture.Arm => "linux-arm",
                    Architecture.Arm64 => "linux-arm64",
#if NET7_0_OR_GREATER
                    Architecture.LoongArch64 => "linux-loongarch64",
#endif
                    _ => throw new PlatformNotSupportedException($"Unsupported processor architecture: {RuntimeInformation.ProcessArchitecture}"),
                };
            }
            else
            {
                throw new PlatformNotSupportedException($"Unsupported system type: {RuntimeInformation.OSDescription}");
            }
            return architecture;
        }

        /// <summary>
        /// 创建默认的本地库搜索路径集合。
        /// 该方法生成一组用于定位本地依赖库（如DLL）的目录路径，
        /// 路径按优先级从高到低包含以下位置：
        /// 1. 当前运行环境对应架构的运行时目录（如runtimes/win-x64/bin）
        /// 2. 通用二进制文件目录（bin）
        /// 3. 应用程序根目录和当前工作目录
        /// </summary>
        /// <returns>去重后的绝对路径数组</returns>
        /// <remarks>
        /// 路径生成规则：
        /// - 优先查找与当前进程架构匹配的运行时目录（通过 GetProcessArchitecturePath() 获取架构标识）
        /// - 包含开发环境（源目录）和生产环境的路径配置
        /// - 所有路径均转换为标准化绝对路径并去除末尾分隔符
        /// </remarks>
        private static string[] CreateDefaultSeachFolders()
        {
            HashSet<string> folders = new HashSet<string>();

            string processArchitecture = GetProcessArchitecturePath();
            if (!string.IsNullOrWhiteSpace(processArchitecture))
            {
                folders.Add(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", GetProcessArchitecturePath(), "bin")).TrimEnd(Path.DirectorySeparatorChar));
                folders.Add(Path.GetFullPath(Path.Combine(".", "runtimes", GetProcessArchitecturePath(), "bin")).TrimEnd(Path.DirectorySeparatorChar));
            }

            folders.Add(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin")).TrimEnd(Path.DirectorySeparatorChar));
            folders.Add(Path.GetFullPath(Path.Combine(".", "bin")).TrimEnd(Path.DirectorySeparatorChar));
            folders.Add(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory)).TrimEnd(Path.DirectorySeparatorChar));
            folders.Add(Path.GetFullPath(Path.Combine(".")).TrimEnd(Path.DirectorySeparatorChar));

            return folders.ToArray();
        }

        #endregion

    }
}
