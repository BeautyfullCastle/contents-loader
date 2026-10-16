using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace H.ContentsLoader.Editor
{
    internal static class ContentsBuildReport
    {
        public static void Write(BuildTarget target, ContentsBuildResult result, List<ContentsEntry> entries, Dictionary<ContentsEntry, string> guidOf)
        {
            var pathOfRoot = entries.ToDictionary(e => guidOf[e], e => e.Path);
            var rootsOfPack = new Dictionary<string, List<string>>();
            foreach (var kv in result.Clusters.RequiredPacks)
            foreach (var pack in kv.Value)
            {
                if (!rootsOfPack.TryGetValue(pack, out var list)) rootsOfPack.Add(pack, list = new List<string>());
                list.Add(pathOfRoot[kv.Key]);
            }

            var packs = result.Clusters.Layout.Packs;
            var report = new StringBuilder();
            report.AppendLine($"Contents Loader build report ({target}, {DateTime.Now:yyyy-MM-dd HH:mm:ss})");
            foreach (var warning in result.Warnings) report.AppendLine($"WARNING {warning}");
            // 낭비: 팩을 여는 루트가 안 쓰는데 같이 올라오는 바이트. 작은 팩 병합과 이전 배치 재사용에서 생김
            report.AppendLine($"waste {packs.Sum(p => p.WasteBytes) / 1024f:0.0} KB in {packs.Count(p => p.WasteBytes > 0)} packs, repacked {packs.Count(p => p.Repacked)}");
            report.AppendLine();
            foreach (var pack in packs)
            {
                var index = Array.FindIndex(result.Catalog.Packs, p => p.FileName == result.FileOfPack[pack.Id]);
                var notes = (pack.Reused ? "  (reused)" : "") + (pack.Repacked ? "  (repacked)" : "")
                            + (pack.WasteBytes > 0 ? $"  waste {pack.WasteBytes / 1024f:0.0} KB" : "");
                report.AppendLine($"{result.FileOfPack[pack.Id]}  {pack.Delivery}  {result.Catalog.Packs[index].Size / 1024f:0.0} KB{notes}");
                rootsOfPack.TryGetValue(pack.Id, out var users);
                report.AppendLine($"  used by: {string.Join(", ", (users ?? new List<string>()).OrderBy(s => s))}");
                foreach (var member in pack.Members)
                {
                    var implicitNote = result.ImplicitMembers.Contains(member) ? "  (referenced objects only)" : "";
                    report.AppendLine($"  - {AssetDatabase.GUIDToAssetPath(member)}{implicitNote}");
                }
            }
            File.WriteAllText(ContentsBuildPaths.Report(target), report.ToString());
        }
    }
}
