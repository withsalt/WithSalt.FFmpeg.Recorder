using WithSalt.FFmpeg.Recorder.Interface.StreamInputs;

namespace WithSalt.FFmpeg.Recorder.Builder.StreamInputs;

internal sealed class RtspInputArgumentsBuilder : BaseStreamInputArgumentsBuilder, IRtspInputArgumentsBuilder
{
    public RtspInputArgumentsBuilder(Uri uri) : base(uri) => InputOptions["rtsp_transport"] = "tcp";
    public IRtspInputArgumentsBuilder WithTcp() { Change(() => InputOptions["rtsp_transport"] = "tcp"); return this; }
    public IRtspInputArgumentsBuilder WithUdp() { Change(() => InputOptions["rtsp_transport"] = "udp"); return this; }
    public IRtspInputArgumentsBuilder WithTimeout(uint timeout = 3) { SetTimeout(timeout); return this; }
    public IRtspInputArgumentsBuilder WithProbeSize(uint probeSize) { SetProbeSize(probeSize); return this; }
}
