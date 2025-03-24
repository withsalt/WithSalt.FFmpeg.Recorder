using System;
using System.Collections.Generic;
using System.Text;

namespace WithSalt.FFmpeg.Recorder.Builder
{
    internal abstract class BaseStreamInputArgumentsBuilder : BaseInputArgumentsBuilder
    {
        protected readonly Uri _uri;

        public BaseStreamInputArgumentsBuilder(Uri uri)
        {
            if (uri == null)
                throw new ArgumentNullException(nameof(uri), "The parameter uri cannot be empty.");
            this._uri = uri;
        }
    }
}
