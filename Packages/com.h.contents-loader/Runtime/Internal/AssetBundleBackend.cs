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

            public Lease(AssetBundleBackend owner, string[] files, Object asset)
            {
                this.owner = owner;
                this.files = files;
                Asset = asset;
            }

            public Object Asset { get; private set; }
            bool released;

            public void Release()
            {
                if (released) return;
                released = true;
                Asset = null;
                foreach (var file in files) owner.ReleasePack(file, file == files[0]);
            }
        }

        readonly AssetBundleBackendOptions options;
        readonly ContentCatalog builtIn;
        readonly HashSet<string> builtInFiles;
        ContentCatalog remote;
        readonly Dictionary<string, LoadedPack> loaded = new();
        readonly Dictionary<string, Task> downloads = new();

        public int DownloadCount { get; private set; }

        public AssetBundleBackend(AssetBundleBackendOptions options)
        {
            this.options = options;
            builtIn = ReadBuiltInCatalog(Path.Combine(options.BuiltInRoot, ContentCatalog.FileName));
            builtInFiles = new HashSet<string>(builtIn.Packs
                .Where(p => p.Delivery == ContentDelivery.BuiltIn)
                .Select(p => p.FileName));
            remote = ReadCachedRemoteCatalog();
        }

        public string Name => "AssetBundle";
        public bool SupportsPartialUnload => false;
        public long MemoryBudget => Current.MemoryBudget;

        ContentCatalog Current => remote ?? builtIn;

        IEnumerable<ContentCatalog> Layers
        {
            get
            {
                if (remote != null) yield return remote;
                yield return builtIn;
            }
        }

        string RemoteUrl
        {
            get
            {
                var url = string.IsNullOrEmpty(options.RemoteUrl) ? Current.RemoteUrl : options.RemoteUrl;
                if (string.IsNullOrEmpty(url))
                    throw new InvalidOperationException("Contents Loader의 RemoteUrl이 비어 있어서 다운로드할 수 없습니다.");
                return url.TrimEnd('/');
            }
        }

        string PackCacheDirectory => Path.Combine(options.CacheRoot, "packs");

        public ContentStatus GetStatus(string path)
        {
            if (!TryResolve(path, out var catalog, out var entry)) return ContentStatus.NotFound;
            return PacksOf(catalog, entry).All(p => LocalPathOf(p) != null) ? ContentStatus.Ready : ContentStatus.NeedsDownload;
        }

        public IContentLease Load(string path, Type type)
        {
            if (!TryResolve(path, out var catalog, out var entry))
                throw new ArgumentException($"'{path}' 경로의 콘텐츠가 없습니다.");

            var files = PacksOf(catalog, entry).Select(p => p.FileName).ToArray();
            var acquired = new List<string>();
            try
            {
                AssetBundle main = null;
                foreach (var pack in PacksOf(catalog, entry))
                {
                    var bundle = AcquireSync(pack);
                    acquired.Add(pack.FileName);
                    main ??= bundle;
                }
                return new Lease(this, files, main.LoadAsset(entry.AssetName, type));
            }
            catch
            {
                foreach (var file in acquired) ReleasePack(file, false);
                throw;
            }
        }

        public async Task<IContentLease> LoadAsync(string path, Type type)
        {
            if (!TryResolve(path, out var catalog, out var entry))
                throw new ArgumentException($"'{path}' 경로의 콘텐츠가 없습니다.");

            var packs = PacksOf(catalog, entry).ToArray();
            await DownloadPacks(packs, null);

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
            return DownloadPacks(PacksOfFolder(folder), progress);
        }

        public long GetDownloadSize(string folder)
        {
            return PacksOfFolder(folder).Where(p => LocalPathOf(p) == null).Sum(p => p.Size);
        }

        public async Task<bool> UpdateCatalog()
        {
            var url = $"{RemoteUrl}/{ContentCatalog.FileName}";
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest().AsTask();
            if (request.result != UnityWebRequest.Result.Success)
                throw new ContentDownloadException(url, request.error);

            var bytes = request.downloadHandler.data;
            var catalog = ContentCatalog.FromBytes(bytes);
            if (catalog.BuildTimestamp <= Current.BuildTimestamp) return false;

            Directory.CreateDirectory(options.CacheRoot);
            File.WriteAllBytes(Path.Combine(options.CacheRoot, ContentCatalog.FileName), bytes);
            remote = catalog;
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
        }

        bool TryResolve(string path, out ContentCatalog catalog, out ContentCatalog.Entry entry)
        {
            foreach (var layer in Layers)
            {
                if (!layer.TryFindEntry(path, out entry)) continue;
                catalog = layer;
                return true;
            }
            catalog = null;
            entry = default;
            return false;
        }

        // 첫 원소가 애셋이 들어 있는 팩임
        static IEnumerable<ContentCatalog.Pack> PacksOf(ContentCatalog catalog, ContentCatalog.Entry entry)
        {
            yield return catalog.Packs[entry.Pack];
            foreach (var dependency in catalog.Packs[entry.Pack].Dependencies)
                yield return catalog.Packs[dependency];
        }

        IEnumerable<ContentCatalog.Pack> PacksOfFolder(string folder)
        {
            folder = ContentPath.Normalize(folder);
            if (folder.Length == 0) return Current.Packs;

            foreach (var layer in Layers)
                if (layer.TryFindGroup(folder, out var group))
                    return group.Packs.Select(i => layer.Packs[i]);

            throw new ArgumentException($"Contents 폴더 '{folder}'가 없습니다.", nameof(folder));
        }

        string LocalPathOf(ContentCatalog.Pack pack)
        {
            if (builtInFiles.Contains(pack.FileName)) return Path.Combine(options.BuiltInRoot, pack.FileName);
            var cached = Path.Combine(PackCacheDirectory, pack.FileName);
            return File.Exists(cached) ? cached : null;
        }

        AssetBundle AcquireSync(ContentCatalog.Pack pack)
        {
            if (loaded.TryGetValue(pack.FileName, out var entry))
            {
                entry.RefCount++;
                // 비동기 로드 중이면 assetBundle 접근이 그 로드를 동기로 끝냄
                if (entry.Bundle == null) entry.Bundle = entry.Request.assetBundle;
                return entry.Bundle;
            }

            var path = LocalPathOf(pack) ?? throw new InvalidOperationException($"팩 {pack.FileName}이(가) 기기에 없습니다.");
            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) throw new InvalidOperationException($"팩 {path}을(를) 열지 못했습니다.");
            loaded.Add(pack.FileName, new LoadedPack { Bundle = bundle, RefCount = 1 });
            return bundle;
        }

        Task<AssetBundle> AcquireAsync(ContentCatalog.Pack pack)
        {
            if (loaded.TryGetValue(pack.FileName, out var entry))
            {
                entry.RefCount++;
                return entry.Bundle != null ? Task.FromResult(entry.Bundle) : entry.Loading;
            }

            var path = LocalPathOf(pack) ?? throw new InvalidOperationException($"팩 {pack.FileName}이(가) 기기에 없습니다.");
            entry = new LoadedPack { RefCount = 1, Request = AssetBundle.LoadFromFileAsync(path) };
            entry.Loading = CompleteLoad(pack.FileName, entry, path);
            loaded.Add(pack.FileName, entry);
            return entry.Loading;
        }

        async Task<AssetBundle> CompleteLoad(string file, LoadedPack entry, string path)
        {
            await entry.Request.AsTask();
            if (entry.Bundle == null) entry.Bundle = entry.Request.assetBundle;
            if (entry.Bundle != null) return entry.Bundle;

            loaded.Remove(file);
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

        async Task DownloadPacks(IEnumerable<ContentCatalog.Pack> packs, IProgress<float> progress)
        {
            var missing = packs.Where(p => LocalPathOf(p) == null).GroupBy(p => p.FileName).Select(g => g.First()).ToList();
            var total = missing.Sum(p => p.Size);
            long done = 0;
            foreach (var pack in missing)
            {
                await DownloadPack(pack, bytes => progress?.Report(total == 0 ? 1f : Mathf.Clamp01((done + bytes) / (float)total)));
                done += pack.Size;
            }
            progress?.Report(1f);
        }

        async Task DownloadPack(ContentCatalog.Pack pack, Action<long> reportBytes)
        {
            if (LocalPathOf(pack) != null) return;
            if (downloads.TryGetValue(pack.FileName, out var running))
            {
                await running;
                return;
            }

            var task = DownloadPackCore(pack, reportBytes);
            downloads.Add(pack.FileName, task);
            try
            {
                await task;
            }
            finally
            {
                downloads.Remove(pack.FileName);
            }
        }

        async Task DownloadPackCore(ContentCatalog.Pack pack, Action<long> reportBytes)
        {
            Directory.CreateDirectory(PackCacheDirectory);
            var url = $"{RemoteUrl}/{pack.FileName}";
            var target = Path.Combine(PackCacheDirectory, pack.FileName);
            var downloaded = target + ".download";
            var recompressed = target + ".lz4";

            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET,
                new DownloadHandlerFile(downloaded) { removeFileOnAbort = true }, null);
            try
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    reportBytes((long)request.downloadedBytes);
                    await Awaitable.NextFrameAsync();
                }
                if (request.result != UnityWebRequest.Result.Success)
                    throw new ContentDownloadException(url, request.error);
            }
            finally
            {
                // 파일 핸들을 닫아야 재압축이 파일을 열 수 있음
                request.Dispose();
            }
            reportBytes(pack.Size);

            // 전송은 LZMA로 작게, 저장은 LZ4로 바꿈. LZMA 번들은 열 때마다 전체 압축을 풀어야 함
            var recompress = AssetBundle.RecompressAssetBundleAsync(downloaded, recompressed, BuildCompression.LZ4Runtime);
            await recompress.AsTask();
            File.Delete(downloaded);
            if (!recompress.success)
            {
                File.Delete(recompressed);
                throw new ContentDownloadException(url, $"재압축에 실패했습니다: {recompress.humanReadableResult}");
            }

            if (File.Exists(target)) File.Delete(target);
            File.Move(recompressed, target);
            DownloadCount++;
        }

        ContentCatalog ReadCachedRemoteCatalog()
        {
            var path = Path.Combine(options.CacheRoot, ContentCatalog.FileName);
            if (!File.Exists(path)) return null;
            try
            {
                var catalog = ContentCatalog.FromBytes(File.ReadAllBytes(path));
                if (catalog.BuildTimestamp > builtIn.BuildTimestamp) return catalog;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException)
            {
                Debug.LogWarning($"[Contents Loader] 저장된 원격 카탈로그를 버립니다: {e.Message}");
            }
            // 앱 업데이트로 내장 카탈로그가 더 새로우면 예전 패치는 버림
            File.Delete(path);
            return null;
        }

        static ContentCatalog ReadBuiltInCatalog(string path)
        {
            byte[] bytes;
            if (path.Contains("://"))
            {
                // Android의 StreamingAssets는 APK 안에 있어서 File API로 못 읽음
                using var request = UnityWebRequest.Get(path);
                var operation = request.SendWebRequest();
                while (!operation.isDone) { }
                bytes = request.result == UnityWebRequest.Result.Success ? request.downloadHandler.data : null;
            }
            else
            {
                bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
            }

            if (bytes != null) return ContentCatalog.FromBytes(bytes);
            Debug.LogWarning($"[Contents Loader] 카탈로그가 없습니다: {path}. 콘텐츠를 빌드했는지 확인하세요.");
            return new ContentCatalog();
        }
    }
}
