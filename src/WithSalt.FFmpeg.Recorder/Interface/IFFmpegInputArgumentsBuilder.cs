using WithSalt.FFmpeg.Recorder.Interface.StreamInputs;

namespace WithSalt.FFmpeg.Recorder.Interface
{
    /// <summary>
    /// Interface for building FFmpeg input arguments.
    /// </summary>
    public interface IFFmpegInputArgumentsBuilder
    {
        /// <summary>
        /// Configures the input source as a camera device.
        /// </summary>
        /// <returns>An instance of <see cref="ICameraInputArgumentsBuilder"/> for further configuration.</returns>
        ICameraInputArgumentsBuilder WithCameraInput();

        /// <summary>
        /// Configures the input source as a stream (e.g., RTSP, HTTP, RTMP).
        /// </summary>
        /// <returns>An instance of <see cref="IStreamInputArgumentsProxyBuilder"/> for further configuration.</returns>
        IStreamInputArgumentsProxyBuilder WithStreamInput();

        /// <summary>
        /// Configures the input source as a desktop screen capture.
        /// </summary>
        /// <returns>An instance of <see cref="IDesktopInputArgumentsBuilder"/> for further configuration.</returns>
        IDesktopInputArgumentsBuilder WithDesktopInput();

        /// <summary>
        /// Configures the input source as one or more files.
        /// </summary>
        /// <returns>An instance of <see cref="IFileInputArgumentsBuilder"/> for further configuration.</returns>
        IFileInputArgumentsBuilder WithFileInput();
    }
}
