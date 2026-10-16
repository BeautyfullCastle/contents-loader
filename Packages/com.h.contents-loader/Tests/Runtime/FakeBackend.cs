using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace H.ContentsLoader.Tests
{
    internal sealed class FakeBackend : IContentBackend
    {
        private sealed class Lease : IContentLease
        {
            private readonly FakeBackend owner;
            private readonly string path;

            public Lease(FakeBackend owner, string path, Object asset)
            {
                this.owner = owner;
                this.path = path;
                Asset = asset;
                Size = owner.SizePerAsset;
                owner.LoadedBytes += Size;
            }

            public Object Asset { get; private set; }
            public long Size { get; }

            public void Release()
            {
                if (Asset == null) return;
                owner.LoadedBytes -= Size;
                owner.Released.Add(path);
                Object.Destroy(Asset);
                Asset = null;
            }
        }

        public readonly HashSet<string> Ready = new();
        public readonly HashSet<string> Remote = new();
        public readonly List<string> Released = new();
        public TaskCompletionSource<bool> AsyncGate;
        public int Loads;

        public long MemoryBudget { get; set; } = long.MaxValue;
        public long LoadedBytes { get; private set; }
        public long SizePerAsset;

        public ContentStatus GetStatus(string path, Type type)
        {
            if (Ready.Contains(path)) return ContentStatus.Ready;
            return Remote.Contains(path) ? ContentStatus.NeedsDownload : ContentStatus.NotFound;
        }

        public IContentLease Load(string path, Type type)
        {
            Loads++;
            var asset = ScriptableObject.CreateInstance<FakeAsset>();
            asset.name = path;
            return new Lease(this, path, asset);
        }

        public async Task<IContentLease> LoadAsync(string path, Type type)
        {
            if (AsyncGate != null) await AsyncGate.Task;
            Ready.Add(path);
            return Load(path, type);
        }

        public Task Download(string folder, IProgress<float> progress) => Task.CompletedTask;
        public long GetDownloadSize(string folder) => 0;
        public Task<bool> UpdateCatalog() => Task.FromResult(false);
        public void Dispose() { }
    }

    internal sealed class FakeAsset : ScriptableObject { }
}
