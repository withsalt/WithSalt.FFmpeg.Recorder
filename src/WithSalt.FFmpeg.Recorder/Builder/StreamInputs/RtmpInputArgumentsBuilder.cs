using WithSalt.FFmpeg.Recorder.Interface.StreamInputs;
using WithSalt.FFmpeg.Recorder.Models;

namespace WithSalt.FFmpeg.Recorder.Builder.StreamInputs;

internal sealed class RtmpInputArgumentsBuilder : BaseStreamInputArgumentsBuilder, IRtmpInputArgumentsBuilder
{
    public RtmpInputArgumentsBuilder(Uri uri) : base(uri) { }
    public IRtmpInputArgumentsBuilder WithProbeSize(uint probeSize) { SetProbeSize(probeSize); return this; }
    public IRtmpInputArgumentsBuilder WithLiveType(RtmpLiveType liveType)
    {
        if (!Enum.IsDefined(liveType)) throw new ArgumentOutOfRangeException(nameof(liveType));
        Change(() => InputOptions["rtmp_live"] = liveType.ToString().ToLowerInvariant());
        return this;
    }
}
