using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace H.ContentsLoader.Editor
{
    internal static class ContentsCatalogFactory
    {
        // sizeFolder는 원격으로 내려받는 크기를 재는 번들 폴더임
        public static ContentCatalog Create(ContentsBuildResult result, List<ContentsEntry> entries,
            Dictionary<ContentsEntry, string> guidOf, Dictionary<string, string[]> dependencies, string sizeFolder,
            ContentsSettings settings, BuildTarget target)
        {
            var packs = result.Clusters.Layout.Packs;
            var indexOf = packs.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);

            var catalog = new ContentCatalog
            {
                BuildTimestamp = DateTime.UtcNow.Ticks,
                RemoteUrl = (settings.RemoteUrl ?? "").Replace("{Platform}", target.ToString()),
                MemoryBudget = settings.MemoryBudgetBytes,
                Packs = packs.Select(p => new ContentCatalog.Pack
                {
                    FileName = result.FileOfPack[p.Id],
                    Delivery = p.Delivery,
                    Size = new FileInfo($"{sizeFolder}/{p.Id}").Length,
                    Dependencies = dependencies[p.Id].Select(d => indexOf[d]).OrderBy(i => i).ToArray(),
                }).ToArray(),
            };

            var typeNames = new List<string>();
            var sharedPaths = entries.GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            catalog.Entries = entries.Select(e => new ContentCatalog.Entry
            {
                PathHash = ContentPath.Hash(e.Path),
                Pack = indexOf[result.Clusters.PackOf[guidOf[e]].Id],
                AssetName = e.AssetPath,
                Delivery = settings.ResolveDelivery(e),
                Types = sharedPaths.Contains(e.Path) ? TypesIn(e.AssetPath, typeNames) : null,
            }).ToArray();
            catalog.TypeNames = typeNames.ToArray();

            var groups = new Dictionary<string, SortedSet<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in catalog.Entries.Zip(entries, (c, e) => (c, e)))
            {
                var needed = catalog.Packs[entry.c.Pack].Dependencies.Append(entry.c.Pack).ToList();
                for (var folder = ContentsBuilder.FolderOf(entry.e.Path); folder.Length > 0; folder = ContentsBuilder.FolderOf(folder))
                {
                    if (!groups.TryGetValue(folder, out var set)) groups.Add(folder, set = new SortedSet<int>());
                    set.UnionWith(needed);
                }
            }
            catalog.Groups = groups.Select(g => new ContentCatalog.Group
            {
                FolderHash = ContentPath.Hash(g.Key),
                Packs = g.Value.ToArray(),
            }).ToArray();

            catalog.Sort();
            return catalog;
        }

        // 기반 타입까지 넣어 두면 런타임은 요청 타입 이름만 찾으면 됨
        private static int[] TypesIn(string assetPath, List<string> typeNames)
        {
            var indices = new SortedSet<int>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset == null) continue;
                for (var type = asset.GetType(); type != null && type != typeof(object); type = type.BaseType)
                {
                    var index = typeNames.IndexOf(type.FullName);
                    if (index < 0)
                    {
                        index = typeNames.Count;
                        typeNames.Add(type.FullName);
                    }
                    indices.Add(index);
                }
            }
            return indices.ToArray();
        }
    }
}
