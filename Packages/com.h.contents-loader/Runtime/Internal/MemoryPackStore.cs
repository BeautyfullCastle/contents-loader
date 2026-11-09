using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace H.ContentsLoader
{
    // 팩 파일을 메모리에 받아 두고 메모리에서 엶. WebGL용
    // WebGL에서는 StreamingAssets가 웹 주소라 파일로 못 읽고, 받은 파일을 이어받기·LZ4 재압축할 파일 시스템도 없음
    // 받은 바이트를 들고 있어서 팩을 닫았다가 다시 열 때 다시 받지 않음. 그만큼 메모리를 더 씀
    internal sealed class MemoryPackStore : IPackStore
    {
        // url, 받은 바이트 보고. 받은 전체 바이트를 돌려줌
        public delegate Task<byte[]> FetchBytes(string url, Action<long> reportBytes);

        private readonly string builtInRoot;
        private readonly ICollection<string> builtInFiles;
        private readonly Func<string> remoteUrl;
        private readonly Func<RemoteCompression> compression;
        private readonly FetchBytes fetch;
        private readonly Dictionary<string, byte[]> packs = new();
        private readonly Dictionary<string, Task> running = new();

        public MemoryPackStore(string builtInRoot, ICollection<string> builtInFiles, Func<string> remoteUrl, Func<RemoteCompression> compression, FetchBytes fetch)
        {
            this.builtInRoot = builtInRoot.TrimEnd('/', '\\');
            this.builtInFiles = builtInFiles;
            this.remoteUrl = remoteUrl;
            this.compression = compression;
            this.fetch = fetch;
        }

        public int DownloadCount { get; private set; }

        public bool Has(ContentCatalog.Pack pack) => packs.ContainsKey(pack.FileName);

        // 내장 팩도 받기 전에는 쓸 수 없지만 배포 위치로 보면 앱에 들어 있음
        public PackLocation LocationOf(ContentCatalog.Pack pack)
        {
            if (builtInFiles.Contains(pack.FileName)) return PackLocation.BuiltIn;
            return Has(pack) ? PackLocation.Downloaded : PackLocation.NotDownloaded;
        }

        public long RemainingBytesOf(ContentCatalog.Pack pack) => Has(pack) ? 0 : pack.Size;

        public async Task Fetch(IReadOnlyList<ContentCatalog.Pack> list, IProgress<float> progress)
        {
            var total = list.Sum(p => p.Size);
            long done = 0;
            foreach (var pack in list)
            {
                await FetchOne(pack, bytes => progress?.Report(total == 0 ? 1f : Mathf.Clamp01((done + bytes) / (float)total)));
                done += pack.Size;
            }
            progress?.Report(1f);
        }

        public AssetBundle Open(ContentCatalog.Pack pack) => AssetBundle.LoadFromMemory(Require(pack));

        public AssetBundleCreateRequest OpenAsync(ContentCatalog.Pack pack) => AssetBundle.LoadFromMemoryAsync(Require(pack));

        public long MemoryOf(ContentCatalog.Pack pack) => packs.TryGetValue(pack.FileName, out var bytes) ? bytes.Length : pack.Size;

        // 메모리에 든 팩은 앱을 끄면 사라지므로 카탈로그가 바뀔 때만 버림
        public int RemoveUnused(ICollection<string> keepFileNames, ICollection<string> inUse)
        {
            var unused = packs.Keys.Where(f => !keepFileNames.Contains(f) && !inUse.Contains(f) && !running.ContainsKey(f)).ToList();
            foreach (var file in unused) packs.Remove(file);
            return unused.Count;
        }

        private async Task FetchOne(ContentCatalog.Pack pack, Action<long> reportBytes)
        {
            if (Has(pack)) return;
            if (running.TryGetValue(pack.FileName, out var existing))
            {
                await existing;
                return;
            }

            var task = Receive(pack, reportBytes);
            running.Add(pack.FileName, task);
            try
            {
                await task;
            }
            finally
            {
                running.Remove(pack.FileName);
            }
        }

        private async Task Receive(ContentCatalog.Pack pack, Action<long> reportBytes)
        {
            byte[] bytes;
            if (builtInFiles.Contains(pack.FileName))
            {
                bytes = await fetch($"{builtInRoot}/{pack.FileName}", reportBytes);
            }
            else
            {
                var format = compression();
                var url = $"{remoteUrl()}/{ContentCatalog.RemoteFileName(pack.FileName, format)}";
                bytes = await fetch(url, reportBytes);
                if (format == RemoteCompression.GZip) bytes = Decompress(bytes, url);
                DownloadCount++;
            }
            reportBytes(pack.Size);
            packs[pack.FileName] = bytes;
        }

        // WebGL에는 스레드가 없어서 메인 스레드에서 바로 풂
        private static byte[] Decompress(byte[] gzip, string url)
        {
            try
            {
                using var input = new GZipStream(new MemoryStream(gzip), CompressionMode.Decompress);
                using var output = new MemoryStream(gzip.Length * 2);
                input.CopyTo(output);
                return output.ToArray();
            }
            catch (InvalidDataException e)
            {
                throw new ContentDownloadException(url, $"압축을 풀지 못했습니다: {e.Message}");
            }
        }

        private byte[] Require(ContentCatalog.Pack pack)
        {
            return packs.TryGetValue(pack.FileName, out var bytes)
                ? bytes
                : throw new InvalidOperationException($"팩 {pack.FileName}을(를) 아직 받지 않았습니다. 먼저 Content.Download나 Content.LoadAsync를 부르세요.");
        }

        // UnityWebRequest로 주소의 내용을 메모리에 받음. 실패하면 ContentDownloadException
        public static async Task<byte[]> WebFetch(string url, Action<long> reportBytes)
        {
            using var request = UnityWebRequest.Get(url);
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                reportBytes?.Invoke((long)request.downloadedBytes);
                await Awaitable.NextFrameAsync();
            }
            if (request.result != UnityWebRequest.Result.Success)
                throw new ContentDownloadException(url, request.error);
            return request.downloadHandler.data;
        }
    }
}
