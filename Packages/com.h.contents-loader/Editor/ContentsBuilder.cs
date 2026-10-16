using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using ThreadPriority = UnityEngine.ThreadPriority;

namespace H.ContentsLoader.Editor
{
    internal sealed class ContentsBuildResult
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public ClusterResult Clusters;

        // 명시 배정하지 않고 Unity가 참조된 하위 오브젝트만 넣게 둔 의존 애셋(GUID)
        public readonly HashSet<string> ImplicitMembers = new();
        public ContentCatalog Catalog;
        public readonly Dictionary<string, string> FileOfPack = new();
        public string BuiltInDirectory;
        public string RemoteDirectory;
        public string WorkDirectory;

        // 단계별 소요 시간(ms). 빌드 로그와 리포트에 씀
        public readonly List<(string Phase, long Milliseconds)> Timings = new();

        public bool Success => Errors.Count == 0;

        internal void Time(string phase, System.Diagnostics.Stopwatch watch)
        {
            Timings.Add((phase, watch.ElapsedMilliseconds));
            watch.Restart();
        }
    }

    public static class ContentsBuilder
    {
        private const string Lz4Folder = "lz4";
        private const string LzmaFolder = "lzma";
        private const string GZipFolder = "gzip";

        /// <summary>Entry point for <c>-executeMethod</c>. Builds for the active build target and exits with code 1 on failure.</summary>
        public static void BuildFromCommandLine()
        {
            var result = Build(EditorUserBuildSettings.activeBuildTarget);
            if (!result.Success) EditorApplication.Exit(1);
        }

        /// <summary>Builds content for the options' target, then builds the player with that content in StreamingAssets.</summary>
        /// <exception cref="UnityEditor.Build.BuildFailedException">The content build failed.</exception>
        public static UnityEditor.Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options)
        {
            ContentsPlayerBuild.BuildContent(options.target);
            return BuildPipeline.BuildPlayer(options);
        }

        /// <summary>Builds content for a target. Errors and warnings go to the console.</summary>
        /// <returns>True when the build succeeded.</returns>
        public static bool BuildContent(BuildTarget target) => Build(target).Success;

        /// <summary>Builds content for the editor's own platform, which the Bundles play mode source reads.</summary>
        public static bool BuildForPlayMode() => BuildContent(ContentsBuildPaths.PlayModeTarget);

        [MenuItem("Tools/Contents Loader/Build Content")]
        private static void BuildFromMenu()
        {
            var result = Build(EditorUserBuildSettings.activeBuildTarget);
            if (result.Success)
                EditorUtility.RevealInFinder(ContentsBuildPaths.Root(EditorUserBuildSettings.activeBuildTarget));
        }

        [MenuItem("Tools/Contents Loader/Build Content for Play Mode")]
        private static void BuildForPlayModeFromMenu() => BuildForPlayMode();

        internal static ContentsBuildResult Build(BuildTarget target, ContentsSettings settings = null, IDependencyCollector collector = null)
        {
            settings ??= ContentsSettings.FindOrDefault();
            collector ??= new FileDependencyCollector();
            var watch = System.Diagnostics.Stopwatch.StartNew();
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

            result.Time("scan", watch);
            var entries = scan.Entries;
            var guidOf = entries.ToDictionary(e => e, e => AssetDatabase.AssetPathToGUID(e.AssetPath));
            var roots = entries.Select(e => new ClusterRoot
            {
                Key = guidOf[e],
                Group = FolderOf(e.Path),
                Delivery = settings.ResolveDelivery(e),
            }).ToList();

            var nodes = DependencyGraph.Collect(roots.Select(r => r.Key), collector);
            result.Time("dependencies", watch);
            var previous = settings.IgnorePreviousLayout ? null : ReadLayout(target);
            result.Clusters = PackClustering.Run(roots, nodes, new ClusterOptions
            {
                SmallPackThreshold = settings.SmallPackThresholdKB * 1024L,
                SignatureByGroup = settings.SignatureByFolder,
                MaxReuseWaste = settings.RepackWastePercent / 100f,
            }, previous);
            WarnNestedRootPrefabs(entries, guidOf, result);
            WarnDeliveryChanges(previous ?? ReadLayout(target), result, entries, guidOf);
            result.ImplicitMembers.UnionWith(SelectImplicitMembers(result.Clusters, nodes, roots.Select(r => r.Key)));
            result.Time("layout", watch);

            var builds = result.Clusters.Layout.Packs.Select(p => new AssetBundleBuild
            {
                assetBundleName = p.Id,
                assetNames = p.Members.Where(m => !result.ImplicitMembers.Contains(m)).Select(AssetDatabase.GUIDToAssetPath).ToArray(),
            }).ToArray();
            if (builds.Length == 0)
            {
                result.Warnings.Add("Contents 폴더에 빌드할 애셋이 없습니다.");
                result.Catalog = new ContentCatalog { BuildTimestamp = DateTime.UtcNow.Ticks };
                WriteOutput(result, null);
                return Finish(result);
            }

            var lz4 = BuildBundles(result, builds, BuildAssetBundleOptions.ChunkBasedCompression, Lz4Folder, target);
            result.Time("bundles", watch);
            if (lz4 == null) return Finish(result);

            var needsRemote = result.Clusters.Layout.Packs.Any(p => p.Delivery == ContentDelivery.Download)
                              || !string.IsNullOrEmpty(settings.RemoteUrl);
            Dictionary<string, string> remoteFiles = null;
            if (needsRemote)
            {
                remoteFiles = BuildRemoteFiles(result, builds, lz4, settings.RemoteCompression, target);
                if (remoteFiles == null) return Finish(result);
            }
            result.Time("remote compression", watch);

            foreach (var pack in result.Clusters.Layout.Packs)
                result.FileOfPack[pack.Id] = $"{pack.Id}_{lz4.Hashes[pack.Id]}.bundle";

            result.Catalog = ContentsCatalogFactory.Create(result, entries, guidOf, lz4.Dependencies,
                id => new FileInfo(remoteFiles != null ? remoteFiles[id] : $"{result.WorkDirectory}/{Lz4Folder}/{id}").Length, settings, target);
            if (needsRemote) result.Catalog.RemoteCompression = settings.RemoteCompression;
            VerifyDeliveryBoundary(result, lz4);
            if (!result.Success) return Finish(result);

            result.Time("catalog", watch);
            WriteOutput(result, remoteFiles);
            WriteLayout(target, result.Clusters.Layout);
            WriteContentTypes(target, result);
            result.Time("output", watch);
            ContentsBuildReport.Write(target, result, entries, guidOf);
            if (settings.GeneratePathConstants)
                PathConstantsGenerator.Write(settings.PathConstantsFile, entries.Select(e => e.Path));
            return Finish(result);
        }

        private static ContentsBuildResult Finish(ContentsBuildResult result)
        {
            foreach (var warning in result.Warnings) Debug.LogWarning($"[Contents Loader] {warning}");
            foreach (var error in result.Errors) Debug.LogError($"[Contents Loader] {error}");
            if (result.Success)
                Debug.Log($"[Contents Loader] 빌드 완료: 팩 {result.Clusters?.Layout.Packs.Count ?? 0}개, 경로 {result.Catalog?.Entries.Length ?? 0}개, " +
                          $"{result.Timings.Sum(t => t.Milliseconds) / 1000.0:0.0}초 ({string.Join(", ", result.Timings.Select(t => $"{t.Phase} {t.Milliseconds}ms"))})");
            return result;
        }

        internal static string FolderOf(string contentPath)
        {
            var slash = contentPath.LastIndexOf('/');
            return slash < 0 ? "" : contentPath.Substring(0, slash);
        }

        private sealed class BuiltBundles
        {
            public readonly Dictionary<string, string> Hashes = new();
            public readonly Dictionary<string, string[]> Dependencies = new();
        }

        private static BuiltBundles BuildBundles(ContentsBuildResult result, AssetBundleBuild[] builds,
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

        private static void VerifyDeliveryBoundary(ContentsBuildResult result, BuiltBundles bundles)
        {
            var deliveryOf = result.Clusters.Layout.Packs.ToDictionary(p => p.Id, p => p.Delivery);
            foreach (var pack in result.Clusters.Layout.Packs.Where(p => p.Delivery == ContentDelivery.BuiltIn))
            foreach (var dependency in bundles.Dependencies[pack.Id])
                if (deliveryOf[dependency] == ContentDelivery.Download)
                    result.Errors.Add($"Built-in 팩 {pack.Id}이(가) Download 팩 {dependency}에 의존합니다.");
        }

        // remoteFiles는 팩 ID별 서버용 파일. null이면 원격 출력을 만들지 않음
        private static void WriteOutput(ContentsBuildResult result, Dictionary<string, string> remoteFiles)
        {
            ResetDirectory(result.BuiltInDirectory);
            var bytes = result.Catalog.ToBytes();
            File.WriteAllBytes($"{result.BuiltInDirectory}/{ContentCatalog.FileName}", bytes);
            foreach (var pack in result.Clusters.Layout.Packs.Where(p => p.Delivery == ContentDelivery.BuiltIn))
                File.Copy($"{result.WorkDirectory}/{Lz4Folder}/{pack.Id}", $"{result.BuiltInDirectory}/{result.FileOfPack[pack.Id]}");

            ResetDirectory(result.RemoteDirectory);
            if (remoteFiles == null) return;
            // 원격에는 Built-in 팩도 올림. 패치로 바뀐 Built-in 팩은 앱에 없어서 받아야 함
            File.WriteAllBytes($"{result.RemoteDirectory}/{ContentCatalog.FileName}", bytes);
            foreach (var pack in result.Clusters.Layout.Packs)
            {
                var name = ContentCatalog.RemoteFileName(result.FileOfPack[pack.Id], result.Catalog.RemoteCompression);
                File.Copy(remoteFiles[pack.Id], $"{result.RemoteDirectory}/{name}");
            }
        }

        private static Dictionary<string, string> BuildRemoteFiles(ContentsBuildResult result, AssetBundleBuild[] builds, BuiltBundles lz4,
            RemoteCompression compression, BuildTarget target)
        {
            var ids = result.Clusters.Layout.Packs.Select(p => p.Id).ToList();
            switch (compression)
            {
                case RemoteCompression.LZ4:
                    return ids.ToDictionary(id => id, id => $"{result.WorkDirectory}/{Lz4Folder}/{id}");
                case RemoteCompression.GZip:
                    return GZipPacks(result, ids, lz4);
                default:
                    // Unity는 이미 만든 번들을 LZMA로 재압축하지 못해서 LZMA로 한 번 더 빌드함. 안 바뀐 번들은 Unity가 건너뜀
                    if (BuildBundles(result, builds, BuildAssetBundleOptions.None, LzmaFolder, target) == null) return null;
                    return ids.ToDictionary(id => id, id => $"{result.WorkDirectory}/{LzmaFolder}/{id}");
            }
        }

        // LZ4 번들을 압축 없는 번들로 푼 뒤 gzip함. 결과는 LZ4 해시별로 남겨서 안 바뀐 팩은 다시 압축하지 않음
        private static Dictionary<string, string> GZipPacks(ContentsBuildResult result, List<string> ids, BuiltBundles lz4)
        {
            var folder = $"{result.WorkDirectory}/{GZipFolder}";
            Directory.CreateDirectory(folder);
            var files = ids.ToDictionary(id => id, id => $"{folder}/{id}_{lz4.Hashes[id]}.gz");
            var stale = ids.Where(id => !File.Exists(files[id])).ToList();

            // Unity 재압축은 작업 스레드에서 돌아서 한꺼번에 시작하고 끝날 때까지 기다림
            var operations = stale.Select(id => (Id: id, Operation: AssetBundle.RecompressAssetBundleAsync(
                $"{result.WorkDirectory}/{Lz4Folder}/{id}", $"{folder}/{id}.unc", BuildCompression.Uncompressed, 0, ThreadPriority.High))).ToList();
            while (operations.Any(o => !o.Operation.isDone)) Thread.Sleep(5);
            foreach (var (id, operation) in operations)
                if (!operation.success) result.Errors.Add($"팩 {id}의 압축을 풀지 못했습니다: {operation.humanReadableResult}");
            if (!result.Success) return null;

            Parallel.ForEach(stale, id =>
            {
                var uncompressed = $"{folder}/{id}.unc";
                var temp = files[id] + ".tmp";
                using (var input = File.OpenRead(uncompressed))
                using (var output = new GZipStream(File.Create(temp), CompressionLevel.Optimal))
                    input.CopyTo(output, 1 << 20);
                File.Delete(uncompressed);
                File.Move(temp, files[id]);
            });

            // 같은 팩의 이전 결과와 레이아웃에서 빠진 팩은 지움
            var keep = new HashSet<string>(files.Values.Select(Path.GetFileName));
            foreach (var path in Directory.GetFiles(folder))
                if (!keep.Contains(Path.GetFileName(path))) File.Delete(path);
            return files;
        }

        private static void ResetDirectory(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            Directory.CreateDirectory(path);
        }

        private static PackLayout ReadLayout(BuildTarget target)
        {
            var path = ContentsBuildPaths.Layout(target);
            return File.Exists(path) ? JsonUtility.FromJson<PackLayout>(File.ReadAllText(path)) : null;
        }

        private static void WriteLayout(BuildTarget target, PackLayout layout)
        {
            File.WriteAllText(ContentsBuildPaths.Layout(target), JsonUtility.ToJson(layout, true));
        }

        // 명시 배정한 애셋은 파일 안의 오브젝트가 전부 번들에 들어감. FBX에서 메시 하나만 써도 나머지 메시와 애니메이션까지 따라옴
        // 배정하지 않은 의존 애셋은 Unity가 참조된 오브젝트만 넣음. 대신 참조하는 번들마다 복제하므로,
        // 직접 참조하는 쪽이 전부 같은 팩일 때만 배정을 빼서 복제 없이 필요한 오브젝트만 넣음
        internal static HashSet<string> SelectImplicitMembers(ClusterResult clusters, IReadOnlyDictionary<string, ClusterNode> nodes, IEnumerable<string> rootKeys)
        {
            var roots = new HashSet<string>(rootKeys);
            var referrers = new Dictionary<string, List<string>>();
            foreach (var node in nodes.Values)
            foreach (var dependency in node.Dependencies)
            {
                if (!referrers.TryGetValue(dependency, out var list)) referrers.Add(dependency, list = new List<string>());
                list.Add(node.Key);
            }

            var selected = new HashSet<string>();
            foreach (var pack in clusters.Layout.Packs)
            {
                if (pack.IsShader) continue;
                var candidates = pack.Members.Where(key =>
                    !roots.Contains(key) && !nodes[key].IsShader
                    && referrers.TryGetValue(key, out var from) && from.Count > 0
                    && from.All(r => clusters.PackOf.TryGetValue(r, out var p) && p == pack)).ToList();
                // 팩에 명시 애셋이 하나는 있어야 번들이 만들어짐
                if (candidates.Count < pack.Members.Count) selected.UnionWith(candidates);
            }
            return selected;
        }

        private static void WriteContentTypes(BuildTarget target, ContentsBuildResult result)
        {
            var types = ContentTypes.Collect(result.Clusters.Layout.Packs.SelectMany(p => p.Members).Select(AssetDatabase.GUIDToAssetPath));
            var missing = ContentTypes.MissingFromPlayer(target, types);
            if (missing.Count > 0)
                result.Warnings.Add($"마지막으로 빌드한 앱에 없는 타입 {missing.Count}개가 콘텐츠에 들어갔습니다: {string.Join(", ", missing.Take(10))}{(missing.Count > 10 ? " 외" : "")}. " +
                                    "이 콘텐츠를 패치로만 내보내면 기존 앱은 해당 컴포넌트를 로드하지 못합니다. 앱 업데이트와 함께 내보내세요.");
            ContentTypes.Write(target, types);
        }

        // Built-in이던 경로가 Download가 되면 이 빌드의 앱에는 안 들어감. 동기 Load가 받기 전에 불리면 실패함
        private static void WarnDeliveryChanges(PackLayout previous, ContentsBuildResult result, List<ContentsEntry> entries, Dictionary<ContentsEntry, string> guidOf)
        {
            if (previous == null) return;
            var wasBuiltIn = new HashSet<string>(previous.Packs.Where(p => p.Delivery == ContentDelivery.BuiltIn).SelectMany(p => p.Members));
            var moved = entries
                .Where(e => wasBuiltIn.Contains(guidOf[e]) && result.Clusters.PackOf[guidOf[e]].Delivery == ContentDelivery.Download)
                .Select(e => e.Path).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
            if (moved.Count == 0) return;
            result.Warnings.Add($"Built-in에서 Download로 바뀐 경로 {moved.Count}개: {string.Join(", ", moved.Take(10))}{(moved.Count > 10 ? " 외" : "")}. " +
                                "이 빌드의 앱에는 들어가지 않으니 동기 Content.Load로 부르는 코드는 Download나 LoadAsync 뒤로 옮기세요. " +
                                "이미 설치된 앱은 내용이 같은 내장 팩을 그대로 씁니다.");
        }

        // 중첩 프리팹은 빌드 때 바깥 프리팹 안으로 펼쳐짐. 독립 루트이기도 하면 계층이 두 번 들어가고 팩 분리로 못 막음
        private static void WarnNestedRootPrefabs(List<ContentsEntry> entries, Dictionary<ContentsEntry, string> guidOf, ContentsBuildResult result)
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
