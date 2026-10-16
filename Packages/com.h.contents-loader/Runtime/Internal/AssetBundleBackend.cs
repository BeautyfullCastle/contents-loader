using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace H.ContentsLoader
{
    internal sealed class AssetBundleBackendOptions
    {
        public const string FolderName = "ContentsLoader";

        public string BuiltInRoot;
        public string CacheRoot;
        public string RemoteUrl;

        // null이면 플랫폼 기본 전송을 씀
        public IPackTransfer Transfer;

        public static AssetBundleBackendOptions ForPlayer() => new()
        {
            BuiltInRoot = Path.Combine(Application.streamingAssetsPath, FolderName),
            CacheRoot = Path.Combine(Application.persistentDataPath, FolderName),
        };
    }

    internal enum PackLocation
    {
        BuiltIn,
        Downloaded,
        NotDownloaded,
    }

    // 모니터 창에 보여줄 팩 상태
    internal struct PackInfo
    {
        public string FileName;
        public ContentDelivery Delivery;
        public long Size;
        public PackLocation Location;
        public int RefCount;
        public long LoadedBytes;
        public bool Opening;
        public string[] Assets;
    }

    internal sealed class AssetBundleBackend : IContentBackend
    {
        private sealed class LoadedPack
        {
            public AssetBundle Bundle;
            public AssetBundleCreateRequest Request;
            public Task<AssetBundle> Loading;
            public int RefCount;
            public long Bytes;
        }

        private sealed class Lease : IContentLease
        {
            private readonly AssetBundleBackend owner;
            private readonly string[] files;
            private bool released;

            public Lease(AssetBundleBackend owner, string[] files, Object asset)
            {
                this.owner = owner;
                this.files = files;
                Asset = asset;
                Size = files.Sum(f => owner.loaded.TryGetValue(f, out var pack) ? pack.Bytes : 0);
            }

            public Object Asset { get; private set; }
            public long Size { get; }

            public void Release()
            {
                if (released) return;
                released = true;
                Asset = null;
                foreach (var file in files) owner.ReleasePack(file, file == files[0]);
            }
        }

        private readonly AssetBundleBackendOptions options;
        private readonly CatalogSet catalogs;
        private readonly HashSet<string> builtInFiles;
        private readonly PackDownloader downloader;
        private readonly Dictionary<string, LoadedPack> loaded = new();
        private long loadedBytes;

        public AssetBundleBackend(AssetBundleBackendOptions options)
        {
            this.options = options;
            catalogs = new CatalogSet(options.BuiltInRoot, options.CacheRoot);
            builtInFiles = new HashSet<string>(catalogs.BuiltIn.Packs
                .Where(p => p.Delivery == ContentDelivery.BuiltIn)
                .Select(p => p.FileName));
            downloader = new PackDownloader(Path.Combine(options.CacheRoot, "packs"), () => RemoteUrl, options.Transfer ?? PackTransfer.CreateDefault());
            RemoveUnusedCache();
        }

        // 내장 카탈로그와 원격 카탈로그 어느 쪽도 참조하지 않는 다운로드 팩을 지움
        private int RemoveUnusedCache()
        {
            // 카탈로그를 못 읽은 상태에서 정리하면 받은 팩을 전부 지우게 됨
            if (catalogs.Current.Packs.Length == 0) return 0;
            var keep = new HashSet<string>(catalogs.Current.Packs.Concat(catalogs.BuiltIn.Packs).Select(p => p.FileName));
            var removed = downloader.RemoveUnused(keep, loaded.Keys);
            if (removed > 0 && Debug.isDebugBuild)
                Debug.Log($"[Contents Loader] 더는 쓰지 않는 다운로드 파일 {removed}개를 지웠습니다.");
            return removed;
        }

        public long MemoryBudget => catalogs.Current.MemoryBudget;
        public long LoadedBytes => loadedBytes;
        public int DownloadCount => downloader.DownloadCount;

        private string RemoteUrl
        {
            get
            {
                var url = string.IsNullOrEmpty(options.RemoteUrl) ? catalogs.Current.RemoteUrl : options.RemoteUrl;
                if (string.IsNullOrEmpty(url))
                    throw new InvalidOperationException("Contents Loader의 RemoteUrl이 비어 있어서 다운로드할 수 없습니다.");
                return url.TrimEnd('/');
            }
        }

        public List<PackInfo> GetPackInfos()
        {
            var catalog = catalogs.Current;
            var assets = catalog.Entries.GroupBy(e => e.Pack).ToDictionary(g => g.Key, g => g.Select(e => e.AssetName).ToArray());
            var infos = new List<PackInfo>(catalog.Packs.Length);
            for (var i = 0; i < catalog.Packs.Length; i++)
            {
                var pack = catalog.Packs[i];
                loaded.TryGetValue(pack.FileName, out var open);
                infos.Add(new PackInfo
                {
                    FileName = pack.FileName,
                    Delivery = pack.Delivery,
                    Size = pack.Size,
                    Location = builtInFiles.Contains(pack.FileName) ? PackLocation.BuiltIn
                        : downloader.CachedPathOf(pack.FileName) != null ? PackLocation.Downloaded
                        : PackLocation.NotDownloaded,
                    RefCount = open?.RefCount ?? 0,
                    LoadedBytes = open?.Bytes ?? 0,
                    Opening = open != null && open.Bundle == null,
                    Assets = assets.TryGetValue(i, out var names) ? names : Array.Empty<string>(),
                });
            }
            return infos;
        }

        public ContentStatus GetStatus(string path, Type type)
        {
            var packs = type == null
                ? catalogs.PacksOfPath(path).ToList()
                : catalogs.TryResolve(path, type, out _, out var resolved) ? resolved.ToList() : new List<ContentCatalog.Pack>();
            if (packs.Count == 0) return ContentStatus.NotFound;
            return packs.All(p => LocalPathOf(p) != null) ? ContentStatus.Ready : ContentStatus.NeedsDownload;
        }

        public IContentLease Load(string path, Type type)
        {
            var packs = Resolve(path, type, out var entry);
            var acquired = new List<string>();
            try
            {
                foreach (var pack in packs)
                {
                    AcquireSync(pack);
                    acquired.Add(pack.FileName);
                }
                var asset = loaded[packs[0].FileName].Bundle.LoadAsset(entry.AssetName, type);
                return new Lease(this, acquired.ToArray(), asset);
            }
            catch
            {
                foreach (var file in acquired) ReleasePack(file, false);
                throw;
            }
        }

        public async Task<IContentLease> LoadAsync(string path, Type type)
        {
            var packs = Resolve(path, type, out var entry);
            await downloader.Download(Missing(packs), null);

            var acquiring = packs.Select(AcquireAsync).ToArray();
            try
            {
                await Task.WhenAll(acquiring);
            }
            catch
            {
                for (var i = 0; i < packs.Length; i++)
                    if (acquiring[i].Status == TaskStatus.RanToCompletion) ReleasePack(packs[i].FileName, false);
                throw;
            }

            var request = acquiring[0].Result.LoadAssetAsync(entry.AssetName, type);
            await request.AsTask();
            return new Lease(this, packs.Select(p => p.FileName).ToArray(), request.asset);
        }

        public Task Download(string folder, IProgress<float> progress)
        {
            return downloader.Download(Missing(catalogs.PacksOfFolder(folder)), progress);
        }

        public long GetDownloadSize(string folder) => Missing(catalogs.PacksOfFolder(folder)).Sum(downloader.RemainingBytesOf);

        public async Task<bool> UpdateCatalog()
        {
            var url = $"{RemoteUrl}/{ContentCatalog.FileName}";
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest().AsTask();
            if (request.result != UnityWebRequest.Result.Success)
                throw new ContentDownloadException(url, request.error);

            if (!catalogs.ApplyRemote(request.downloadHandler.data)) return false;
            RemoveUnusedCache();
            return true;
        }

        public void Dispose()
        {
            foreach (var pack in loaded.Values)
            {
                var bundle = pack.Bundle != null ? pack.Bundle : pack.Request?.assetBundle;
                if (bundle != null) bundle.Unload(true);
            }
            loaded.Clear();
            loadedBytes = 0;
        }

        private ContentCatalog.Pack[] Resolve(string path, Type type, out ContentCatalog.Entry entry)
        {
            if (catalogs.TryResolve(path, type, out entry, out var packs)) return packs;
            throw new ArgumentException($"'{path}' 경로의 콘텐츠가 없습니다.");
        }

        private List<ContentCatalog.Pack> Missing(IEnumerable<ContentCatalog.Pack> packs)
        {
            return packs.Where(p => LocalPathOf(p) == null).GroupBy(p => p.FileName).Select(g => g.First()).ToList();
        }

        private string LocalPathOf(ContentCatalog.Pack pack)
        {
            return builtInFiles.Contains(pack.FileName)
                ? Path.Combine(options.BuiltInRoot, pack.FileName)
                : downloader.CachedPathOf(pack.FileName);
        }

        private void AcquireSync(ContentCatalog.Pack pack)
        {
            if (loaded.TryGetValue(pack.FileName, out var entry))
            {
                // 비동기 로드 중이면 assetBundle 접근이 그 로드를 동기로 끝냄
                if (entry.Bundle == null) entry.Bundle = entry.Request.assetBundle;
                if (entry.Bundle == null) throw new InvalidOperationException($"팩 {pack.FileName}을(를) 열지 못했습니다.");
                entry.RefCount++;
                return;
            }

            var path = LocalPathOf(pack) ?? throw new InvalidOperationException($"팩 {pack.FileName}이(가) 기기에 없습니다.");
            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) throw new InvalidOperationException($"팩 {path}을(를) 열지 못했습니다.");
            loaded.Add(pack.FileName, new LoadedPack { Bundle = bundle, RefCount = 1, Bytes = Track(MemoryOf(pack, path)) });
        }

        private Task<AssetBundle> AcquireAsync(ContentCatalog.Pack pack)
        {
            if (loaded.TryGetValue(pack.FileName, out var entry))
            {
                entry.RefCount++;
                return entry.Bundle != null ? Task.FromResult(entry.Bundle) : entry.Loading;
            }

            var path = LocalPathOf(pack);
            if (path == null)
                return Task.FromException<AssetBundle>(new InvalidOperationException($"팩 {pack.FileName}이(가) 기기에 없습니다."));

            entry = new LoadedPack { RefCount = 1, Request = AssetBundle.LoadFromFileAsync(path), Bytes = Track(MemoryOf(pack, path)) };
            loaded.Add(pack.FileName, entry);
            entry.Loading = CompleteLoad(pack.FileName, entry, path);
            return entry.Loading;
        }

        private async Task<AssetBundle> CompleteLoad(string file, LoadedPack entry, string path)
        {
            await entry.Request.AsTask();
            if (entry.Bundle == null) entry.Bundle = entry.Request.assetBundle;
            if (entry.Bundle != null) return entry.Bundle;

            // 그 사이 같은 팩이 새로 로드됐을 수 있으니 자기 항목만 지움
            if (loaded.TryGetValue(file, out var current) && current == entry)
            {
                loaded.Remove(file);
                loadedBytes -= entry.Bytes;
            }
            throw new InvalidOperationException($"팩 {path}을(를) 열지 못했습니다.");
        }

        private void ReleasePack(string file, bool holdsAsset)
        {
            if (!loaded.TryGetValue(file, out var entry)) return;
            if (--entry.RefCount > 0)
            {
                if (holdsAsset && Debug.isDebugBuild)
                    Debug.Log($"[Contents Loader] 팩 {file}의 다른 애셋이 아직 쓰이고 있어서 메모리 해제를 미룹니다.");
                return;
            }

            loaded.Remove(file);
            loadedBytes -= entry.Bytes;
            var bundle = entry.Bundle != null ? entry.Bundle : entry.Request?.assetBundle;
            if (bundle != null) bundle.Unload(true);
        }

        private long Track(long bytes)
        {
            loadedBytes += bytes;
            return bytes;
        }

        // 기기의 팩 파일은 LZ4라 압축을 푼 오브젝트 크기에 가까움. 카탈로그 크기는 다운로드용 LZMA 크기라 작게 나옴
        // APK 안 파일처럼 크기를 못 읽으면 카탈로그 크기를 씀
        private static long MemoryOf(ContentCatalog.Pack pack, string path)
        {
            try
            {
                if (!path.Contains("://") && File.Exists(path)) return new FileInfo(path).Length;
            }
            catch (IOException)
            {
            }
            return pack.Size;
        }
    }
}
