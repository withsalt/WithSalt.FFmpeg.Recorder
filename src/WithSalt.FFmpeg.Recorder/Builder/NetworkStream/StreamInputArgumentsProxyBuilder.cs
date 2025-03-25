using System;
using WithSalt.FFmpeg.Recorder.Builder;

namespace WithSalt.FFmpeg.Recorder.Interface
{
    internal class StreamInputArgumentsProxyBuilder : IStreamInputArgumentsProxyBuilder
    {
        #region Rtsp

        public IRtspInputArgumentsBuilder WithRtsp(string uri)
        {
            return WithRtsp(ParseUri(uri));
        }

        public IRtspInputArgumentsBuilder WithRtsp(Uri uri)
        {
            if (!uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(nameof(uri), "Invalid URI. Your input uri is not a rtsp stream.");
            }
            return new RtspInputArgumentsBuilder(uri);
        }

        #endregion

        #region Http

        public IHttpInputArgumentsBuilder WithHttp(string uri)
        {
            return WithHttp(ParseUri(uri));
        }

        public IHttpInputArgumentsBuilder WithHttp(Uri uri)
        {
            if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(nameof(uri), "Invalid URI. Your input uri is not a http stream.");
            }
            return new HttpInputArgumentsBuilder(uri);
        }

        #endregion

        #region Rtmp

        public IRtmpInputArgumentsBuilder WithRtmp(string uri)
        {
            return WithRtmp(ParseUri(uri));
        }

        public IRtmpInputArgumentsBuilder WithRtmp(Uri uri)
        {
            if (!uri.Scheme.Equals("rtmp", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(nameof(uri), "Invalid URI. Your input uri is not a rtmp stream.");
            }
            return new RtmpInputArgumentsBuilder(uri);
        }

        #endregion

        private Uri ParseUri(string uriStr)
        {
            if (string.IsNullOrWhiteSpace(uriStr))
            {
                throw new ArgumentNullException(nameof(uriStr), "Invalid URI.");
            }
            if (!Uri.TryCreate(uriStr, UriKind.Absolute, out Uri? uri))
            {
                throw new ArgumentException(nameof(uriStr), "Invalid URI.");
            }
            return uri;
        }
    }
}
