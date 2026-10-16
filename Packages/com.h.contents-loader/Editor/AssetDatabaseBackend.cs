using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace H.ContentsLoader.Editor
{
    // 빌드 없이 AssetDatabase에서 바로 읽음. Delivery와 무관하게 전부 Ready로 봄
    internal sealed class AssetDatabaseBackend : IContentBackend
    {
        private sealed class Lease : IContentLease
        {
            private readonly AssetDatabaseBackend owner;
            private readonly int[] objects;

            public Lease(AssetDatabaseBackend owner, Object asset)
            {
                this.owner = owner;
                Asset = asset;
                if (asset == null)
                {
                    objects = Array.Empty<int>();
                    return;
                }
                objects = owner.Retain(asset, out var size);
                Size = size;
            }

            public Object Asset { get; private set; }
            public long Size { get; }

            public void Release()
            {
                if (Asset == null) return;
                Asset = null;
                owner.ReleaseObjects(objects);
            }
        }

        // 애셋이 끌고 오는 오브젝트(텍스처, 메시 등)를 인스턴스 ID로 세서 공유 오브젝트는 한 번만 셈
        private readonly Dictionary<int, (int Count, long Size)> objectRefs = new();
        private long loadedBytes;

        private static bool mapDirty = true;
        private static readonly Dictionary<string, List<string>> AssetPaths = new(StringComparer.OrdinalIgnoreCase);

        public AssetDatabaseBackend(long memoryBudget)
        {
            MemoryBudget = memoryBudget;
        }

        public long MemoryBudget { get; }
        public long LoadedBytes => loadedBytes;

        internal static void MarkDirty() => mapDirty = true;

        private static Dictionary<string, List<string>> Map
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
                if (asset != null) return new Lease(this, asset);
            }
            return new Lease(this, null);
        }

        public Task<IContentLease> LoadAsync(string path, Type type) => Task.FromResult(Load(path, type));

        public Task Download(string folder, IProgress<float> progress)
        {
            progress?.Report(1f);
            return Task.CompletedTask;
        }

        public long GetDownloadSize(string folder) => 0;

        public Task<bool> UpdateCatalog() => Task.FromResult(false);

        public void Dispose()
        {
            objectRefs.Clear();
            loadedBytes = 0;
        }

        private int[] Retain(Object asset, out long size)
        {
            size = 0;
            var ids = new List<int>();
            foreach (var dependency in EditorUtility.CollectDependencies(new[] { asset }))
            {
                if (dependency == null || dependency is MonoScript) continue;
                var id = dependency.GetInstanceID();
                if (objectRefs.TryGetValue(id, out var entry))
                {
                    objectRefs[id] = (entry.Count + 1, entry.Size);
                    size += entry.Size;
                }
                else
                {
                    var bytes = Profiler.GetRuntimeMemorySizeLong(dependency);
                    objectRefs.Add(id, (1, bytes));
                    loadedBytes += bytes;
                    size += bytes;
                }
                ids.Add(id);
            }
            return ids.ToArray();
        }

        private void ReleaseObjects(int[] ids)
        {
            foreach (var id in ids)
            {
                if (!objectRefs.TryGetValue(id, out var entry)) continue;
                if (entry.Count > 1)
                {
                    objectRefs[id] = (entry.Count - 1, entry.Size);
                    continue;
                }
                objectRefs.Remove(id);
                loadedBytes -= entry.Size;
            }
        }
    }

    internal sealed class ContentsAssetWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            AssetDatabaseBackend.MarkDirty();
        }
    }
}
