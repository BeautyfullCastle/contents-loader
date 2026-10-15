using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace H.ContentsLoader
{
    internal enum ContentState
    {
        Loading,
        Owned,
        Cached,
        Unloaded,
    }

    internal enum ContentEventKind
    {
        Load,
        CacheHit,
        Cached,
        Unload,
        Evict,
    }

    internal readonly struct ContentEvent
    {
        public readonly ContentEventKind Kind;
        public readonly string Path;
        public readonly int Frame;

        public ContentEvent(ContentEventKind kind, string path, int frame)
        {
            Kind = kind;
            Path = path;
            Frame = frame;
        }
    }

    internal readonly struct ContentOwner
    {
        public static readonly ContentOwner App = new(default, true);

        public readonly Scene Scene;
        public readonly bool IsApp;

        ContentOwner(Scene scene, bool isApp)
        {
            Scene = scene;
            IsApp = isApp;
        }

        public static ContentOwner Of(Scene scene) => new(scene, false);
    }

    internal sealed class ContentRecord
    {
        public string Path;
        public Type Type;
        public ContentState State;
        public readonly HashSet<Scene> Scenes = new();
        public bool App;
        public IContentLease Lease;
        public Task<Object> Loading;
        public int LastUsedFrame;
        public long Size;

        public Object Asset => Lease?.Asset;
        public bool HasOwners => App || Scenes.Count > 0;
    }

    // 상태: Loading -> Owned <-> Cached -> Unloaded
    internal sealed class ContentLifetime
    {
        readonly IContentBackend backend;
        readonly Dictionary<(string, Type), ContentRecord> records = new();
        int lastSceneEventFrame;
        bool sweepPending;

        public Func<int> Frame = () => Time.frameCount;
        public Func<Object, long> SizeOf = Profiler.GetRuntimeMemorySizeLong;
        public long MemoryBudget;
        public int BackendLoadCount { get; private set; }
        public event Action<ContentEvent> Changed;

        public ContentLifetime(IContentBackend backend)
        {
            this.backend = backend;
            MemoryBudget = backend.MemoryBudget;
        }

        public IContentBackend Backend => backend;
        public IReadOnlyCollection<ContentRecord> Records => records.Values;

        public ContentRecord Find(string path, Type type)
        {
            return records.TryGetValue((ContentPath.Normalize(path), type), out var record) ? record : null;
        }

        public Object Load(string path, Type type, ContentOwner owner)
        {
            path = ContentPath.Normalize(path);
            if (records.TryGetValue((path, type), out var record) && record.Lease != null)
            {
                Claim(record, owner);
                return record.Asset;
            }

            switch (backend.GetStatus(path))
            {
                case ContentStatus.NotFound:
                    Debug.LogWarning($"[Contents Loader] '{path}' 경로의 콘텐츠가 없습니다.");
                    return null;
                case ContentStatus.NeedsDownload:
                    return NotDownloaded(path);
            }

            // 비동기 로드가 진행 중이어도 동기 호출자는 기다릴 수 없으니 따로 로드하고 먼저 붙임
            record ??= Add(path, type);
            Claim(record, owner);
            IContentLease lease;
            try
            {
                BackendLoadCount++;
                lease = backend.Load(path, type);
            }
            catch
            {
                if (record.Loading == null) Remove(record);
                throw;
            }
            Attach(record, lease);
            return record.Asset;
        }

        public Task<Object> LoadAsync(string path, Type type, ContentOwner owner)
        {
            path = ContentPath.Normalize(path);
            if (records.TryGetValue((path, type), out var record))
            {
                Claim(record, owner);
                return record.Lease != null ? Task.FromResult(record.Asset) : record.Loading;
            }

            if (backend.GetStatus(path) == ContentStatus.NotFound)
            {
                Debug.LogWarning($"[Contents Loader] '{path}' 경로의 콘텐츠가 없습니다.");
                return Task.FromResult<Object>(null);
            }

            record = Add(path, type);
            Claim(record, owner);
            record.Loading = LoadRecordAsync(record);
            return record.Loading;
        }

        async Task<Object> LoadRecordAsync(ContentRecord record)
        {
            IContentLease lease;
            try
            {
                BackendLoadCount++;
                lease = await backend.LoadAsync(record.Path, record.Type);
            }
            catch
            {
                record.Loading = null;
                if (record.Lease == null) Remove(record);
                throw;
            }

            record.Loading = null;
            if (record.State == ContentState.Unloaded)
            {
                lease.Release();
                return null;
            }
            Attach(record, lease);
            return record.Asset;
        }

        public void OnSceneUnloaded(Scene scene)
        {
            MarkSceneEvent();
            foreach (var record in records.Values)
            {
                if (!record.Scenes.Remove(scene) || record.HasOwners || record.State != ContentState.Owned) continue;
                record.State = ContentState.Cached;
                Raise(ContentEventKind.Cached, record);
            }
        }

        public void OnSceneLoaded(Scene scene) => MarkSceneEvent();

        // 씬 전환 다음 프레임까지 기다려야 새 씬이 같은 애셋을 다시 가져갈 기회가 생김
        public void Tick()
        {
            if (sweepPending && Frame() > lastSceneEventFrame)
            {
                sweepPending = false;
                foreach (var record in records.Values.Where(r => r.State == ContentState.Cached).ToList())
                    Unload(record, ContentEventKind.Unload);
            }
            EnforceBudget();
        }

        public void UnloadAll()
        {
            foreach (var record in records.Values.ToList())
                Unload(record, ContentEventKind.Unload);
        }

        void MarkSceneEvent()
        {
            lastSceneEventFrame = Frame();
            sweepPending = true;
        }

        ContentRecord Add(string path, Type type)
        {
            var record = new ContentRecord { Path = path, Type = type, State = ContentState.Loading };
            records.Add((path, type), record);
            return record;
        }

        void Remove(ContentRecord record)
        {
            record.State = ContentState.Unloaded;
            records.Remove((record.Path, record.Type));
        }

        void Claim(ContentRecord record, ContentOwner owner)
        {
            if (owner.IsApp) record.App = true;
            else record.Scenes.Add(owner.Scene);
            record.LastUsedFrame = Frame();

            if (record.State != ContentState.Cached) return;
            record.State = ContentState.Owned;
            Raise(ContentEventKind.CacheHit, record);
        }

        void Attach(ContentRecord record, IContentLease lease)
        {
            if (record.Lease != null)
            {
                lease.Release();
                return;
            }
            if (lease.Asset == null)
            {
                lease.Release();
                Remove(record);
                Debug.LogWarning($"[Contents Loader] '{record.Path}'에서 {record.Type.Name} 타입 애셋을 찾지 못했습니다.");
                return;
            }

            record.Lease = lease;
            record.Size = SizeOf(lease.Asset);
            record.State = record.HasOwners ? ContentState.Owned : ContentState.Cached;
            if (record.State == ContentState.Cached) sweepPending = true;
            Raise(ContentEventKind.Load, record);
            EnforceBudget();
        }

        void Unload(ContentRecord record, ContentEventKind kind)
        {
            record.Lease?.Release();
            record.Lease = null;
            Remove(record);
            Raise(kind, record);
        }

        void EnforceBudget()
        {
            if (MemoryBudget <= 0 || MemoryBudget == long.MaxValue) return;

            var total = records.Values.Sum(r => r.Size);
            while (total > MemoryBudget)
            {
                var oldest = records.Values
                    .Where(r => r.State == ContentState.Cached)
                    .OrderBy(r => r.LastUsedFrame)
                    .FirstOrDefault();
                if (oldest == null) return;
                total -= oldest.Size;
                Unload(oldest, ContentEventKind.Evict);
            }
        }

        void Raise(ContentEventKind kind, ContentRecord record)
        {
            Changed?.Invoke(new ContentEvent(kind, record.Path, Frame()));
        }

        static Object NotDownloaded(string path)
        {
            var message = $"'{path}'은(는) Download 애셋입니다. LoadAsync를 쓰거나 Download()로 미리 받으세요.";
            if (Debug.isDebugBuild) throw new ContentNotDownloadedException(path, message);
            Debug.LogWarning($"[Contents Loader] {message}");
            return null;
        }
    }
}
