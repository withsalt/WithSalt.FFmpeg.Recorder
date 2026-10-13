namespace WithSalt.FFmpeg.Recorder.Native;

// Only enable a family after checking the signatures and struct fields used by Recorder.
// FFmpeg 8/9 share those fields; later AVCodecContext fields (e.g. skip_frame) do not.
internal sealed class NativeAbi
{
    internal static IReadOnlyList<NativeAbi> Supported { get; } = new[]
    {
        new NativeAbi(9, 61, 7, 63, 63, 10, 12, 63),
        new NativeAbi(8, 60, 6, 62, 62, 9, 11, 62),
        new NativeAbi(7, 59, 5, 61, 61, 8, 10, 61)
    };

    private readonly IReadOnlyDictionary<string, int> _versions;
    internal int FFmpegMajorVersion { get; }

    private NativeAbi(int ffmpegMajorVersion, int avutil, int swresample, int avcodec,
        int avformat, int swscale, int avfilter, int avdevice)
    {
        FFmpegMajorVersion = ffmpegMajorVersion;
        _versions = new Dictionary<string, int>
        {
            ["avutil"] = avutil, ["swresample"] = swresample, ["avcodec"] = avcodec,
            ["avformat"] = avformat, ["swscale"] = swscale, ["avfilter"] = avfilter,
            ["avdevice"] = avdevice
        };
    }

    internal int LibraryVersion(string library) => _versions[library];

    internal void ValidateVersion(string library, int majorVersion, string path)
    {
        int expectedVersion = LibraryVersion(library);
        if (majorVersion != expectedVersion)
            throw new NotSupportedException(
                $"'{path}' reports ABI {majorVersion}; expected {expectedVersion} for FFmpeg {FFmpegMajorVersion}.x. " +
                "Deploy all FFmpeg libraries from the same ABI family.");
    }

    internal static NativeAbi FromAvutilVersion(int majorVersion)
        => Supported.FirstOrDefault(abi => abi.LibraryVersion("avutil") == majorVersion)
            ?? throw new NotSupportedException(
                $"FFmpeg avutil ABI {majorVersion} is not supported. Recorder supports FFmpeg 8.x and 9.x; " +
                "a different major version requires compatible bindings and struct layouts.");
}
