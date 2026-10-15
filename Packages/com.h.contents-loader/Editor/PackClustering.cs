using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace H.ContentsLoader.Editor
{
    internal sealed class ClusterNode
    {
        public string Key;
        public long Size;
        public bool IsShader;
        public readonly List<string> Dependencies = new();
    }

    internal sealed class ClusterRoot
    {
        public string Key;
        public string Group;
        public ContentDelivery Delivery;
    }

    internal sealed class ClusterOptions
    {
        public long SmallPackThreshold = 64 * 1024;
        public bool SignatureByGroup;
    }

    [Serializable]
    internal sealed class PackPlan
    {
        public string Id;
        public ContentDelivery Delivery;
        public bool IsShader;
        public List<string> Members = new();

        [NonSerialized] public string Signature;
        [NonSerialized] public long Size;
        [NonSerialized] public bool Reused;
    }

    [Serializable]
    internal sealed class PackLayout
    {
        public List<PackPlan> Packs = new();
    }

    internal sealed class ClusterResult
    {
        public readonly PackLayout Layout = new();
        public readonly Dictionary<string, PackPlan> PackOf = new();
        public readonly Dictionary<string, HashSet<string>> RequiredPacks = new();
    }

    // 도달 시그니처 클러스터링: 같은 루트 집합에서만 도달하는 애셋끼리 한 팩으로 묶어서 중복 없이 필요한 팩만 로드하게 함
    internal static class PackClustering
    {
        public const string ShaderPackId = "shaders";

        sealed class Reach
        {
            public readonly SortedSet<string> Signature = new(StringComparer.Ordinal);
            public bool RequiresBuiltIn;
            public string SignatureKey => string.Join("|", Signature);
        }

        public static ClusterResult Run(IReadOnlyList<ClusterRoot> roots, IReadOnlyDictionary<string, ClusterNode> nodes,
            ClusterOptions options, PackLayout previous = null)
        {
            var reachOfRoot = roots.ToDictionary(r => r.Key, r => Traverse(r.Key, nodes));
            var reach = new Dictionary<string, Reach>();
            foreach (var root in roots)
            {
                foreach (var key in reachOfRoot[root.Key])
                {
                    if (!reach.TryGetValue(key, out var info)) reach.Add(key, info = new Reach());
                    info.Signature.Add(options.SignatureByGroup ? root.Group : root.Key);
                    info.RequiresBuiltIn |= root.Delivery == ContentDelivery.BuiltIn;
                }
            }

            var result = new ClusterResult();
            var placed = new Dictionary<string, PackPlan>();
            var packs = new List<PackPlan>();

            var shaders = reach.Keys.Where(k => nodes[k].IsShader).ToList();
            if (shaders.Count > 0)
            {
                var pack = new PackPlan { Id = ShaderPackId, Delivery = ContentDelivery.BuiltIn, IsShader = true };
                foreach (var key in shaders) Place(key, pack, placed, nodes);
                packs.Add(pack);
            }

            // 패치 안정성: 규칙을 어기지 않는 한 직전 배치를 그대로 둬서 팩 내용과 해시가 안 바뀌게 함
            foreach (var old in previous?.Packs ?? Enumerable.Empty<PackPlan>())
            {
                if (old.IsShader) continue;
                var pack = new PackPlan { Id = old.Id, Delivery = old.Delivery, Reused = true };
                foreach (var key in old.Members)
                {
                    if (placed.ContainsKey(key) || !reach.TryGetValue(key, out var info) || nodes[key].IsShader) continue;
                    if (info.RequiresBuiltIn != (old.Delivery == ContentDelivery.BuiltIn)) continue;
                    Place(key, pack, placed, nodes);
                }
                if (pack.Members.Count > 0) packs.Add(pack);
            }

            var usedIds = new HashSet<string>(packs.Select(p => p.Id));
            var fresh = new List<PackPlan>();
            foreach (var bucket in reach
                         .Where(kv => !placed.ContainsKey(kv.Key))
                         .GroupBy(kv => (kv.Value.SignatureKey, kv.Value.RequiresBuiltIn))
                         .OrderBy(g => g.Key.SignatureKey, StringComparer.Ordinal)
                         .ThenBy(g => g.Key.RequiresBuiltIn))
            {
                var delivery = bucket.Key.RequiresBuiltIn ? ContentDelivery.BuiltIn : ContentDelivery.Download;
                var pack = new PackPlan
                {
                    Id = UniqueId("p" + StableHash($"{delivery}:{bucket.Key.SignatureKey}"), usedIds),
                    Delivery = delivery,
                    Signature = bucket.Key.SignatureKey,
                };
                foreach (var kv in bucket) Place(kv.Key, pack, placed, nodes);
                fresh.Add(pack);
            }

            MergeSmallPacks(fresh, options.SmallPackThreshold, placed);
            packs.AddRange(fresh);

            foreach (var pack in packs.OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                pack.Members.Sort(StringComparer.Ordinal);
                result.Layout.Packs.Add(pack);
            }
            foreach (var kv in placed) result.PackOf.Add(kv.Key, kv.Value);
            foreach (var root in roots)
                result.RequiredPacks[root.Key] = new HashSet<string>(reachOfRoot[root.Key].Select(k => placed[k].Id));
            return result;
        }

        static HashSet<string> Traverse(string rootKey, IReadOnlyDictionary<string, ClusterNode> nodes)
        {
            var visited = new HashSet<string>();
            var stack = new Stack<string>();
            stack.Push(rootKey);
            while (stack.Count > 0)
            {
                var key = stack.Pop();
                if (!nodes.TryGetValue(key, out var node) || !visited.Add(key)) continue;
                foreach (var dependency in node.Dependencies) stack.Push(dependency);
            }
            return visited;
        }

        static void Place(string key, PackPlan pack, Dictionary<string, PackPlan> placed, IReadOnlyDictionary<string, ClusterNode> nodes)
        {
            pack.Members.Add(key);
            pack.Size += nodes[key].Size;
            placed[key] = pack;
        }

        // 시그니처가 상위집합인 팩은 이 팩을 쓰는 루트가 이미 전부 로드함. 그래서 흡수해도 그 루트들의 로드 팩 수는 안 늚
        static void MergeSmallPacks(List<PackPlan> packs, long threshold, Dictionary<string, PackPlan> placed)
        {
            if (threshold <= 0) return;
            var signatures = packs.ToDictionary(p => p, p => new HashSet<string>(p.Signature.Split('|'), StringComparer.Ordinal));

            foreach (var small in packs.OrderBy(p => p.Size).ThenBy(p => p.Id, StringComparer.Ordinal).ToList())
            {
                if (small.Size >= threshold) continue;
                var target = packs
                    .Where(p => p != small && p.Delivery == small.Delivery && signatures[p].IsProperSupersetOf(signatures[small]))
                    .OrderBy(p => p.Size)
                    .ThenBy(p => p.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (target == null) continue;

                target.Members.AddRange(small.Members);
                target.Size += small.Size;
                foreach (var key in small.Members) placed[key] = target;
                packs.Remove(small);
            }
        }

        static string UniqueId(string id, HashSet<string> used)
        {
            var candidate = id;
            for (var i = 2; !used.Add(candidate); i++) candidate = $"{id}-{i}";
            return candidate;
        }

        static string StableHash(string text)
        {
            var hash = 14695981039346656037UL;
            foreach (var b in Encoding.UTF8.GetBytes(text))
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }
    }
}
