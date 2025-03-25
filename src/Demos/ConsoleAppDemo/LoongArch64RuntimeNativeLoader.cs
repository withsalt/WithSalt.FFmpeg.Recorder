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
        private static readonly string RuntimeDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "runtimes", "linux-loongarch64", "native");

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

            NativeLibrary.SetDllImportResolver(assembly,
                (name, asm, path) => { return name == libraryName ? NativeLibrary.Load(libraryPath) : IntPtr.Zero; });
        }

        private static string GetSkiaLibraryPath()
        {
            return Path.Combine(RuntimeDirectory,
                RuntimeInformationHelper.IsABI1() ? Path.Combine("ABI1.0", "libSkiaSharp.so") : "libSkiaSharp.so");
        }
    }

    /// <summary>
    /// Helper class for runtime environment information, especially for LoongArch64 architecture.
    /// </summary>
    internal static class RuntimeInformationHelper
    {
        // Pre-compile the regex pattern for kernel version matching
        private static readonly Regex KernelVersionRegex = new(
            @"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<prerelease>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+(?<buildmetadata>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$",
            RegexOptions.Compiled);

        private static readonly Lazy<bool> _isABI1 = new(DetermineIsABI1);

        // Version threshold for ABI2
        private static readonly Version Abi2MinVersion = new(5, 19);

        // Common executable files to read ELF mark from
        private static readonly string[] ElfExecutables = { "/usr/bin/sh", "/bin/sh", "/usr/bin/bash" };

        /// <summary>
        /// Determines if the current runtime is using ABI1 on LoongArch64 Linux.
        /// </summary>
        /// <returns>True if running on ABI1, false otherwise.</returns>
        public static bool IsABI1() => _isABI1.Value;

        /// <summary>
        /// Determines if the current runtime is LoongArch64 architecture on Linux OS.
        /// </summary>
        /// <returns>True if running on LoongArch64 Linux, false otherwise.</returns>
        public static bool IsLoongArch64Linux() =>
            RuntimeInformation.ProcessArchitecture == Architecture.LoongArch64 &&
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        /// <summary>
        /// Determines if the current LoongArch64 Linux environment is using ABI1.
        /// </summary>
        /// <returns>True if ABI1 is detected, false otherwise.</returns>
        private static bool DetermineIsABI1()
        {
            // Quick check - if not on LoongArch64 Linux, definitely not ABI1
            if (!IsLoongArch64Linux())
            {
                return false;
            }

            // Strategy 1: Check ELF header mark
            string elfMark = ReadELFMark();
            if (elfMark == "03") return true;
            if (elfMark == "43") return false;

            // Strategy 2: Check kernel version
            Version? kernelVersion = DetectKernelVersion();
            if (kernelVersion != null)
            {
                return kernelVersion < Abi2MinVersion;
            }

            // Strategy 3: Check OS description
            return RuntimeInformation.OSDescription.Contains("Loongnix GNU/Linux 20");
        }

        /// <summary>
        /// Detects the kernel version using available methods.
        /// </summary>
        /// <returns>The kernel version or null if detection failed.</returns>
        private static Version? DetectKernelVersion()
        {
            // Try uname syscall first
            string? kernelVersionStr = GetLinuxKernelVersionByUname();
            if (TryMatchKernelVersion(kernelVersionStr, out var version))
            {
                return version;
            }

            // Fall back to process execution
            kernelVersionStr = GetLinuxKernelVersionByProcess();
            TryMatchKernelVersion(kernelVersionStr, out version);

            return version;
        }

        /// <summary>
        /// Reads the ELF header mark from common executable files.
        /// </summary>
        /// <returns>The ELF mark as a hexadecimal string or empty string if reading failed.</returns>
        private static string ReadELFMark()
        {
            foreach (var filePath in ElfExecutables)
            {
                if (!File.Exists(filePath))
                {
                    continue;
                }

                try
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    fs.Seek(48, SeekOrigin.Begin);
                    int byteValue = fs.ReadByte();
                    if (byteValue != -1)
                    {
                        string hexValue = byteValue.ToString("X2");
                        if (hexValue is "43" or "03")
                        {
                            return hexValue;
                        }
                    }
                }
                catch (Exception)
                {
                    // Continue trying with next file
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Attempts to parse a kernel version string into a Version object.
        /// </summary>
        /// <param name="kernelVersion">The kernel version string.</param>
        /// <param name="version">The parsed Version object.</param>
        /// <returns>True if parsing succeeded, false otherwise.</returns>
        private static bool TryMatchKernelVersion(string? kernelVersion, out Version? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(kernelVersion))
            {
                return false;
            }

            Match match = KernelVersionRegex.Match(kernelVersion);
            if (match.Success &&
                match.Groups.TryGetValue("major", out Group? major) && !string.IsNullOrWhiteSpace(major?.Value) &&
                match.Groups.TryGetValue("minor", out Group? minor) && !string.IsNullOrWhiteSpace(minor?.Value) &&
                Version.TryParse($"{major.Value}.{minor.Value}", out Version? parsedVersion))
            {
                version = parsedVersion;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the Linux kernel version by executing the uname command.
        /// </summary>
        /// <returns>The kernel version string or empty string if execution failed.</returns>
        private static string GetLinuxKernelVersionByProcess()
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "uname",
                        Arguments = "-r",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return output;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int uname(IntPtr buf);

        /// <summary>
        /// Gets the Linux kernel version using the uname system call.
        /// </summary>
        /// <returns>The kernel version string or empty string if the call failed.</returns>
        private static string? GetLinuxKernelVersionByUname()
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                buf = Marshal.AllocHGlobal(400);
                if (uname(buf) == 0)
                {
                    return Marshal.PtrToStringAnsi(buf + 130);
                }

                return string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
            finally
            {
                if (buf != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
        }
    }
}