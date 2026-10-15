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
    internal sealed class ContentLifetime
    {
        readonly IContentBackend backend;
        readonly Dictionary<(string, Type), ContentRecord> records = new();
        int lastSceneEventFrame;
        bool sweepPending;
        long loadedSize;

        public Func<int> Frame = () => Time.frameCount;
        public Func<Object, long> SizeOf = Profiler.GetRuntimeMemorySizeLong;
        public long MemoryBudget;
        public int BackendLoadCount { get; private set; }

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
            }
            EnforceBudget();
        }

        public void OnSceneLoaded(Scene scene) => MarkSceneEvent();

        // 씬 전환 다음 프레임까지 기다려야 새 씬이 같은 애셋을 다시 가져갈 기회가 생김
        public void Tick()
        {
            if (!sweepPending || Frame() <= lastSceneEventFrame) return;
            sweepPending = false;
            foreach (var record in records.Values.Where(r => r.State == ContentState.Cached).ToList())
                Unload(record);
        }

        public void UnloadAll()
        {
            foreach (var record in records.Values.ToList())
                Unload(record);
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
            // 해제 후 같은 키로 새 레코드가 생겼을 수 있으니 자기 것만 지움
            var key = (record.Path, record.Type);
            if (records.TryGetValue(key, out var current) && current == record) records.Remove(key);
        }

        void Claim(ContentRecord record, ContentOwner owner)
        {
            if (owner.IsApp) record.App = true;
            else record.Scenes.Add(owner.Scene);
            record.LastUsedFrame = Frame();
            if (record.State == ContentState.Cached) record.State = ContentState.Owned;
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
            loadedSize += record.Size;
            record.State = record.HasOwners ? ContentState.Owned : ContentState.Cached;
            if (record.State == ContentState.Cached) sweepPending = true;
            EnforceBudget();
        }

        void Unload(ContentRecord record)
        {
            if (record.Lease != null)
            {
                record.Lease.Release();
                record.Lease = null;
                loadedSize -= record.Size;
            }
            Remove(record);
        }

        void EnforceBudget()
        {
            while (loadedSize > MemoryBudget)
            {
                var oldest = records.Values
                    .Where(r => r.State == ContentState.Cached)
                    .OrderBy(r => r.LastUsedFrame)
                    .FirstOrDefault();
                if (oldest == null) return;
                Unload(oldest);
            }
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
