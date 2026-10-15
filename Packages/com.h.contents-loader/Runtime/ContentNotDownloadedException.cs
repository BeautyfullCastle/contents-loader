using System;

namespace H.ContentsLoader
{
    /// <summary>Thrown in development builds when a synchronous load targets Download content that is not on the device yet.</summary>
    public sealed class ContentNotDownloadedException : InvalidOperationException
    {
        public string Path { get; }

        public ContentNotDownloadedException(string path, string message) : base(message)
        {
            Path = path;
        }
    }
}
