using System.Globalization;
using WithSalt.FFmpeg.Recorder.Native;

namespace WithSalt.FFmpeg.Recorder.Builder.StreamInputs;

internal abstract class BaseStreamInputArgumentsBuilder : BaseInputArgumentsBuilder
{
    private readonly Uri _uri;
    protected readonly Dictionary<string, string> InputOptions = new();
    private uint _timeout;
    protected BaseStreamInputArgumentsBuilder(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        _uri = uri;
    }
    protected void SetTimeout(uint timeout) => Change(() => _timeout = timeout);
    protected void SetProbeSize(uint probeSize)
    {
        if (probeSize != 0 && probeSize < 32) throw new ArgumentOutOfRangeException(nameof(probeSize), "FFmpeg requires at least 32 probe bytes.");
        Change(() =>
        {
            if (probeSize == 0) InputOptions.Remove("probesize");
            else InputOptions["probesize"] = probeSize.ToString(CultureInfo.InvariantCulture);
        });
    }
    protected override InputSpec[] CreateInputs()
        => [new InputSpec(_uri.AbsoluteUri, null, false, new Dictionary<string, string>(InputOptions), _timeout)];
}
