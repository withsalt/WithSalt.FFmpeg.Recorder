using System;
using System.Collections.Generic;
using System.Text;
using FFMpegCore.Arguments;

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

        protected void ResetProbeSize(List<IArgument> inputArgumentList, uint probeSize)
        {
            if (probeSize == 0)
                return;

            if (inputArgumentList.Count == 0)
                return;

            for (int i = 0; i < inputArgumentList.Count; i++)
            {
                if (inputArgumentList[i].Text.StartsWith("-probesize", StringComparison.OrdinalIgnoreCase))
                {
                    inputArgumentList[i] = new CustomArgument($"-probesize {probeSize}");
                    return;
                }
            }
            return;
        }
    }
}
