using WithSalt.FFmpeg.Recorder.Interface.StreamInputs;

namespace WithSalt.FFmpeg.Recorder.Builder.StreamInputs;

internal sealed class HttpInputArgumentsBuilder : BaseStreamInputArgumentsBuilder, IHttpInputArgumentsBuilder
{
    public HttpInputArgumentsBuilder(Uri uri) : base(uri) { }
    public IHttpInputArgumentsBuilder WithTimeout(uint timeout = 3) { SetTimeout(timeout); return this; }
    public IHttpInputArgumentsBuilder WithProbeSize(uint probeSize) { SetProbeSize(probeSize); return this; }
}
