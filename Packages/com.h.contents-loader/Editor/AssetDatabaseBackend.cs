using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using Object = UnityEngine.Object;

namespace H.ContentsLoader.Editor
{
    // 빌드 없이 AssetDatabase에서 바로 읽음. Delivery와 무관하게 전부 Ready로 봄
    internal sealed class AssetDatabaseBackend : IContentBackend
    {
        sealed class Lease : IContentLease
        {
            public Lease(Object asset) => Asset = asset;
            public Object Asset { get; private set; }
            public void Release() => Asset = null;
        }

        static bool mapDirty = true;
        static readonly Dictionary<string, List<string>> AssetPaths = new(StringComparer.OrdinalIgnoreCase);

        public AssetDatabaseBackend(long memoryBudget)
        {
            MemoryBudget = memoryBudget;
        }

        public long MemoryBudget { get; }

        internal static void MarkDirty() => mapDirty = true;

        static Dictionary<string, List<string>> Map
        {
            get
            {
                if (!mapDirty) return AssetPaths;
                AssetPaths.Clear();
                foreach (var entry in ContentsScanner.ScanProject().Entries)
                {
                    if (!AssetPaths.TryGetValue(entry.Path, out var list)) AssetPaths.Add(entry.Path, list = new List<string>());
                    list.Add(entry.AssetPath);
                }
                mapDirty = false;
                return AssetPaths;
            }
        }

        public ContentStatus GetStatus(string path, Type type)
        {
            return Map.ContainsKey(ContentPath.Normalize(path)) ? ContentStatus.Ready : ContentStatus.NotFound;
        }

        public IContentLease Load(string path, Type type)
        {
            if (!Map.TryGetValue(ContentPath.Normalize(path), out var assetPaths))
                throw new ArgumentException($"'{path}' 경로의 콘텐츠가 없습니다.");
            foreach (var assetPath in assetPaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath(assetPath, type);
                if (asset != null) return new Lease(asset);
            }
            return new Lease(null);
        }

        public Task<IContentLease> LoadAsync(string path, Type type) => Task.FromResult(Load(path, type));

        public Task Download(string folder, IProgress<float> progress)
        {
            progress?.Report(1f);
            return Task.CompletedTask;
        }

        public long GetDownloadSize(string folder) => 0;

        public Task<bool> UpdateCatalog() => Task.FromResult(false);

        public void Dispose() { }
    }

    sealed class ContentsAssetWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            AssetDatabaseBackend.MarkDirty();
        }
    }
}
