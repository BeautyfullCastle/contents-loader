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

        // 이 애셋 하나가 메모리에 올리는 바이트(추정). 다른 애셋과 공유하는 부분도 포함함
        long Size { get; }
        void Release();
    }

    internal interface IContentBackend : IDisposable
    {
        long MemoryBudget { get; }

        // 지금 메모리에 올라와 있는 바이트(추정). 여러 애셋이 공유하는 팩이나 오브젝트는 한 번만 셈
        long LoadedBytes { get; }

        // type이 null이면 경로에 걸린 애셋 전부를 봄
        ContentStatus GetStatus(string path, Type type);
        IContentLease Load(string path, Type type);
        Task<IContentLease> LoadAsync(string path, Type type);
        Task Download(string folder, IProgress<float> progress);
        long GetDownloadSize(string folder);
        Task<bool> UpdateCatalog();
    }
}
