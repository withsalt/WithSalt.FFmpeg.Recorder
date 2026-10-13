using System.Collections;
using WithSalt.FFmpeg.Recorder.Models;

namespace WithSalt.FFmpeg.Recorder.Native;

internal readonly record struct FrameEmission(double Time, bool Previous);

// Concrete foreach uses a value enumerator. IEnumerable is only for LINQ/test callers.
internal readonly struct FrameEmissions(double time, long start, long count, double rate,
    bool previous, bool lastIsCurrent) : IEnumerable<FrameEmission>
{
    internal static FrameEmissions Single(double time) => new(time, 0, 1, 0, false, false);
    public Enumerator GetEnumerator() => new(time, start, count, rate, previous, lastIsCurrent);
    IEnumerator<FrameEmission> IEnumerable<FrameEmission>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal struct Enumerator(double time, long start, long count, double rate,
        bool previous, bool lastIsCurrent) : IEnumerator<FrameEmission>
    {
        private long _index = -1;
        public FrameEmission Current => new(rate == 0 ? time : (start + _index) / rate,
            previous && !(lastIsCurrent && _index == count - 1));
        object IEnumerator.Current => Current;
        public bool MoveNext() => ++_index < count;
        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}

/// <summary>Media-time scheduling; file extraction is not paced against the wall clock.</summary>
internal sealed class FrameRateScheduler
{
    private const double Epsilon = 1e-7;
    private readonly FpsMode _mode;
    private readonly double? _rate;
    private long _next;
    private double _last = double.NegativeInfinity;
    private bool _hasPrevious;
    internal bool NeedsPrevious => _mode == FpsMode.CFR;

    internal FrameRateScheduler(FpsMode mode, double? rate)
    {
        _mode = mode == FpsMode.Auto ? (rate.HasValue ? FpsMode.CFR : FpsMode.VFR) : mode;
        _rate = rate;
    }

    internal FrameEmissions Push(double time)
    {
        if (!double.IsFinite(time) || time < 0 || time >= TimeSpan.MaxValue.TotalSeconds)
            throw new InvalidDataException("Invalid video timestamp.");
        if (_mode == FpsMode.Passthrough) return FrameEmissions.Single(time);
        if (time <= _last + Epsilon) return default;
        _last = time;
        if (_mode == FpsMode.CFR)
        {
            double rate = _rate!.Value;
            long start = _next;
            AdvanceBefore(time - Epsilon, rate);
            bool current = _next / rate <= time + Epsilon;
            if (current) _next = checked(_next + 1);
            var result = new FrameEmissions(0, start, _next - start, rate, _hasPrevious, current);
            _hasPrevious = true;
            return result;
        }
        if (_mode == FpsMode.Drop && _rate.HasValue)
        {
            if (time + Epsilon < _next / _rate.Value) return default;
            _next = checked((long)Math.Floor(time * _rate.Value + Epsilon) + 1);
        }
        return FrameEmissions.Single(time);
    }

    // Jump over long timestamp gaps without walking the range twice. Correct the
    // rounded product using the original division comparison at the boundaries.
    private void AdvanceBefore(double end, double rate)
    {
        long start = _next;
        _next = Math.Max(start, checked((long)Math.Ceiling(end * rate)));
        while (_next > start && (_next - 1) / rate >= end) _next--;
        while (_next / rate < end) _next = checked(_next + 1);
    }

    internal FrameEmissions FinishInput(double end)
    {
        if (_mode != FpsMode.CFR || !_hasPrevious) return default;
        long start = _next;
        AdvanceBefore(end - Epsilon, _rate!.Value);
        _hasPrevious = false;
        return new(0, start, _next - start, _rate.Value, true, false);
    }
}
