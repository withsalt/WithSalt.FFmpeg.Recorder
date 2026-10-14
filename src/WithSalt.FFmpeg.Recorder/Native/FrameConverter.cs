using FFmpeg.AutoGen;
using SkiaSharp;
using WithSalt.FFmpeg.Recorder.Models;

namespace WithSalt.FFmpeg.Recorder.Native;

internal sealed unsafe class FrameConverter : IDisposable
{
    private SwsContext* _context;
    private readonly RecordingOptions _options;
    private readonly int _flags;
    private readonly delegate* unmanaged[Cdecl]<SwsContext*, byte**, int*, int, int, byte**, int*, int> _scale;
    private int _sourceWidth, _sourceHeight, _width, _height;
    private AVPixelFormat _sourceFormat;
    private int _colorspace = -1, _range = -1;
    private SKBitmap? _borrowed;
    private bool _borrowedIsView;
    private int _borrowedWidth, _borrowedHeight, _borrowedRowBytes;
    private nint _borrowedPixels;
    private PixelBufferPool? _pixelPool;
    internal SKBitmap LastBorrowed => _borrowed!;

    internal FrameConverter(RecordingOptions options)
    {
        _options = options;
        // AutoGen's sws_scale takes managed arrays; implicit AVFrame array conversions
        // allocate four arrays per frame. Use the same validated native library directly.
        _scale = (delegate* unmanaged[Cdecl]<SwsContext*, byte**, int*, int, int, byte**, int*, int>)
            FFmpegNative.ResolveFunctionPointer("swscale", "sws_scale");
        _flags = options.Quality switch
        {
            OutputQuality.High => (int)SwsFlags.SWS_LANCZOS,
            OutputQuality.Low => (int)SwsFlags.SWS_FAST_BILINEAR,
            _ => (int)SwsFlags.SWS_BILINEAR
        };
    }

