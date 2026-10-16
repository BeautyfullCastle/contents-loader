using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace H.ContentsLoader.Editor
{
    // 노드 키는 GUID임. 경로를 바꿔도 이전 배치를 재사용할 수 있게 함
    internal interface IDependencyCollector
    {
        IEnumerable<string> GetDirectDependencies(string guid);
        long GetSize(string guid);
        bool IsShader(string guid);
    }

    // 파일 단위 수집. 오브젝트 단위가 필요해지면 ContentBuildInterface 기반 구현으로 바꿈
    internal sealed class FileDependencyCollector : IDependencyCollector
    {
        private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".dll", ".asmdef", ".asmref", ".unity",
        };

        public IEnumerable<string> GetDirectDependencies(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return AssetDatabase.GetDependencies(path, false)
                .Where(d => d != path && IsBundleable(d))
                .Select(AssetDatabase.AssetPathToGUID);
        }

        public long GetSize(string guid)
        {
            var file = new FileInfo(AssetDatabase.GUIDToAssetPath(guid));
            return file.Exists ? file.Length : 0;
        }

        public bool IsShader(string guid)
        {
            return AssetDatabase.GetMainAssetTypeAtPath(AssetDatabase.GUIDToAssetPath(guid)) == typeof(Shader);
        }

        private static bool IsBundleable(string path)
        {
            return (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal))
                   && !Excluded.Contains(Path.GetExtension(path))
                   && !AssetDatabase.IsValidFolder(path);
        }
    }

    internal static class DependencyGraph
    {
        public static Dictionary<string, ClusterNode> Collect(IEnumerable<string> rootGuids, IDependencyCollector collector)
        {
            var nodes = new Dictionary<string, ClusterNode>();
            var pending = new Stack<string>(rootGuids);
            while (pending.Count > 0)
            {
                var guid = pending.Pop();
                if (nodes.ContainsKey(guid)) continue;

                var node = new ClusterNode { Key = guid, Size = collector.GetSize(guid), IsShader = collector.IsShader(guid) };
                node.Dependencies.AddRange(collector.GetDirectDependencies(guid));
                nodes.Add(guid, node);
                foreach (var dependency in node.Dependencies) pending.Push(dependency);
            }
            return nodes;
        }
    }
}
