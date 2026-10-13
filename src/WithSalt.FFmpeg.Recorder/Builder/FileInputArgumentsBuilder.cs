using WithSalt.FFmpeg.Recorder.Interface;
using WithSalt.FFmpeg.Recorder.Native;

namespace WithSalt.FFmpeg.Recorder.Builder;

internal sealed class FileInputArgumentsBuilder : BaseInputArgumentsBuilder, IFileInputArgumentsBuilder
{
    private readonly List<string> _files = new();
    public IFileInputArgumentsBuilder WithFiles(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var paths = files.Select(Path.GetFullPath).ToArray();
        if (paths.Length == 0) throw new ArgumentException("Specify at least one input file.", nameof(files));
        foreach (var path in paths)
            if (!File.Exists(path)) throw new FileNotFoundException("Input video was not found.", path);
        Change(() => _files.AddRange(paths));
        return this;
    }
    public IFileInputArgumentsBuilder WithFiles(params string[] files) => WithFiles((IEnumerable<string>)files);
    protected override InputSpec[] CreateInputs()
    {
        if (_files.Count == 0) throw new ArgumentException("Specify input files with WithFiles.");
        if (!HasOutputSize) throw new ArgumentException("File inputs require WithOutputSize.");
        return _files.Select(path => new InputSpec(path, null, true, new Dictionary<string, string>())).ToArray();
    }
}
