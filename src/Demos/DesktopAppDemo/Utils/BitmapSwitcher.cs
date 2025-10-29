using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace DesktopAppDemo.Utils
{
    sealed class BitmapSwitcher : IDisposable
    {
        private readonly WriteableBitmap?[] _tempImage = new WriteableBitmap?[2];
        private int _currentImageIndex = 0;
        private readonly object _sync = new();

        public WriteableBitmap? CurrentBitmap
        {
            get
            {
                lock (_sync)
                {
                    _currentImageIndex = _currentImageIndex == 1 ? 0 : 1;
                    return _tempImage[_currentImageIndex];
                }
            }
            set
            {
                lock (_sync)
                {
                    _tempImage[_currentImageIndex] = value;
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                for (int i = 0; i < _tempImage.Length; i++)
                {
                    if (_tempImage[i] != null)
                    {
                        _tempImage[i]?.Dispose();
                        _tempImage[i] = null;
                    }
                }
                _currentImageIndex = 0;
            }
        }
    }
}
