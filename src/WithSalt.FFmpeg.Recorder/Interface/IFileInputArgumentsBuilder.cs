﻿using System.Collections.Generic;

namespace WithSalt.FFmpeg.Recorder.Interface
{
    public interface IFileInputArgumentsBuilder : IFFmpegArgumentsBuilder
    {
        IFileInputArgumentsBuilder WithFiles(IEnumerable<string> files);

        IFileInputArgumentsBuilder WithFiles(params string[] files);
    }
}
