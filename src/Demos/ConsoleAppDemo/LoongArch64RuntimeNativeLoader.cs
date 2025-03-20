using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace ConsoleAppDemo
{
    internal static class LoongArch64RuntimeNativeLoader
    {
        private static readonly string RuntimeDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "linux-loongarch64", "native");

        public static void Load()
        {
            if (!RuntimeInformationHelper.IsLoongArch64Linux())
            {
                return;
            }

            LoadLibrary(typeof(SKBitmap).Assembly, "libSkiaSharp", GetSkiaLibraryPath());
        }

        private static void LoadLibrary(Assembly assembly, string libraryName, string libraryPath)
        {
            if (!File.Exists(libraryPath))
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(assembly, (name, asm, path) =>
            {
                return name == libraryName ? NativeLibrary.Load(libraryPath) : IntPtr.Zero;
            });
        }

        private static string GetSkiaLibraryPath()
        {
            return Path.Combine(RuntimeDirectory, RuntimeInformationHelper.IsABI1() ? Path.Combine("ABI1.0", "libSkiaSharp.so") : "libSkiaSharp.so");
        }
    }

    internal static class RuntimeInformationHelper
    {
        private static readonly Lazy<bool> _isABI1 = new Lazy<bool>(DetermineIsABI1);

        public static bool IsABI1() => _isABI1.Value;

        public static bool IsLoongArch64Linux()
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.LoongArch64
                && RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        }

        private static bool DetermineIsABI1()
        {
            if (!IsLoongArch64Linux())
            {
                return false;
            }

            // 1. 读取ELF标记
            string elfMark = ReadELFMark();
            if (elfMark == "03") return true;
            if (elfMark == "43") return false;

            // 2. 通过内核版本判断
            string kernelVersion = GetLinuxKernelVersion();
            if (!string.IsNullOrWhiteSpace(kernelVersion))
            {
                Match match = Regex.Match(kernelVersion, @"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<prerelease>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+(?<buildmetadata>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$");
                if (match.Success
                    && match.Groups.TryGetValue("major", out Group? major) && !string.IsNullOrWhiteSpace(major?.Value)
                    && match.Groups.TryGetValue("minor", out Group? minor) && !string.IsNullOrWhiteSpace(minor?.Value)
                    && Version.TryParse($"{major.Value}.{minor.Value}", out Version? getkernelVersion) && getkernelVersion != null
                    && getkernelVersion < new Version(5, 19))
                {
                    return true;
                }
            }

            // 3. 根据操作系统描述判断
            string osDescription = RuntimeInformation.OSDescription;
            if (osDescription.Contains("Loongnix GNU/Linux 20"))
            {
                return true;
            }

            return false;
        }

        private static string ReadELFMark()
        {
            string[] readFilePaths = { "/usr/bin/sh" };
            foreach (var filePath in readFilePaths)
            {
                if (!File.Exists(filePath))
                {
                    continue;
                }

                try
                {
                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        fs.Seek(48, SeekOrigin.Begin);
                        int byteValue = fs.ReadByte();
                        if (byteValue != -1)
                        {
                            string hexValue = byteValue.ToString("X2");
                            if (hexValue == "43" || hexValue == "03")
                            {
                                return hexValue;
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略异常，继续尝试下一个文件
                }
            }
            return string.Empty;
        }

        private static string GetLinuxKernelVersion()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "uname",
                    Arguments = "-r",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = psi })
                {
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    return output;
                }
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}