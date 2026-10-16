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

        // 이전 배치를 재사용한 팩에서 "팩을 여는 루트가 안 쓰는 바이트" 비율이 이걸 넘으면 그 팩을 다시 나눔
        public float MaxReuseWaste = 0.25f;
    }

    [Serializable]
    internal sealed class PackPlan
    {
        public string Id;
        public ContentDelivery Delivery;
        public bool IsShader;
        public List<string> Members = new();

        // 작은 팩 병합으로 들어온 멤버. 의도한 낭비라서 재사용할 때 드리프트로 세지 않음
        public List<string> Merged = new();

        [NonSerialized] public string Signature;
        [NonSerialized] public long Size;
        [NonSerialized] public bool Reused;

        // 이 팩을 여는 루트들이 각자 안 쓰는 멤버 바이트의 합. 팩 단위 로드와 해제 때문에 생기는 낭비임
        [NonSerialized] public long WasteBytes;
        [NonSerialized] public bool Repacked;
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

        private sealed class Reach
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
            // 단, 쓰임이 바뀐 애셋이 쌓여 낭비가 커지면 그 팩만 다시 나눔. 안 그러면 패치를 거듭할수록 팩이 섞임
            foreach (var old in previous?.Packs ?? Enumerable.Empty<PackPlan>())
            {
                if (old.IsShader) continue;
                var members = old.Members
                    .Where(key => !placed.ContainsKey(key) && reach.TryGetValue(key, out var info) && !nodes[key].IsShader
                                  && info.RequiresBuiltIn == (old.Delivery == ContentDelivery.BuiltIn))
                    .ToList();
                if (members.Count == 0) continue;

                var pack = new PackPlan { Id = old.Id, Delivery = old.Delivery, Reused = true };
                var merged = new HashSet<string>(old.Merged ?? new List<string>(), StringComparer.Ordinal);
                if (WasteRatio(members.Where(k => !merged.Contains(k)).ToList(), reach, nodes) > options.MaxReuseWaste)
                {
                    // 바이트가 가장 많은 쓰임새만 남기고 나머지는 아래에서 새 팩으로 감
                    var main = members.GroupBy(k => reach[k].SignatureKey)
                        .OrderByDescending(g => g.Sum(k => nodes[k].Size))
                        .ThenBy(g => g.Key, StringComparer.Ordinal)
                        .First().Key;
                    members = members.Where(k => reach[k].SignatureKey == main).ToList();
                    pack.Repacked = true;
                }
                foreach (var key in members) Place(key, pack, placed, nodes);
                pack.Merged.AddRange(members.Where(merged.Contains));
                packs.Add(pack);
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
                    // Delivery는 ID에 넣지 않음. Delivery만 바뀐 팩은 내용과 파일 이름이 그대로라 패치에서 다시 받지 않음
                    Id = UniqueId("p" + StableHash(bucket.Key.SignatureKey), usedIds),
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
                pack.Merged.Sort(StringComparer.Ordinal);
                pack.WasteBytes = WasteBytes(pack.Members, reach, nodes);
                result.Layout.Packs.Add(pack);
            }
            foreach (var kv in placed) result.PackOf.Add(kv.Key, kv.Value);
            foreach (var root in roots)
                result.RequiredPacks[root.Key] = new HashSet<string>(reachOfRoot[root.Key].Select(k => placed[k].Id));
            return result;
        }

        // 팩을 여는 루트마다, 그 루트가 도달하지 않는 멤버 바이트를 더함
        private static long WasteBytes(IReadOnlyCollection<string> members, Dictionary<string, Reach> reach, IReadOnlyDictionary<string, ClusterNode> nodes)
        {
            var users = new HashSet<string>(members.Where(reach.ContainsKey).SelectMany(k => reach[k].Signature), StringComparer.Ordinal);
            long waste = 0;
            foreach (var user in users)
            foreach (var key in members)
                if (reach.TryGetValue(key, out var info) && !info.Signature.Contains(user))
                    waste += nodes[key].Size;
            return waste;
        }

        private static double WasteRatio(IReadOnlyCollection<string> members, Dictionary<string, Reach> reach, IReadOnlyDictionary<string, ClusterNode> nodes)
        {
            var users = members.SelectMany(k => reach[k].Signature).Distinct(StringComparer.Ordinal).Count();
            var total = (double)users * members.Sum(k => nodes[k].Size);
            return total <= 0 ? 0 : WasteBytes(members, reach, nodes) / total;
        }

        private static HashSet<string> Traverse(string rootKey, IReadOnlyDictionary<string, ClusterNode> nodes)
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

        private static void Place(string key, PackPlan pack, Dictionary<string, PackPlan> placed, IReadOnlyDictionary<string, ClusterNode> nodes)
        {
            pack.Members.Add(key);
            pack.Size += nodes[key].Size;
            placed[key] = pack;
        }

        // 시그니처가 상위집합인 팩은 이 팩을 쓰는 루트가 이미 전부 로드함. 그래서 흡수해도 그 루트들의 로드 팩 수는 안 늚
        private static void MergeSmallPacks(List<PackPlan> packs, long threshold, Dictionary<string, PackPlan> placed)
        {
            if (threshold <= 0) return;
            var signatures = packs.ToDictionary(p => p, p => new HashSet<string>(p.Signature.Split('|'), StringComparer.Ordinal));

            foreach (var small in packs.OrderBy(p => p.Size).ThenBy(p => p.Id, StringComparer.Ordinal).ToList())
            {
                if (small.Size >= threshold) continue;
                // 루트가 가장 적은 상위집합으로 보냄. 이 팩을 여는데 small을 안 쓰는 루트 수가 최소라 낭비가 가장 작음
                var target = packs
                    .Where(p => p != small && p.Delivery == small.Delivery && signatures[p].IsProperSupersetOf(signatures[small]))
                    .OrderBy(p => signatures[p].Count)
                    .ThenBy(p => p.Size)
                    .ThenBy(p => p.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (target == null) continue;

                target.Members.AddRange(small.Members);
                target.Merged.AddRange(small.Members);
                target.Size += small.Size;
                foreach (var key in small.Members) placed[key] = target;
                packs.Remove(small);
            }
        }

        private static string UniqueId(string id, HashSet<string> used)
        {
            var candidate = id;
            for (var i = 2; !used.Add(candidate); i++) candidate = $"{id}-{i}";
            return candidate;
        }

        private static string StableHash(string text)
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
