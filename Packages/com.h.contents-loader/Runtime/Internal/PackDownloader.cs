using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace H.ContentsLoader
{
    internal sealed class PackDownloader
    {
        readonly string cacheDirectory;
        readonly Func<string> remoteUrl;
        readonly Dictionary<string, Task> running = new();

        public PackDownloader(string cacheDirectory, Func<string> remoteUrl)
        {
            this.cacheDirectory = cacheDirectory;
            this.remoteUrl = remoteUrl;
        }

        public int DownloadCount { get; private set; }

        public string CachedPathOf(string fileName)
        {
            var path = Path.Combine(cacheDirectory, fileName);
            return File.Exists(path) ? path : null;
        }

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
            var downloaded = target + ".download";
            var recompressed = target + ".lz4";

            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET, new DownloadHandlerFile(downloaded), null);
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
            catch
            {
                request.Dispose();
                File.Delete(downloaded);
                throw;
            }
            // 파일 핸들을 닫아야 재압축이 파일을 열 수 있음
            request.Dispose();
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
