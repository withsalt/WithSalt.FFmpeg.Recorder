namespace WithSalt.FFmpeg.Recorder.Native;

internal static class NativeRecording
{
    internal static void Run(RecordingOptions options, CancellationToken token)
    {
        using var output = new FrameOutput(options, token);
        double offset = 0;
        for (int index = 0; index < options.Inputs.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            using var input = new NativeInput(options.Inputs[index], options.Latency, token, options.DecoderThreads);
            output.BeginInput(index, offset, input.Duration);
            double duration = input.Decode(output);
            output.EndInput(duration);
            offset += duration;
        }
        token.ThrowIfCancellationRequested();
        output.Report();
    }
}
