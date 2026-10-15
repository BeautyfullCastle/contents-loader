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
        string Name { get; }

        // false면 같은 저장 단위의 다른 애셋이 살아 있는 동안 메모리가 실제로 해제되지 않음
        bool SupportsPartialUnload { get; }

        long MemoryBudget { get; }

        ContentStatus GetStatus(string path);
        IContentLease Load(string path, Type type);
        Task<IContentLease> LoadAsync(string path, Type type);
        Task Download(string folder, IProgress<float> progress);
        long GetDownloadSize(string folder);
        Task<bool> UpdateCatalog();
    }
}
