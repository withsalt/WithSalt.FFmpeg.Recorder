using System.Runtime.InteropServices;
using SkiaSharp;

namespace WithSalt.FFmpeg.Recorder.Native;

// Keep at most one idle buffer. Outstanding bitmaps retain their own storage even
// after the session ends. Skia's release callback also accounts for shared SKImages.
internal sealed unsafe class PixelBufferPool : IDisposable
{
    private readonly object _gate = new();
    private PixelBuffer? _cached;
    private bool _disposed;

    internal PixelBuffer Rent(int length)
    {
        PixelBuffer? buffer;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            buffer = _cached;
            _cached = null;
        }
        if (buffer != null && buffer.Length != length) { buffer.Free(); buffer = null; }
        buffer ??= new PixelBuffer(this, length);
        buffer.BeginLease();
        return buffer;
    }

    private void Return(PixelBuffer buffer)
    {
        lock (_gate)
        {
            if (!_disposed && _cached == null) { _cached = buffer; return; }
        }
        buffer.Free();
    }

    public void Dispose()
    {
        PixelBuffer? buffer;
        lock (_gate)
        {
            _disposed = true;
            buffer = _cached;
            _cached = null;
        }
        buffer?.Free();
    }

    internal sealed class PixelBuffer
    {
        // swscale SIMD converters can write a complete vector past the final
        // packed row. Reserve it explicitly; rounding the payload alone is insufficient.
        internal const int TailPadding = 64;
        private readonly PixelBufferPool _pool;
        private int _leased;
        internal int Length { get; }
        internal nuint AllocationLength { get; }
        internal nint Pixels { get; private set; }
        internal SKBitmapReleaseDelegate ReleaseCallback { get; }

        internal PixelBuffer(PixelBufferPool pool, int length)
        {
            _pool = pool;
            Length = length;
            // Passing a reusable delegate without a context avoids SkiaSharp's
            // per-install closure allocation. The native GCHandle roots this lease.
            ReleaseCallback = (_, _) => Release();
            AllocationLength = ((nuint)length + TailPadding + 63) & ~(nuint)63;
            Pixels = (nint)NativeMemory.AlignedAlloc(AllocationLength, 64);
            if (Pixels == nint.Zero) throw new OutOfMemoryException("Cannot allocate output pixels.");
        }

        internal void BeginLease() => Volatile.Write(ref _leased, 1);
        internal void Release()
        {
            // InstallPixels failure and bitmap disposal may both try to release.
            if (Interlocked.Exchange(ref _leased, 0) == 1) _pool.Return(this);
        }
        internal void Free()
        {
            NativeMemory.AlignedFree((void*)Pixels);
            Pixels = nint.Zero;
        }
    }
}
