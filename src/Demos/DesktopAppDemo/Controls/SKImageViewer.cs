using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Reactive;
using SkiaSharp;

namespace DesktopAppDemo.Controls
{
    public class SKImageViewer : UserControl, IDisposable
    {

        public static readonly StyledProperty<SKBitmap> SourceProperty =
            AvaloniaProperty.Register<SKImageViewer, SKBitmap>(nameof(Source));

        static SKImageViewer()
        {
            AffectsRender<SKImageViewer>(SourceProperty);
        }

        public SKImageViewer()
        {
            ClipToBounds = true;

            SourceProperty.Changed.Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs<SKBitmap>>(
               e =>
               {
                   InvalidateMeasure();
                   InvalidateVisual();
               })
            );
        }

        private Size RenderSize => Bounds.Size;
        private WriteableBitmap? writableBitmap;
        private bool disposed = false;

        protected override Size MeasureOverride(Size constraint) =>
           InternalMeasureArrangeOverride(constraint);

        protected override Size ArrangeOverride(Size arrangeSize) =>
            InternalMeasureArrangeOverride(arrangeSize);

        private Size InternalMeasureArrangeOverride(Size targetSize)
        {
            if (Source != null && !disposed)
            {
                var self = new Size(Source.Width, Source.Height);
                var scaleFactor = ComputeScaleFactor(
                    targetSize,
                    self)
                  ;
                return new(
                   self.Width * scaleFactor.Width,
                   self.Height * scaleFactor.Height);
            }
            else
            {
                return default;
            }
        }


        public SKBitmap Source
        {
            get => GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public override void Render(DrawingContext drawingContext)
        {
            base.Render(drawingContext);
            if (Source == null || disposed) return;

            int width = Source.Width;
            int height = Source.Height;

            var info = new SKImageInfo(
                width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);

            // 检查是否需要重新创建WriteableBitmap
            if (writableBitmap == null || 
                writableBitmap.PixelSize.Width != width || 
                writableBitmap.PixelSize.Height != height)
            {
                writableBitmap?.Dispose();
                writableBitmap = new WriteableBitmap(
                    new(info.Width, info.Height), new(96.0, 96.0), PixelFormat.Bgra8888, AlphaFormat.Premul);
            }

            using var locker = writableBitmap.Lock();
            using var surface = SKSurface.Create(info, locker.Address, locker.RowBytes);
            if (surface != null)
            {
                surface.Canvas.Clear();
                surface.Canvas.DrawBitmap(Source, default(SKPoint));
            }
            
            drawingContext.DrawImage(writableBitmap, new(new(), RenderSize));
        }

        private Size ComputeScaleFactor(Size availableSize, Size contentSize)
        {
            // Compute scaling factors to use for axes
            double scaleX = 1.0;
            double scaleY = 1.0;

            // 防止除零错误
            if (contentSize.Width > 0 && contentSize.Height > 0)
            {
                // Compute scaling factors for both axes
                scaleX = availableSize.Width / contentSize.Width;
                scaleY = availableSize.Height / contentSize.Height;

                //Find maximum scale that we use for both axes
                double minscale = scaleX < scaleY ? scaleX : scaleY;
                scaleX = scaleY = minscale;
            }

            //Return this as a size now
            return new Size(scaleX, scaleY);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    writableBitmap?.Dispose();
                    writableBitmap = null;
                }
                disposed = true;
            }
        }

        ~SKImageViewer()
        {
            Dispose(false);
        }
    }
}
