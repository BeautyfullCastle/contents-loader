using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace H.ContentsLoader
{
    internal sealed class PackDownloader
    {
        readonly string cacheDirectory;
        readonly Func<string> remoteUrl;
        readonly IPackTransfer transfer;
        readonly Dictionary<string, Task> running = new();

        public PackDownloader(string cacheDirectory, Func<string> remoteUrl, IPackTransfer transfer)
        {
            this.cacheDirectory = cacheDirectory;
            this.remoteUrl = remoteUrl;
            this.transfer = transfer;
        }

        public int DownloadCount { get; private set; }

        public string CachedPathOf(string fileName)
        {
            var path = Path.Combine(cacheDirectory, fileName);
            return File.Exists(path) ? path : null;
        }

        // 앱이 꺼져 중단된 다운로드도 남아 있으니 남은 크기만 셈
        public long RemainingBytesOf(ContentCatalog.Pack pack)
        {
            var partial = new FileInfo(PartialPathOf(pack.FileName));
            return partial.Exists ? Math.Max(0, pack.Size - partial.Length) : pack.Size;
        }

        string PartialPathOf(string fileName) => Path.Combine(cacheDirectory, fileName + ".download");

        public async Task Download(IReadOnlyList<ContentCatalog.Pack> packs, IProgress<float> progress)
        {
            var total = packs.Sum(p => p.Size);
            long done = 0;
            foreach (var pack in packs)
            {
                await DownloadOne(pack, bytes => progress?.Report(total == 0 ? 1f : Mathf.Clamp01((done + bytes) / (float)total)));
                done += pack.Size;
            }
            progress?.Report(1f);
        }

        async Task DownloadOne(ContentCatalog.Pack pack, Action<long> reportBytes)
        {
            if (CachedPathOf(pack.FileName) != null) return;
            if (running.TryGetValue(pack.FileName, out var existing))
            {
                await existing;
                return;
            }

            var task = Fetch(pack, reportBytes);
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

        async Task Fetch(ContentCatalog.Pack pack, Action<long> reportBytes)
        {
            Directory.CreateDirectory(cacheDirectory);
            var url = $"{remoteUrl()}/{pack.FileName}";
            var target = Path.Combine(cacheDirectory, pack.FileName);
            var downloaded = PartialPathOf(pack.FileName);
            var recompressed = target + ".lz4";

            await transfer.Fetch(url, downloaded, pack.Size, reportBytes);
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
    }
}
