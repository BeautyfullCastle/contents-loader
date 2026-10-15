using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace H.ContentsLoader.Editor
{
    internal sealed class ContentsEntry
    {
        public string Path;
        public string AssetPath;
        public string RootFolder;
    }

    internal sealed class ContentsScanResult
    {
        public readonly List<ContentsEntry> Entries = new();
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
    }

    internal static class ContentsScanner
    {
        public const string FolderName = "Contents";

        static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".asmdef", ".asmref", ".dll", ".meta",
        };

        // 가장 바깥 Contents 폴더 기준임. 중첩 Contents는 하위 폴더로 취급함
        public static bool TryGetContentPath(string assetPath, out string rootFolder, out string contentPath)
        {
            rootFolder = contentPath = null;
            var parts = ContentPath.Normalize(assetPath).Split('/');
            if (parts.Length < 3 || parts[0] != "Assets") return false;

            for (var i = 1; i < parts.Length - 1; i++)
            {
                if (parts[i] != FolderName) continue;
                rootFolder = string.Join("/", parts, 0, i + 1);
                var relative = string.Join("/", parts, i + 1, parts.Length - i - 1);
                var dot = relative.LastIndexOf('.');
                var slash = relative.LastIndexOf('/');
                contentPath = dot > slash ? relative.Substring(0, dot) : relative;
                return true;
            }
            return false;
        }

        public static bool TryGetContentsFolder(string folderPath, out string rootFolder)
        {
            // 폴더 자신도 판정하려고 가짜 파일 이름을 붙임
            return TryGetContentPath(ContentPath.Normalize(folderPath) + "/_", out rootFolder, out _);
        }

        public static ContentsScanResult Scan(IEnumerable<string> assetFilePaths)
        {
            var result = new ContentsScanResult();
            var byPath = new Dictionary<string, ContentsEntry>(StringComparer.OrdinalIgnoreCase);
            var byHash = new Dictionary<ulong, ContentsEntry>();

            foreach (var assetPath in assetFilePaths.OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!TryGetContentPath(assetPath, out var root, out var path)) continue;
                if (IgnoredExtensions.Contains(System.IO.Path.GetExtension(assetPath))) continue;
                if (assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add($"{assetPath}: 씬은 아직 Contents 대상이 아니라서 제외합니다.");
                    continue;
                }

                var entry = new ContentsEntry { Path = path, AssetPath = assetPath, RootFolder = root };
                if (byPath.TryGetValue(path, out var existing))
                {
                    result.Errors.Add($"Contents 경로 '{path}'가 겹칩니다: {existing.AssetPath}, {assetPath}");
                    continue;
                }
                if (byHash.TryGetValue(ContentPath.Hash(path), out var collided))
                {
                    result.Errors.Add($"Contents 경로 해시가 충돌합니다: '{collided.Path}', '{path}'. 둘 중 하나의 이름을 바꾸세요.");
                    continue;
                }
                byPath.Add(path, entry);
                byHash.Add(ContentPath.Hash(path), entry);
                result.Entries.Add(entry);
            }
            return result;
        }

        public static ContentsScanResult ScanProject()
        {
            return Scan(AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(p)));
        }
    }
}
