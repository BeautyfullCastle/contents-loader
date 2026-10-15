using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace H.ContentsLoader.Editor
{
    internal sealed class ContentsBuildResult
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public ClusterResult Clusters;
        public ContentCatalog Catalog;
        public readonly Dictionary<string, string> FileOfPack = new();
        public string BuiltInDirectory;
        public string RemoteDirectory;
        public string WorkDirectory;

        public bool Success => Errors.Count == 0;
    }

    public static class ContentsBuilder
    {
        const string Lz4Folder = "lz4";
        const string LzmaFolder = "lzma";

        /// <summary>Entry point for <c>-executeMethod</c>. Builds for the active build target and exits with code 1 on failure.</summary>
        public static void BuildFromCommandLine()
        {
            var result = Build(EditorUserBuildSettings.activeBuildTarget);
            if (!result.Success) EditorApplication.Exit(1);
        }

        [MenuItem("Tools/Contents Loader/Build Content")]
        static void BuildFromMenu()
        {
            var result = Build(EditorUserBuildSettings.activeBuildTarget);
            if (result.Success)
                EditorUtility.RevealInFinder(ContentsBuildPaths.Root(EditorUserBuildSettings.activeBuildTarget));
        }

        internal static ContentsBuildResult Build(BuildTarget target, ContentsSettings settings = null, IDependencyCollector collector = null)
        {
            settings ??= ContentsSettings.FindOrDefault();
            collector ??= new FileDependencyCollector();
            var result = new ContentsBuildResult
            {
                BuiltInDirectory = ContentsBuildPaths.BuiltIn(target),
                RemoteDirectory = ContentsBuildPaths.Remote(target),
                WorkDirectory = ContentsBuildPaths.Work(target),
            };

            var scan = ContentsScanner.ScanProject();
            result.Errors.AddRange(scan.Errors);
            result.Warnings.AddRange(scan.Warnings);
            if (!result.Success) return Finish(result);

            var entries = scan.Entries;
            var guidOf = entries.ToDictionary(e => e, e => AssetDatabase.AssetPathToGUID(e.AssetPath));
            var roots = entries.Select(e => new ClusterRoot
            {
                Key = guidOf[e],
                Group = FolderOf(e.Path),
                Delivery = settings.ResolveDelivery(e),
            }).ToList();

            var nodes = DependencyGraph.Collect(roots.Select(r => r.Key), collector);
            var previous = settings.IgnorePreviousLayout ? null : ReadLayout(target);
            result.Clusters = PackClustering.Run(roots, nodes, new ClusterOptions
            {
                SmallPackThreshold = settings.SmallPackThresholdKB * 1024L,
                SignatureByGroup = settings.SignatureByFolder,
            }, previous);
            WarnNestedRootPrefabs(entries, guidOf, result);

            var builds = result.Clusters.Layout.Packs.Select(p => new AssetBundleBuild
            {
                assetBundleName = p.Id,
                assetNames = p.Members.Select(AssetDatabase.GUIDToAssetPath).ToArray(),
            }).ToArray();
            if (builds.Length == 0)
            {
                result.Warnings.Add("Contents 폴더에 빌드할 애셋이 없습니다.");
                result.Catalog = new ContentCatalog { BuildTimestamp = DateTime.UtcNow.Ticks };
                WriteOutput(result, false);
                return Finish(result);
            }

            var lz4 = BuildBundles(result, builds, BuildAssetBundleOptions.ChunkBasedCompression, Lz4Folder, target);
            if (lz4 == null) return Finish(result);

            var needsRemote = result.Clusters.Layout.Packs.Any(p => p.Delivery == ContentDelivery.Download)
                              || !string.IsNullOrEmpty(settings.RemoteUrl);
            if (needsRemote && BuildBundles(result, builds, BuildAssetBundleOptions.None, LzmaFolder, target) == null)
                return Finish(result);

            foreach (var pack in result.Clusters.Layout.Packs)
                result.FileOfPack[pack.Id] = $"{pack.Id}_{lz4.Hashes[pack.Id]}.bundle";

            result.Catalog = CreateCatalog(result, entries, guidOf, lz4, needsRemote, settings, target);
            VerifyDeliveryBoundary(result, lz4);
            if (!result.Success) return Finish(result);

            WriteOutput(result, needsRemote);
            WriteLayout(target, result.Clusters.Layout);
            ContentsBuildReport.Write(target, result, entries, guidOf);
            if (settings.GeneratePathConstants)
                PathConstantsGenerator.Write(settings.PathConstantsFile, entries.Select(e => e.Path));
            return Finish(result);
        }

        static ContentsBuildResult Finish(ContentsBuildResult result)
        {
            foreach (var warning in result.Warnings) Debug.LogWarning($"[Contents Loader] {warning}");
            foreach (var error in result.Errors) Debug.LogError($"[Contents Loader] {error}");
            if (result.Success)
                Debug.Log($"[Contents Loader] 빌드 완료: 팩 {result.Clusters?.Layout.Packs.Count ?? 0}개, 경로 {result.Catalog?.Entries.Length ?? 0}개");
            return result;
        }

        static string FolderOf(string contentPath)
        {
            var slash = contentPath.LastIndexOf('/');
            return slash < 0 ? "" : contentPath.Substring(0, slash);
        }

        sealed class BuiltBundles
        {
            public readonly Dictionary<string, string> Hashes = new();
            public readonly Dictionary<string, string[]> Dependencies = new();
        }

        static BuiltBundles BuildBundles(ContentsBuildResult result, AssetBundleBuild[] builds,
            BuildAssetBundleOptions compression, string folder, BuildTarget target)
        {
            var output = $"{result.WorkDirectory}/{folder}";
            Directory.CreateDirectory(output);
            var manifest = BuildPipeline.BuildAssetBundles(output, builds,
                compression | BuildAssetBundleOptions.StrictMode, target);
            if (manifest == null)
            {
                result.Errors.Add($"번들 빌드에 실패했습니다 ({folder}). 콘솔 로그를 확인하세요.");
                return null;
            }

            // 다음 BuildAssetBundles 호출이 이 manifest 객체를 파괴하므로 값으로 꺼내 둠
            var built = new BuiltBundles();
            foreach (var name in manifest.GetAllAssetBundles())
            {
                built.Hashes[name] = manifest.GetAssetBundleHash(name).ToString();
                built.Dependencies[name] = manifest.GetAllDependencies(name);
            }
            return built;
        }

        static ContentCatalog CreateCatalog(ContentsBuildResult result, List<ContentsEntry> entries,
            Dictionary<ContentsEntry, string> guidOf, BuiltBundles lz4, bool sizesFromLzma,
            ContentsSettings settings, BuildTarget target)
        {
            var packs = result.Clusters.Layout.Packs;
            var indexOf = packs.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
            var sizeFolder = $"{result.WorkDirectory}/{(sizesFromLzma ? LzmaFolder : Lz4Folder)}";

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
                    Dependencies = lz4.Dependencies[p.Id].Select(d => indexOf[d]).OrderBy(i => i).ToArray(),
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
                for (var folder = FolderOf(entry.e.Path); folder.Length > 0; folder = FolderOf(folder))
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
        static int[] TypesIn(string assetPath, List<string> typeNames)
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

        static void VerifyDeliveryBoundary(ContentsBuildResult result, BuiltBundles bundles)
        {
            var deliveryOf = result.Clusters.Layout.Packs.ToDictionary(p => p.Id, p => p.Delivery);
            foreach (var pack in result.Clusters.Layout.Packs.Where(p => p.Delivery == ContentDelivery.BuiltIn))
            foreach (var dependency in bundles.Dependencies[pack.Id])
                if (deliveryOf[dependency] == ContentDelivery.Download)
                    result.Errors.Add($"Built-in 팩 {pack.Id}이(가) Download 팩 {dependency}에 의존합니다.");
        }

        static void WriteOutput(ContentsBuildResult result, bool remote)
        {
            ResetDirectory(result.BuiltInDirectory);
            var bytes = result.Catalog.ToBytes();
            File.WriteAllBytes($"{result.BuiltInDirectory}/{ContentCatalog.FileName}", bytes);
            foreach (var pack in result.Clusters.Layout.Packs.Where(p => p.Delivery == ContentDelivery.BuiltIn))
                File.Copy($"{result.WorkDirectory}/{Lz4Folder}/{pack.Id}", $"{result.BuiltInDirectory}/{result.FileOfPack[pack.Id]}");

            ResetDirectory(result.RemoteDirectory);
            if (!remote) return;
            // 원격에는 Built-in 팩도 올림. 패치로 바뀐 Built-in 팩은 앱에 없어서 받아야 함
            File.WriteAllBytes($"{result.RemoteDirectory}/{ContentCatalog.FileName}", bytes);
            foreach (var pack in result.Clusters.Layout.Packs)
                File.Copy($"{result.WorkDirectory}/{LzmaFolder}/{pack.Id}", $"{result.RemoteDirectory}/{result.FileOfPack[pack.Id]}");
        }

        static void ResetDirectory(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            Directory.CreateDirectory(path);
        }

        static PackLayout ReadLayout(BuildTarget target)
        {
            var path = ContentsBuildPaths.Layout(target);
            return File.Exists(path) ? JsonUtility.FromJson<PackLayout>(File.ReadAllText(path)) : null;
        }

        static void WriteLayout(BuildTarget target, PackLayout layout)
        {
            File.WriteAllText(ContentsBuildPaths.Layout(target), JsonUtility.ToJson(layout, true));
        }

        // 중첩 프리팹은 빌드 때 바깥 프리팹 안으로 펼쳐짐. 독립 루트이기도 하면 계층이 두 번 들어가고 팩 분리로 못 막음
        static void WarnNestedRootPrefabs(List<ContentsEntry> entries, Dictionary<ContentsEntry, string> guidOf, ContentsBuildResult result)
        {
            var rootPrefabs = entries
                .Where(e => e.AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(e => e.AssetPath, e => e);
            foreach (var entry in rootPrefabs.Values)
            {
                var nested = AssetDatabase.GetDependencies(entry.AssetPath, true)
                    .Where(d => d != entry.AssetPath && rootPrefabs.ContainsKey(d))
                    .ToList();
                if (nested.Count == 0) continue;
                var size = nested.Sum(p => new FileInfo(p).Length) / 1024;
                result.Warnings.Add($"계층 중복 약 {size} KB: '{entry.Path}'이(가) 루트 프리팹 {string.Join(", ", nested.Select(p => $"'{rootPrefabs[p].Path}'"))}을(를) 중첩합니다.");
            }
        }
    }
}
