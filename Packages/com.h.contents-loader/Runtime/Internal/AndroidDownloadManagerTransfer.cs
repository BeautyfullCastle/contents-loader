#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;

namespace H.ContentsLoader
{
    // OS DownloadManager에 맡김. 앱이 백그라운드로 가거나 꺼져도 OS가 계속 받음
    // 작업 ID를 파일로 남겨서 앱을 다시 켜면 같은 작업을 이어서 기다림
    internal sealed class AndroidDownloadManagerTransfer : IPackTransfer
    {
        private const int StatusSuccessful = 8;
        private const int StatusFailed = 16;
        private const int VisibilityVisible = 0;
        private const float PollSeconds = 0.25f;

        private readonly struct JobState
        {
            public readonly int Status;
            public readonly long Bytes;
            public readonly int Reason;

            public JobState(int status, long bytes, int reason)
            {
                Status = status;
                Bytes = bytes;
                Reason = reason;
            }

            public bool Exists => Status != 0;
        }

        private readonly UnityWebRequestTransfer fallback = new();
        private bool warnedCleartext;
        private bool orphansRemoved;

        // DownloadManager는 앱 전용 외부 저장소에만 쓸 수 있음
        public static bool CanWriteTo(string path) => path.Contains("/Android/data/");

        public async Task Fetch(string url, string destination, long expectedSize, Action<long> reportBytes)
        {
            // Unity의 HTTP 허용 설정은 매니페스트에 안 들어가서 OS 서비스는 HTTP를 거부함
            if (IsCleartextBlocked(url))
            {
                if (!warnedCleartext)
                    Debug.LogWarning($"[Contents Loader] 앱이 {new Uri(url).Host}의 HTTP 통신을 허용하지 않아서 백그라운드 다운로드 대신 앱 안에서 받습니다. HTTPS를 쓰세요.");
                warnedCleartext = true;
                await fallback.Fetch(url, destination, expectedSize, reportBytes);
                return;
            }

            var idFile = destination + ".id";
            using var manager = Manager();
            if (!orphansRemoved) RemoveOrphans(manager, Path.GetDirectoryName(destination));
            orphansRemoved = true;

            var id = SavedId(destination);
            var state = id >= 0 ? Query(manager, id) : default;
            if (!state.Exists || state.Status == StatusFailed)
            {
                if (id >= 0) Remove(manager, id);
                // DownloadManager는 기존 파일에 이어 쓰지 못함. 중단 후 재개는 DownloadManager가 직접 처리함
                if (File.Exists(destination)) File.Delete(destination);
                id = Enqueue(manager, url, destination);
                File.WriteAllText(idFile, id.ToString());
            }

            while (true)
            {
                state = Query(manager, id);
                if (state.Status == StatusSuccessful) break;
                if (!state.Exists || state.Status == StatusFailed)
                {
                    Remove(manager, id);
                    File.Delete(idFile);
                    throw new ContentDownloadException(url, $"DownloadManager가 실패했습니다 (reason {state.Reason}).");
                }
                reportBytes(state.Bytes);
                await Awaitable.WaitForSecondsAsync(PollSeconds);
            }

            // 작업 기록을 지우면 DownloadManager가 받은 파일도 지우므로 잠깐 옮겨 둠
            var keep = destination + ".keep";
            File.Move(destination, keep);
            Remove(manager, id);
            File.Move(keep, destination);
            File.Delete(idFile);

            if (new FileInfo(destination).Length == expectedSize) return;
            File.Delete(destination);
            throw new ContentDownloadException(url, $"받은 크기가 {expectedSize} 바이트와 다릅니다.");
        }

        // DownloadManager는 받을 파일을 전체 크기로 미리 잡아서 파일 길이로는 받은 양을 알 수 없음
        public long ReceivedBytes(string destination)
        {
            var id = SavedId(destination);
            if (id < 0) return fallback.ReceivedBytes(destination);
            using var manager = Manager();
            var state = Query(manager, id);
            return state.Exists && state.Status != StatusFailed ? state.Bytes : 0;
        }

        // 앱 데이터를 지우면 작업 ID 파일이 사라져도 OS 작업은 남아서 계속 재시도함
        private static void RemoveOrphans(AndroidJavaObject manager, string directory)
        {
            var known = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.id").Select(f => File.ReadAllText(f).Trim()).ToHashSet()
                : new HashSet<string>();

            var orphans = new List<long>();
            using (var query = new AndroidJavaObject("android.app.DownloadManager$Query"))
            using (var cursor = manager.Call<AndroidJavaObject>("query", query))
            {
                var idColumn = cursor.Call<int>("getColumnIndex", "_id");
                while (cursor.Call<bool>("moveToNext"))
                {
                    var id = cursor.Call<long>("getLong", idColumn);
                    if (!known.Contains(id.ToString())) orphans.Add(id);
                }
                cursor.Call("close");
            }
            if (orphans.Count > 0) manager.Call<int>("remove", orphans.ToArray());
        }

        private static long SavedId(string destination)
        {
            var idFile = destination + ".id";
            return File.Exists(idFile) && long.TryParse(File.ReadAllText(idFile), out var id) ? id : -1;
        }

        private static AndroidJavaObject Manager() => AndroidApplication.currentContext.Call<AndroidJavaObject>("getSystemService", "download");

        private static bool IsCleartextBlocked(string url)
        {
            var uri = new Uri(url);
            if (uri.Scheme != Uri.UriSchemeHttp) return false;
            using var policyClass = new AndroidJavaClass("android.security.NetworkSecurityPolicy");
            using var policy = policyClass.CallStatic<AndroidJavaObject>("getInstance");
            return !policy.Call<bool>("isCleartextTrafficPermitted", uri.Host);
        }

        private static long Enqueue(AndroidJavaObject manager, string url, string destination)
        {
            using var uriClass = new AndroidJavaClass("android.net.Uri");
            using var source = uriClass.CallStatic<AndroidJavaObject>("parse", url);
            using var file = new AndroidJavaObject("java.io.File", destination);
            using var target = uriClass.CallStatic<AndroidJavaObject>("fromFile", file);
            using var request = new AndroidJavaObject("android.app.DownloadManager$Request", source);
            request.Call<AndroidJavaObject>("setDestinationUri", target).Dispose();
            request.Call<AndroidJavaObject>("setTitle", Application.productName).Dispose();
            request.Call<AndroidJavaObject>("setNotificationVisibility", VisibilityVisible).Dispose();
            return manager.Call<long>("enqueue", request);
        }

        private static JobState Query(AndroidJavaObject manager, long id)
        {
            using var query = new AndroidJavaObject("android.app.DownloadManager$Query");
            query.Call<AndroidJavaObject>("setFilterById", new[] { id }).Dispose();
            using var cursor = manager.Call<AndroidJavaObject>("query", query);
            try
            {
                if (!cursor.Call<bool>("moveToFirst")) return default;
                return new JobState(
                    cursor.Call<int>("getInt", cursor.Call<int>("getColumnIndex", "status")),
                    cursor.Call<long>("getLong", cursor.Call<int>("getColumnIndex", "bytes_so_far")),
                    cursor.Call<int>("getInt", cursor.Call<int>("getColumnIndex", "reason")));
            }
            finally
            {
                cursor.Call("close");
            }
        }

        private static void Remove(AndroidJavaObject manager, long id) => manager.Call<int>("remove", new[] { id });
    }
}
#endif
