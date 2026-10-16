using System.IO;
using UnityEditor;
using UnityEngine;

namespace H.ContentsLoader.Editor
{
    [InitializeOnLoad]
    internal static class EditorBackendInstaller
    {
        static EditorBackendInstaller()
        {
            ContentSystem.EditorBackendFactory = Create;
        }

        private static IContentBackend Create()
        {
            var settings = ContentsSettings.FindOrDefault();
            if (settings.PlayModeSource != PlayModeSource.Bundles) return new AssetDatabaseBackend(settings.MemoryBudgetBytes);

            var target = PlayModeBundleTarget();
            var source = settings.EditorDownloadSource;
            string remoteUrl;
            switch (source)
            {
                case EditorDownloadSource.LocalServer:
                    remoteUrl = EditorContentServer.Start(ContentsBuildPaths.Remote(target), settings.EditorDownloadSpeedKBps * 1024L).Url;
                    break;
                case EditorDownloadSource.RemoteUrl:
                    remoteUrl = (settings.RemoteUrl ?? "").Replace("{Platform}", target.ToString());
                    if (string.IsNullOrEmpty(remoteUrl))
                        Debug.LogWarning("[Contents Loader] EditorDownloadSource가 RemoteUrl인데 RemoteUrl이 비어 있습니다.");
                    break;
                default:
                    remoteUrl = ContentsBuildPaths.RemoteFileUrl(target);
                    break;
            }
            Debug.Log($"[Contents Loader] 플레이 모드: {target} 빌드, Download 팩은 {source} ({remoteUrl})");
            return CreateBundleBackend(target, remoteUrl, source.ToString());
        }

        // 에디터 플랫폼 빌드가 있으면 그걸 씀. 없으면 활성 타깃 빌드를 쓰되 화면이 깨질 수 있다고 알림
        private static BuildTarget PlayModeBundleTarget()
        {
            var editorTarget = ContentsBuildPaths.PlayModeTarget;
            if (ContentsBuildPaths.HasBuild(editorTarget)) return editorTarget;

            var active = EditorUserBuildSettings.activeBuildTarget;
            if (active != editorTarget)
                Debug.LogWarning($"[Contents Loader] {editorTarget}용 콘텐츠 빌드가 없어서 {active} 빌드를 읽습니다. 셰이더가 에디터에서 깨져 보일 수 있습니다. Tools > Contents Loader > Build Content for Play Mode를 실행하세요.");
            return active;
        }

        // 받는 곳마다 캐시를 따로 둠. 실제 서버에서 받은 팩과 로컬 빌드 팩이 섞이지 않게 함
        internal static AssetBundleBackend CreateBundleBackend(BuildTarget target, string remoteUrl = null, string cacheName = null)
        {
            var cache = ContentsBuildPaths.EditorCache(target);
            if (!string.IsNullOrEmpty(cacheName)) cache = $"{cache}/{cacheName}";
            return new AssetBundleBackend(new AssetBundleBackendOptions
            {
                BuiltInRoot = Path.GetFullPath(ContentsBuildPaths.BuiltIn(target)),
                CacheRoot = Path.GetFullPath(cache),
                RemoteUrl = remoteUrl ?? ContentsBuildPaths.RemoteFileUrl(target),
            });
        }

        [MenuItem("Tools/Contents Loader/Clear Play Mode Download Cache")]
        private static void ClearCache()
        {
            var root = Path.GetDirectoryName(ContentsBuildPaths.EditorCache(BuildTarget.NoTarget));
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Debug.Log("[Contents Loader] 플레이 모드 다운로드 캐시를 지웠습니다. 다음 플레이에서 Download 콘텐츠를 다시 받습니다.");
        }

        // 플레이 중에는 열린 팩 파일을 지울 수 없음
        [MenuItem("Tools/Contents Loader/Clear Play Mode Download Cache", true)]
        private static bool CanClearCache() => !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
