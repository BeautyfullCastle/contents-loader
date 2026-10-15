using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace H.ContentsLoader.Tests
{
    internal sealed class FakeBackend : IContentBackend
    {
        sealed class Lease : IContentLease
        {
            readonly FakeBackend owner;
            readonly string path;

            public Lease(FakeBackend owner, string path, Object asset)
            {
                this.owner = owner;
                this.path = path;
                Asset = asset;
            }

            public Object Asset { get; private set; }

            public void Release()
            {
                if (Asset == null) return;
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

        public string Name => "Fake";
        public bool SupportsPartialUnload => true;
        public long MemoryBudget { get; set; } = long.MaxValue;

        public ContentStatus GetStatus(string path)
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
