using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace WithSalt.FFmpeg.Recorder.Native;

// FFmpeg.AutoGen's vectors retain function pointers globally. Handles intentionally live
// for the lifetime of the process; unloading them after a session would leave dangling pointers.
internal sealed class NativeFunctionResolver : IFunctionResolver
{
    private readonly object _gate = new();
    private readonly Dictionary<string, nint> _handles = new();
    private readonly string? _directory;
    private NativeAbi? _abi;

    internal NativeFunctionResolver(string? directory)
    {
        if (directory is not null) { _directory = directory; return; }
        string rid = NativePlatform.RuntimeIdentifier;
        _directory = FindDirectory(SearchDirectories(AppContext.BaseDirectory, rid, NativePlatform.OS));
    }

    internal static IEnumerable<string> SearchDirectories(string baseDirectory, string rid, OSPlatform platform)
    {
        yield return Path.Combine(baseDirectory, "runtimes", rid, "native");
        if (platform == OSPlatform.OSX)
            yield return Path.Combine(baseDirectory, "runtimes", "osx", "native");
        yield return baseDirectory;
        yield return Path.Combine(baseDirectory, "ffmpeg", rid);
        yield return Path.Combine(baseDirectory, "ffmpeg");
    }

    internal static string? FindDirectory(IEnumerable<string> candidates)
        => candidates.FirstOrDefault(directory => AvutilFileNames().Any(
            file => File.Exists(Path.Combine(directory, file))) || HasOtherAvutilVersion(directory));

    private static bool HasOtherAvutilVersion(string directory)
    {
        if (!Directory.Exists(directory)) return false;
        string pattern = OperatingSystem.IsWindows() ? "avutil-*.dll"
            : OperatingSystem.IsMacOS() ? "libavutil.*.dylib" : "libavutil.so.*";
        return Directory.EnumerateFiles(directory, pattern).Any();
    }

    private static IEnumerable<string> AvutilFileNames()
        => NativeAbi.Supported.Select(abi => FileName("avutil", abi.LibraryVersion("avutil")))
            .Append(NativePlatform.LibraryFileName("avutil", NativePlatform.OS));

    internal void LoadCore()
    {
        foreach (string library in new[] { "avutil", "swresample", "avcodec", "avformat", "swscale" })
            Load(library);
    }

    internal void LoadDevice()
    {
        Load("avfilter");
        Load("avdevice");
    }

    private static string FileName(string library, int version)
        => NativePlatform.LibraryFileName(library, version, NativePlatform.OS);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint VersionFunction();

    private nint Load(string library)
    {
        lock (_gate)
        {
            if (_handles.TryGetValue(library, out var handle)) return handle;
            if (library != "avutil") Load("avutil");
            var files = library == "avutil" ? AvutilFileNames()
                : new[] { FileName(library, _abi!.LibraryVersion(library)),
                    NativePlatform.LibraryFileName(library, NativePlatform.OS) };
            Exception? loadFailure = null;
            var attemptedPaths = new List<string>();
            foreach (string file in files)
            {
                string path = _directory is null ? file : Path.Combine(_directory, file);
                attemptedPaths.Add(path);
                try
                {
                    handle = _directory is not null && OperatingSystem.IsWindows()
                        ? NativeLibrary.Load(path, typeof(NativeFunctionResolver).Assembly,
                            DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.SafeDirectories)
                        : NativeLibrary.Load(path);
                }
                catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
                {
                    loadFailure = ex;
                    // A deployed library with missing dependencies must not select another ABI.
                    if (File.Exists(path) || ex is BadImageFormatException) break;
                    continue;
                }
                try
                {
                    int majorVersion = (int)(Marshal.GetDelegateForFunctionPointer<VersionFunction>(
                        NativeLibrary.GetExport(handle, library + "_version"))() >> 16);
                    var abi = _abi ?? NativeAbi.FromAvutilVersion(majorVersion);
                    abi.ValidateVersion(library, majorVersion, path);
                    _abi = abi;
                }
                catch
                {
                    NativeLibrary.Free(handle);
                    throw;
                }
                _handles.Add(library, handle);
                return handle;
            }
            throw new DllNotFoundException(
                $"Cannot load '{library}' for {NativePlatform.RuntimeIdentifier}. Tried: {string.Join(", ", attemptedPaths)}. " +
                (_abi is null ? "Supported FFmpeg versions: 8.x and 9.x. "
                    : $"Selected FFmpeg {_abi.FFmpegMajorVersion}.x; required {library} ABI {_abi.LibraryVersion(library)}. ") +
                "Deploy a complete matching set of shared libraries and their dependencies in an application native directory, " +
                "the system loader path, or set FFmpegNative.Configure(options => options.LibraryPath = ...). " +
                "FFmpeg.AutoGen and the ffmpeg executable alone do not supply compatible shared libraries.", loadFailure);
        }
    }

    public T GetFunctionDelegate<T>(string libraryName, string functionName, bool throwOnError = true)
    {
        var handle = Load(libraryName);
        if (NativeLibrary.TryGetExport(handle, functionName, out var pointer))
            return Marshal.GetDelegateForFunctionPointer<T>(pointer);
        if (throwOnError)
            throw new EntryPointNotFoundException(
                $"FFmpeg {_abi!.FFmpegMajorVersion}.x library '{FileName(libraryName, _abi.LibraryVersion(libraryName))}' does not export '{functionName}'.");
        return default!;
    }

    internal nint GetFunctionPointer(string libraryName, string functionName)
        => NativeLibrary.GetExport(Load(libraryName), functionName);
}
