using System;
using System.Threading.Tasks;
using Object = UnityEngine.Object;

namespace H.ContentsLoader
{
    internal enum ContentStatus
    {
        NotFound,
        Ready,
        NeedsDownload,
    }

    internal interface IContentLease
    {
        Object Asset { get; }
        void Release();
    }

    internal interface IContentBackend : IDisposable
    {
        long MemoryBudget { get; }

        // type이 null이면 경로에 걸린 애셋 전부를 봄
        ContentStatus GetStatus(string path, Type type);
        IContentLease Load(string path, Type type);
        Task<IContentLease> LoadAsync(string path, Type type);
        Task Download(string folder, IProgress<float> progress);
        long GetDownloadSize(string folder);
        Task<bool> UpdateCatalog();
    }
}
