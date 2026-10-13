using System.Diagnostics;
using FFmpeg.AutoGen;

namespace WithSalt.FFmpeg.Recorder.Native;

/// <summary>Per blocking operation deadline. Never throws across the native callback boundary.</summary>
internal sealed unsafe class NativeInterrupt
{
    private readonly CancellationToken _token;
    private readonly uint _timeoutSeconds;
    private long _started;
    private volatile bool _timedOut;
    // Keep the delegate rooted until avformat_close_input has finished.
    private readonly AVIOInterruptCB_callback _callback;

    internal NativeInterrupt(CancellationToken token, uint timeoutSeconds)
    {
        _token = token;
        _timeoutSeconds = timeoutSeconds;
        _callback = _ => IsInterrupted() ? 1 : 0;
    }

    internal AVIOInterruptCB Callback => new() { callback = _callback };
    internal void Begin()
    {
        _token.ThrowIfCancellationRequested();
        _timedOut = false;
        if (_timeoutSeconds > 0) _started = Stopwatch.GetTimestamp();
    }

    private bool IsInterrupted()
    {
        if (_token.IsCancellationRequested) return true;
        if (_timeoutSeconds > 0 && Stopwatch.GetElapsedTime(_started).TotalSeconds >= _timeoutSeconds)
            _timedOut = true;
        return _timedOut;
    }

    internal void Check()
    {
        // Called after the native operation returns, never inside the native callback.
        // Cancellation must unwind decoding and release its resources before reaching the caller.
        _token.ThrowIfCancellationRequested();
        if (_timedOut || (_timeoutSeconds > 0 && Stopwatch.GetElapsedTime(_started).TotalSeconds >= _timeoutSeconds))
            throw new TimeoutException("FFmpeg input operation timed out.");
    }
}
