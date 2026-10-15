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

            var report = new StringBuilder();
            report.AppendLine($"Contents Loader build report ({target}, {DateTime.Now:yyyy-MM-dd HH:mm:ss})");
            foreach (var warning in result.Warnings) report.AppendLine($"WARNING {warning}");
            report.AppendLine();
            foreach (var pack in result.Clusters.Layout.Packs)
            {
                var index = Array.FindIndex(result.Catalog.Packs, p => p.FileName == result.FileOfPack[pack.Id]);
                report.AppendLine($"{result.FileOfPack[pack.Id]}  {pack.Delivery}  {result.Catalog.Packs[index].Size / 1024f:0.0} KB{(pack.Reused ? "  (reused)" : "")}");
                rootsOfPack.TryGetValue(pack.Id, out var users);
                report.AppendLine($"  used by: {string.Join(", ", (users ?? new List<string>()).OrderBy(s => s))}");
                foreach (var member in pack.Members) report.AppendLine($"  - {AssetDatabase.GUIDToAssetPath(member)}");
            }
            File.WriteAllText(ContentsBuildPaths.Report(target), report.ToString());
        }
    }
}
