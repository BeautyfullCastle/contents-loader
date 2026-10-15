using System;

namespace H.ContentsLoader
{
    public sealed class ContentDownloadException : Exception
    {
        public string Url { get; }

        public ContentDownloadException(string url, string message) : base($"{message} ({url})")
        {
            Url = url;
        }
    }
}
