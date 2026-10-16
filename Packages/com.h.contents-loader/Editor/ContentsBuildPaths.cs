using System.IO;
using UnityEditor;
using UnityEngine;

namespace H.ContentsLoader.Editor
{
    internal static class ContentsBuildPaths
    {
        public const string OutputRoot = "ContentsBuild";

        public static string Root(BuildTarget target) => $"{OutputRoot}/{target}";
        public static string BuiltIn(BuildTarget target) => $"{Root(target)}/BuiltIn";
        public static string Remote(BuildTarget target) => $"{Root(target)}/Remote";
        public static string Layout(BuildTarget target) => $"{Root(target)}/layout.json";
        public static string Report(BuildTarget target) => $"{Root(target)}/report.txt";
        public static string Work(BuildTarget target) => $"Library/ContentsLoader/Build/{target}";
        public static string EditorCache(BuildTarget target) => $"Library/ContentsLoader/Cache/{target}";

        public static string RemoteFileUrl(BuildTarget target) => "file:///" + Path.GetFullPath(Remote(target)).Replace('\\', '/').TrimStart('/');

        // 에디터 플레이 모드에서 열 수 있는 빌드 타깃. 모바일용 번들은 셰이더가 에디터 그래픽 API용으로 컴파일되지 않아 화면이 깨짐
        public static BuildTarget PlayModeTarget => Application.platform switch
        {
            RuntimePlatform.WindowsEditor => BuildTarget.StandaloneWindows64,
            RuntimePlatform.OSXEditor => BuildTarget.StandaloneOSX,
            _ => BuildTarget.StandaloneLinux64,
        };

        public static bool HasBuild(BuildTarget target) => File.Exists($"{BuiltIn(target)}/{ContentCatalog.FileName}");
    }
}
