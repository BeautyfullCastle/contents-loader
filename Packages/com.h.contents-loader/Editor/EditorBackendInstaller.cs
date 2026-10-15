using System.IO;
using UnityEditor;

namespace H.ContentsLoader.Editor
{
    [InitializeOnLoad]
    static class EditorBackendInstaller
    {
        static EditorBackendInstaller()
        {
            ContentSystem.EditorBackendFactory = Create;
        }

        static IContentBackend Create()
        {
            var settings = ContentsSettings.FindOrDefault();
            return settings.PlayModeSource == PlayModeSource.Bundles
                ? CreateBundleBackend(EditorUserBuildSettings.activeBuildTarget)
                : new AssetDatabaseBackend(settings.MemoryBudgetBytes);
        }

        // 원격 서버 대신 로컬 Remote 출력 폴더에서 받아서 다운로드 경로까지 그대로 재현함
        internal static AssetBundleBackend CreateBundleBackend(BuildTarget target)
        {
            return new AssetBundleBackend(new AssetBundleBackendOptions
            {
                BuiltInRoot = Path.GetFullPath(ContentsBuildPaths.BuiltIn(target)),
                CacheRoot = Path.GetFullPath(ContentsBuildPaths.EditorCache(target)),
                RemoteUrl = ContentsBuildPaths.RemoteFileUrl(target),
            });
        }
    }
}