    private SKImageInfo GetInfo(AVFrame* frame)
    {
        if (frame->width <= 0 || frame->height <= 0)
            throw new InvalidDataException("Decoded source dimensions are invalid.");
        int width = _options.Width ?? frame->width, height = _options.Height ?? frame->height;
        if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue / 4)
            throw new InvalidDataException("Decoded frame dimensions are invalid.");
        return new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
    }

    internal SKBitmap Convert(AVFrame* frame)
    {
        var info = GetInfo(frame);
        var bitmap = new SKBitmap();
        try
        {
            InstallPooledPixels(bitmap, info);
            ConvertInto(frame, bitmap, info);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    private void InstallPooledPixels(SKBitmap bitmap, SKImageInfo info)
    {
        var buffer = (_pixelPool ??= new PixelBufferPool()).Rent(checked(info.Width * info.Height * 4));
        try
        {
            if (!bitmap.InstallPixels(info, buffer.Pixels, info.RowBytes, buffer.ReleaseCallback))
                throw new InvalidOperationException("Cannot install output pixels.");
        }
        catch { buffer.Release(); throw; }
    }

    internal SKBitmap ConvertBorrowed(AVFrame* frame)
    {
        var info = GetInfo(frame);
        _borrowed ??= new SKBitmap();
        if (_borrowed.Handle == nint.Zero)
            throw new InvalidOperationException("The borrowed bitmap must not be disposed by its callback.");
        if (CanCopy(frame, info) && frame->linesize[0] > 0)
        {
            // No release callback: FFmpeg owns these pixels. The bitmap is only a view
            // during the synchronous callback, while the decoded AVFrame is still alive.
            if (!_borrowedIsView || _borrowedPixels != (nint)frame->data[0]
                || _borrowedWidth != info.Width || _borrowedHeight != info.Height
                || _borrowedRowBytes != frame->linesize[0])
            {
                if (!_borrowed.InstallPixels(info, (nint)frame->data[0], frame->linesize[0]))
                    throw new InvalidOperationException("Cannot create a view of the decoded BGRA frame.");
            }
            _borrowedIsView = true;
        }
        else
        {
            if (_borrowedIsView || _borrowedPixels == nint.Zero
                || _borrowedWidth != info.Width || _borrowedHeight != info.Height)
            {
                // Skia's tightly allocated pixels do not include swscale's SIMD tail padding.
                InstallPooledPixels(_borrowed, info);
            }
            _borrowedIsView = false;
            ConvertInto(frame, _borrowed, info);
        }
        _borrowedWidth = info.Width; _borrowedHeight = info.Height;
        _borrowedPixels = _borrowed.GetPixels(); _borrowedRowBytes = _borrowed.RowBytes;
        // Skia consumers may cache images by the pixel generation ID.
        _borrowed.NotifyPixelsChanged();
        return _borrowed;
    }

    private static bool CanCopy(AVFrame* frame, SKImageInfo info)
        => frame->format == (int)AVPixelFormat.AV_PIX_FMT_BGRA
            && frame->width == info.Width && frame->height == info.Height
            && frame->data[0] != null && Math.Abs((long)frame->linesize[0]) >= (long)info.Width * 4;

    private void ConvertInto(AVFrame* frame, SKBitmap bitmap, SKImageInfo info)
    {
        byte* pixels = (byte*)bitmap.GetPixels();
        if (pixels == null) throw new OutOfMemoryException("Cannot allocate output bitmap.");
        if (CanCopy(frame, info))
        {
            int rowBytes = checked(info.Width * 4);
            byte* source = frame->data[0];
            if (frame->linesize[0] == rowBytes && bitmap.RowBytes == rowBytes)
            {
                long bytes = (long)rowBytes * info.Height;
                Buffer.MemoryCopy(source, pixels, bytes, bytes);
            }
            else
            {
                for (int row = 0; row < info.Height; row++)
                {
                    Buffer.MemoryCopy(source, pixels, bitmap.RowBytes, rowBytes);
                    source += frame->linesize[0];
                    pixels += bitmap.RowBytes;
                }
            }
            return;
        }

        var originalFormat = (AVPixelFormat)frame->format;
        // YUVJ has the same plane layout as YUV; range is supplied separately below.
        var sourceFormat = originalFormat switch
        {
            AVPixelFormat.AV_PIX_FMT_YUVJ420P => AVPixelFormat.AV_PIX_FMT_YUV420P,
            AVPixelFormat.AV_PIX_FMT_YUVJ422P => AVPixelFormat.AV_PIX_FMT_YUV422P,
            AVPixelFormat.AV_PIX_FMT_YUVJ444P => AVPixelFormat.AV_PIX_FMT_YUV444P,
            AVPixelFormat.AV_PIX_FMT_YUVJ440P => AVPixelFormat.AV_PIX_FMT_YUV440P,
            _ => originalFormat
        };
        if (_context == null || _sourceWidth != frame->width || _sourceHeight != frame->height
            || _sourceFormat != sourceFormat || _width != info.Width || _height != info.Height)
        {
            _context = ffmpeg.sws_getCachedContext(_context, frame->width, frame->height, sourceFormat,
                info.Width, info.Height, AVPixelFormat.AV_PIX_FMT_BGRA, _flags, null, null, null);
            if (_context == null) throw new InvalidOperationException("Cannot create FFmpeg pixel converter.");
            _sourceWidth = frame->width; _sourceHeight = frame->height; _sourceFormat = sourceFormat;
            _width = info.Width; _height = info.Height;
            _colorspace = _range = -1;
        }
        int colorspace = frame->colorspace switch
        {
            AVColorSpace.AVCOL_SPC_BT709 => ffmpeg.SWS_CS_ITU709,
            AVColorSpace.AVCOL_SPC_BT2020_NCL or AVColorSpace.AVCOL_SPC_BT2020_CL => ffmpeg.SWS_CS_BT2020,
            AVColorSpace.AVCOL_SPC_FCC => ffmpeg.SWS_CS_FCC,
            AVColorSpace.AVCOL_SPC_SMPTE240M => ffmpeg.SWS_CS_SMPTE240M,
            _ => ffmpeg.SWS_CS_ITU601
        };
        int range = frame->color_range == AVColorRange.AVCOL_RANGE_JPEG
            || originalFormat is AVPixelFormat.AV_PIX_FMT_YUVJ420P or AVPixelFormat.AV_PIX_FMT_YUVJ422P
                or AVPixelFormat.AV_PIX_FMT_YUVJ444P or AVPixelFormat.AV_PIX_FMT_YUVJ440P ? 1 : 0;
        if (_colorspace != colorspace || _range != range)
        {
            int* coefficients = ffmpeg.sws_getCoefficients(colorspace);
            var table = new int_array4();
            for (uint i = 0; i < 4; i++) table[i] = coefficients[i];
            NativeError.Check(ffmpeg.sws_setColorspaceDetails(_context, table, range,
                table, 1, 0, 1 << 16, 1 << 16), "configure color conversion");
            _colorspace = colorspace; _range = range;
        }
        byte** data = stackalloc byte*[4] { pixels, null, null, null };
        int* lines = stackalloc int[4] { bitmap.RowBytes, 0, 0, 0 };
        int rows = NativeError.Check(_scale(_context, (byte**)&frame->data, (int*)&frame->linesize,
            0, frame->height, data, lines), "convert video frame");
        if (rows != info.Height) throw new InvalidDataException("FFmpeg returned an incomplete converted frame.");
    }

    public void Dispose()
    {
        _borrowed?.Dispose(); _borrowed = null;
        _pixelPool?.Dispose(); _pixelPool = null;
        if (_context != null) { ffmpeg.sws_freeContext(_context); _context = null; }
    }
}
