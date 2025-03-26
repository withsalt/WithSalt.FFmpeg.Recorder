using System;
using System.Collections.Generic;
using System.Text;
using FFMpegCore.Arguments;

namespace WithSalt.FFmpeg.Recorder.Builder.StreamInputs
{
    internal abstract class BaseStreamInputArgumentsBuilder : BaseInputArgumentsBuilder
    {
        protected readonly Uri _uri;

        public BaseStreamInputArgumentsBuilder(Uri uri)
        {
            if (uri == null)
                throw new ArgumentNullException(nameof(uri), "The parameter uri cannot be empty.");
            _uri = uri;
        }

        protected void ResetProbeSize(List<IArgument> inputArgumentList, uint probeSize)
        {
            if (probeSize == 0)
                return;

            if (inputArgumentList.Count == 0)
                return;

            foreach (var item in this._latencyOptimizationContainer.Container)
            {
                for (int i = 0; i < item.Value.Count; i++)
                {
                    if (item.Value[i].Text.StartsWith("-probesize", StringComparison.OrdinalIgnoreCase))
                    {
                        item.Value[i] = new CustomArgument($"-probesize {probeSize}");
                        continue;
                    }
                }
            }
            return;
        }
    }
}
