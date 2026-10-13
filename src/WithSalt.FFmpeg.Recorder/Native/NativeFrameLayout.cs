using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using SkiaSharp;

namespace WithSalt.FFmpeg.Recorder.Native;

// FFmpeg.AutoGen 9.0.1.1 declares AVFrame's size_t crop fields as ulong.
// On ARM32 those fields are 32-bit, so duration after them has a different offset.
// FFmpeg 8/9 (avutil 60/61) share this prefix through duration. Keep size_t pointer-sized.
// FFmpeg 8.0 has no alpha_mode field after duration; never access that trailing field.
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeFrameLayout
{
    internal byte_ptrArray8 data;
    internal int_array8 linesize;
    internal byte** extended_data;
    internal int width, height, nb_samples, format;
    internal AVPictureType pict_type;
    internal AVRational sample_aspect_ratio;
    internal long pts, pkt_dts;
    internal AVRational time_base;
    internal int quality;
    internal void* opaque;
    internal int repeat_pict, sample_rate;
    internal AVBufferRef_ptrArray8 buf;
    internal AVBufferRef** extended_buf;
    internal int nb_extended_buf;
    internal AVFrameSideData** side_data;
    internal int nb_side_data, flags;
    internal AVColorRange color_range;
    internal AVColorPrimaries color_primaries;
    internal AVColorTransferCharacteristic color_trc;
    internal AVColorSpace colorspace;
    internal AVChromaLocation chroma_location;
    internal long best_effort_timestamp;
    internal AVDictionary* metadata;
    internal int decode_error_flags;
    internal AVBufferRef* hw_frames_ctx;
    internal AVBufferRef* opaque_ref;
    internal nuint crop_top, crop_bottom, crop_left, crop_right;
    internal void* private_ref;
    internal AVChannelLayout ch_layout;
    internal long duration;

    internal static long Duration(AVFrame* frame) => ((NativeFrameLayout*)frame)->duration;

    internal static void Crop(AVFrame* frame, SKRect rectangle)
    {
        if (rectangle.Left < 0 || rectangle.Top < 0 || rectangle.Width <= 0 || rectangle.Height <= 0
            || (double)rectangle.Left + rectangle.Width > frame->width
            || (double)rectangle.Top + rectangle.Height > frame->height)
            throw new ArgumentOutOfRangeException(nameof(rectangle), "Capture rectangle must fit within the captured display's pixel dimensions.");
        var native = (NativeFrameLayout*)frame;
        native->crop_left = (nuint)rectangle.Left;
        native->crop_top = (nuint)rectangle.Top;
        native->crop_right = (nuint)(frame->width - rectangle.Left - rectangle.Width);
        native->crop_bottom = (nuint)(frame->height - rectangle.Top - rectangle.Height);
        NativeError.Check(ffmpeg.av_frame_apply_cropping(frame, (int)AvFrameCrop.AV_FRAME_CROP_UNALIGNED), "crop desktop frame");
    }
}
