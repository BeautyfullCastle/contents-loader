using System.IO;
using UnityEditor;

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
    }
}
