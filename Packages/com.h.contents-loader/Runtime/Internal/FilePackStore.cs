using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace H.ContentsLoader
{
    // 내장 팩은 StreamingAssets에서, 받은 팩은 persistentDataPath에서 파일로 엶
    internal sealed class FilePackStore : IPackStore
    {
        private readonly string builtInRoot;
        private readonly ICollection<string> builtInFiles;
        private readonly PackDownloader downloader;

        public FilePackStore(string builtInRoot, ICollection<string> builtInFiles, PackDownloader downloader)
        {
            this.builtInRoot = builtInRoot;
            this.builtInFiles = builtInFiles;
            this.downloader = downloader;
        }

        public int DownloadCount => downloader.DownloadCount;

        public bool Has(ContentCatalog.Pack pack) => PathOf(pack) != null;

        public PackLocation LocationOf(ContentCatalog.Pack pack)
        {
            if (builtInFiles.Contains(pack.FileName)) return PackLocation.BuiltIn;
            return downloader.CachedPathOf(pack.FileName) != null ? PackLocation.Downloaded : PackLocation.NotDownloaded;
        }

        public long RemainingBytesOf(ContentCatalog.Pack pack) => downloader.RemainingBytesOf(pack);

        public Task Fetch(IReadOnlyList<ContentCatalog.Pack> packs, IProgress<float> progress) => downloader.Download(packs, progress);

        public AssetBundle Open(ContentCatalog.Pack pack) => AssetBundle.LoadFromFile(RequirePath(pack));

        public AssetBundleCreateRequest OpenAsync(ContentCatalog.Pack pack) => AssetBundle.LoadFromFileAsync(RequirePath(pack));

        // 기기의 팩 파일은 LZ4라 압축을 푼 오브젝트 크기에 가까움. 카탈로그 크기는 다운로드용 LZMA 크기라 작게 나옴
        // APK 안 파일처럼 크기를 못 읽으면 카탈로그 크기를 씀
        public long MemoryOf(ContentCatalog.Pack pack)
        {
            var path = PathOf(pack);
            try
            {
                if (path != null && !path.Contains("://") && File.Exists(path)) return new FileInfo(path).Length;
            }
            catch (IOException)
            {
            }
            return pack.Size;
        }

        public int RemoveUnused(ICollection<string> keepFileNames, ICollection<string> inUse) => downloader.RemoveUnused(keepFileNames, inUse);

        private string PathOf(ContentCatalog.Pack pack)
        {
            return builtInFiles.Contains(pack.FileName)
                ? Path.Combine(builtInRoot, pack.FileName)
                : downloader.CachedPathOf(pack.FileName);
        }

        private string RequirePath(ContentCatalog.Pack pack)
        {
            return PathOf(pack) ?? throw new InvalidOperationException($"팩 {pack.FileName}이(가) 기기에 없습니다.");
        }
    }
}
