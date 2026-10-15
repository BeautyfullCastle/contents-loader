using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace H.ContentsLoader
{
    internal interface IPackTransfer
    {
        // destination에 받던 파일이 있으면 그 뒤부터 이어 받음. 실패해도 받은 부분은 남김
        Task Fetch(string url, string destination, long expectedSize, Action<long> reportBytes);
    }

    internal static class PackTransfer
    {
        public static IPackTransfer CreateDefault()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (AndroidDownloadManagerTransfer.CanWriteTo(Application.persistentDataPath))
                return new AndroidDownloadManagerTransfer();
#endif
            return new UnityWebRequestTransfer();
        }
    }

    internal sealed class UnityWebRequestTransfer : IPackTransfer
    {
        public async Task Fetch(string url, string destination, long expectedSize, Action<long> reportBytes)
        {
            // 서버가 Range를 무시하면 처음부터 한 번 더 받음
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var existing = File.Exists(destination) ? new FileInfo(destination).Length : 0;
                if (existing > expectedSize)
                {
                    File.Delete(destination);
                    existing = 0;
                }
                if (existing == expectedSize) return;

                var resumed = existing > 0;
                var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET, new DownloadHandlerFile(destination, resumed), null);
                if (resumed) request.SetRequestHeader("Range", $"bytes={existing}-");
                long responseCode;
                try
                {
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        reportBytes(existing + (long)request.downloadedBytes);
                        await Awaitable.NextFrameAsync();
                    }
                    responseCode = request.responseCode;
                    if (request.result != UnityWebRequest.Result.Success && responseCode != 416)
                        throw new ContentDownloadException(url, request.error);
                }
                finally
                {
                    // 파일 핸들을 닫아야 다음 단계가 파일을 열 수 있음
                    request.Dispose();
                }

                if (resumed && responseCode != 206)
                {
                    File.Delete(destination);
                    continue;
                }
                if (new FileInfo(destination).Length == expectedSize) return;

                File.Delete(destination);
                throw new ContentDownloadException(url, $"받은 크기가 {expectedSize} 바이트와 다릅니다.");
            }
            throw new ContentDownloadException(url, "서버가 이어받기 요청에 맞게 응답하지 않습니다.");
        }
    }
}
