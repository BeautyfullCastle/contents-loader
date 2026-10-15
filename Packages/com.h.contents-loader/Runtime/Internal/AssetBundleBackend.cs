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

        public static AssetBundleBackendOptions ForPlayer() => new()
        {
            BuiltInRoot = Path.Combine(Application.streamingAssetsPath, FolderName),
            CacheRoot = Path.Combine(Application.persistentDataPath, FolderName),
        };
    }

    internal sealed class AssetBundleBackend : IContentBackend
    {
        sealed class LoadedPack
        {
            public AssetBundle Bundle;
            public AssetBundleCreateRequest Request;
            public Task<AssetBundle> Loading;
            public int RefCount;
        }

        sealed class Lease : IContentLease
        {
            readonly AssetBundleBackend owner;
            readonly string[] files;
            bool released;

            public Lease(AssetBundleBackend owner, string[] files, Object asset)
            {
                this.owner = owner;
                this.files = files;
                Asset = asset;
            }

            public Object Asset { get; private set; }

            public void Release()
            {
                if (released) return;
                released = true;
                Asset = null;
                foreach (var file in files) owner.ReleasePack(file, file == files[0]);
            }
        }

        readonly AssetBundleBackendOptions options;
        readonly CatalogSet catalogs;
        readonly HashSet<string> builtInFiles;
        readonly PackDownloader downloader;
        readonly Dictionary<string, LoadedPack> loaded = new();

        public AssetBundleBackend(AssetBundleBackendOptions options)
        {
            this.options = options;
            catalogs = new CatalogSet(options.BuiltInRoot, options.CacheRoot);
            builtInFiles = new HashSet<string>(catalogs.BuiltIn.Packs
                .Where(p => p.Delivery == ContentDelivery.BuiltIn)
                .Select(p => p.FileName));
            downloader = new PackDownloader(Path.Combine(options.CacheRoot, "packs"), () => RemoteUrl);
        }

        public bool SupportsPartialUnload => false;
        public long MemoryBudget => catalogs.Current.MemoryBudget;
        public int DownloadCount => downloader.DownloadCount;

        string RemoteUrl
        {
            get
            {
                var url = string.IsNullOrEmpty(options.RemoteUrl) ? catalogs.Current.RemoteUrl : options.RemoteUrl;
                if (string.IsNullOrEmpty(url))
                    throw new InvalidOperationException("Contents Loader의 RemoteUrl이 비어 있어서 다운로드할 수 없습니다.");
                return url.TrimEnd('/');
            }
        }

        public ContentStatus GetStatus(string path)
        {
            if (!catalogs.TryResolve(path, out _, out var packs)) return ContentStatus.NotFound;
            return packs.All(p => LocalPathOf(p) != null) ? ContentStatus.Ready : ContentStatus.NeedsDownload;
        }

        public IContentLease Load(string path, Type type)
        {
            var packs = Resolve(path, out var entry);
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
            var packs = Resolve(path, out var entry);
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

        public long GetDownloadSize(string folder) => Missing(catalogs.PacksOfFolder(folder)).Sum(p => p.Size);

        public async Task<bool> UpdateCatalog()
        {
            var url = $"{RemoteUrl}/{ContentCatalog.FileName}";
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest().AsTask();
            if (request.result != UnityWebRequest.Result.Success)
                throw new ContentDownloadException(url, request.error);

            return catalogs.ApplyRemote(request.downloadHandler.data);
        }

        public void Dispose()
        {
            foreach (var pack in loaded.Values)
            {
                var bundle = pack.Bundle != null ? pack.Bundle : pack.Request?.assetBundle;
                if (bundle != null) bundle.Unload(true);
            }
            loaded.Clear();
        }

        ContentCatalog.Pack[] Resolve(string path, out ContentCatalog.Entry entry)
        {
            if (catalogs.TryResolve(path, out entry, out var packs)) return packs;
            throw new ArgumentException($"'{path}' 경로의 콘텐츠가 없습니다.");
        }

        List<ContentCatalog.Pack> Missing(IEnumerable<ContentCatalog.Pack> packs)
        {
            return packs.Where(p => LocalPathOf(p) == null).GroupBy(p => p.FileName).Select(g => g.First()).ToList();
        }

        string LocalPathOf(ContentCatalog.Pack pack)
        {
            return builtInFiles.Contains(pack.FileName)
                ? Path.Combine(options.BuiltInRoot, pack.FileName)
                : downloader.CachedPathOf(pack.FileName);
        }

        void AcquireSync(ContentCatalog.Pack pack)
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
            loaded.Add(pack.FileName, new LoadedPack { Bundle = bundle, RefCount = 1 });
        }

        Task<AssetBundle> AcquireAsync(ContentCatalog.Pack pack)
        {
            if (loaded.TryGetValue(pack.FileName, out var entry))
            {
                entry.RefCount++;
                return entry.Bundle != null ? Task.FromResult(entry.Bundle) : entry.Loading;
            }

            var path = LocalPathOf(pack);
            if (path == null)
                return Task.FromException<AssetBundle>(new InvalidOperationException($"팩 {pack.FileName}이(가) 기기에 없습니다."));

            entry = new LoadedPack { RefCount = 1, Request = AssetBundle.LoadFromFileAsync(path) };
            loaded.Add(pack.FileName, entry);
            entry.Loading = CompleteLoad(pack.FileName, entry, path);
            return entry.Loading;
        }

        async Task<AssetBundle> CompleteLoad(string file, LoadedPack entry, string path)
        {
            await entry.Request.AsTask();
            if (entry.Bundle == null) entry.Bundle = entry.Request.assetBundle;
            if (entry.Bundle != null) return entry.Bundle;

            // 그 사이 같은 팩이 새로 로드됐을 수 있으니 자기 항목만 지움
            if (loaded.TryGetValue(file, out var current) && current == entry) loaded.Remove(file);
            throw new InvalidOperationException($"팩 {path}을(를) 열지 못했습니다.");
        }

        void ReleasePack(string file, bool holdsAsset)
        {
            if (!loaded.TryGetValue(file, out var entry)) return;
            if (--entry.RefCount > 0)
            {
                if (holdsAsset && Debug.isDebugBuild)
                    Debug.Log($"[Contents Loader] 팩 {file}의 다른 애셋이 아직 쓰이고 있어서 메모리 해제를 미룹니다.");
                return;
            }

            loaded.Remove(file);
            var bundle = entry.Bundle != null ? entry.Bundle : entry.Request?.assetBundle;
            if (bundle != null) bundle.Unload(true);
        }
    }
}
