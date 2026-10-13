# Third-party notices

WithSalt.FFmpeg.Recorder is licensed under MIT. Its native and managed dependencies retain their own licenses.

- FFmpeg.AutoGen 9.0.1.1: MIT, https://github.com/Ruslan-B/FFmpeg.AutoGen
- SkiaSharp: MIT, with additional native Skia notices supplied by its packages: https://github.com/mono/SkiaSharp

Recorder does not distribute FFmpeg native binaries. Application owners select and distribute the required FFmpeg build, such as FFmpeg.LGPL or FFmpeg.GPL, and are responsible for its notices and license obligations. FFmpeg is dynamically loaded and is not statically linked into this project. Linux and macOS shared-library licensing depends on the selected build and enabled dependencies.
