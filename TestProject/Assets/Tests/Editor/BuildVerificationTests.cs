using System.Collections.Generic;
using System.IO;
using System.Linq;
using H.ContentsLoader.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace H.ContentsLoader.Integration.Editor
{
    internal static class FixtureBuild
    {
        public static BuildTarget Target => EditorUserBuildSettings.activeBuildTarget;

        public static ContentsBuildResult Run(int smallPackThresholdKB, bool ignorePrevious = true)
        {
            var settings = Object.Instantiate(ContentsSettings.Find());
            try
            {
                settings.SmallPackThresholdKB = smallPackThresholdKB;
                settings.IgnorePreviousLayout = ignorePrevious;
                var result = ContentsBuilder.Build(Target, settings);
                Assert.That(result.Errors, Is.Empty);
                return result;
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        public static string Guid(string path) => AssetDatabase.AssetPathToGUID(path);
    }

    public class BuildVerificationTests
    {
        ContentsBuildResult result;

        [OneTimeSetUp]
        public void Build() => result = FixtureBuild.Run(smallPackThresholdKB: 0);

        PackPlan PackOf(string assetPath) => result.Clusters.PackOf[FixtureBuild.Guid(assetPath)];

        [Test]
        public void CatalogListsEveryContentPath()
        {
            var catalog = ContentCatalog.FromBytes(File.ReadAllBytes($"{result.BuiltInDirectory}/{ContentCatalog.FileName}"));

            foreach (var path in new[] { "Towers/Cannon", "Towers/Archer", "UI/Shop", "Data/Config", "Bosses/Dragon", "Season01/Goblin" })
                Assert.That(catalog.TryFindEntry(path, null, out _), path);
            Assert.That(catalog.Entries, Has.Length.EqualTo(7));
            Assert.That(catalog.FindEntries("Towers/Cannon").Count(), Is.EqualTo(2));
            Assert.That(catalog.TryFindEntry("Towers/Cannon", typeof(Texture2D), out var texture));
            Assert.That(texture.AssetName, Is.EqualTo("Assets/Game/Contents/Towers/Cannon.png"));
            Assert.That(catalog.TryFindEntry("Towers/Cannon", typeof(GameObject), out var prefab));
            Assert.That(prefab.AssetName, Is.EqualTo("Assets/Game/Contents/Towers/Cannon.prefab"));
            Assert.That(catalog.RemoteUrl, Is.EqualTo($"https://cdn.invalid/contents/{FixtureBuild.Target}"));
        }

        [Test]
        public void EveryAssetIsInExactlyOneBundleAndNoDependencyIsLeftImplicit()
        {
            var bundles = new List<AssetBundle>();
            try
            {
                var owner = new Dictionary<string, string>();
                foreach (var pack in result.Clusters.Layout.Packs)
                {
                    var bundle = AssetBundle.LoadFromFile($"{result.WorkDirectory}/lz4/{pack.Id}");
                    Assert.That(bundle, Is.Not.Null, pack.Id);
                    bundles.Add(bundle);
                    foreach (var name in bundle.GetAllAssetNames())
                    {
                        Assert.That(owner.ContainsKey(name), Is.False, $"{name}이(가) {pack.Id}와 {(owner.TryGetValue(name, out var o) ? o : "")}에 중복됨");
                        owner[name] = pack.Id;
                    }
                }

                // 명시 배정 안 된 의존 애셋만 Unity가 참조하는 번들마다 복제함
                foreach (var name in owner.Keys.ToList())
                foreach (var dependency in AssetDatabase.GetDependencies(name, true))
                {
                    if (dependency.EndsWith(".cs")) continue;
                    Assert.That(owner.ContainsKey(dependency.ToLowerInvariant()), $"{name}의 의존 애셋 {dependency}가 어느 번들에도 명시되지 않음");
                }
                Assert.That(owner.Keys, Does.Contain("assets/art/shared.png"));
            }
            finally
            {
                foreach (var bundle in bundles) bundle.Unload(true);
            }
        }

        [Test]
        public void SharedTextureIsSeparatedAndBuiltIn()
        {
            var shared = PackOf("Assets/Art/Shared.png");

            Assert.That(shared, Is.Not.SameAs(PackOf("Assets/Art/Cannon.png")));
            Assert.That(shared, Is.Not.SameAs(PackOf("Assets/Art/Dragon.png")));
            Assert.That(shared, Is.SameAs(PackOf("Assets/Art/Shared.mat")));
            Assert.That(shared.Delivery, Is.EqualTo(ContentDelivery.BuiltIn), "Built-in 루트와 공유하므로 Built-in이어야 함");
        }

        [Test]
        public void DownloadOnlyAssetsAreDownloadPacks()
        {
            Assert.That(PackOf("Assets/Art/Dragon.png").Delivery, Is.EqualTo(ContentDelivery.Download));
            Assert.That(PackOf("Assets/DLC/Contents/Season01/Goblin.prefab").Delivery, Is.EqualTo(ContentDelivery.Download));
            Assert.That(PackOf("Assets/Game/Contents/Towers/Cannon.prefab").Delivery, Is.EqualTo(ContentDelivery.BuiltIn));
        }

        [Test]
        public void ShadersGoToTheShaderPack()
        {
            var shaders = PackOf("Assets/Art/Simple.shader");

            Assert.That(shaders.Id, Is.EqualTo(PackClustering.ShaderPackId));
            Assert.That(shaders.Delivery, Is.EqualTo(ContentDelivery.BuiltIn));
        }

        [Test]
        public void OutputSplitsBuiltInAndRemotePacks()
        {
            foreach (var pack in result.Clusters.Layout.Packs)
            {
                var file = result.FileOfPack[pack.Id];
                Assert.That(File.Exists($"{result.BuiltInDirectory}/{file}"), Is.EqualTo(pack.Delivery == ContentDelivery.BuiltIn), file);
                Assert.That(File.Exists($"{result.RemoteDirectory}/{file}"), file);
            }
            Assert.That(File.Exists($"{result.RemoteDirectory}/{ContentCatalog.FileName}"));
        }

        [Test]
        public void RemotePacksUseLzmaAndBuiltInPacksUseLz4()
        {
            var dragon = result.FileOfPack[PackOf("Assets/Art/Dragon.png").Id];
            var lz4 = new FileInfo($"{result.WorkDirectory}/lz4/{PackOf("Assets/Art/Dragon.png").Id}").Length;
            var remote = new FileInfo($"{result.RemoteDirectory}/{dragon}").Length;

            Assert.That(remote, Is.LessThan(lz4));
        }
    }

    public class BuildStabilityTests
    {
        [Test]
        public void RebuildWithPreviousLayoutKeepsPackFiles()
        {
            var first = FixtureBuild.Run(smallPackThresholdKB: 0);
            var second = FixtureBuild.Run(smallPackThresholdKB: 0, ignorePrevious: false);

            Assert.That(second.FileOfPack, Is.EquivalentTo(first.FileOfPack));
        }

        [Test]
        public void SmallPacksMergeWithDefaultThreshold()
        {
            var merged = FixtureBuild.Run(smallPackThresholdKB: 64);
            var cannon = merged.Clusters.PackOf[FixtureBuild.Guid("Assets/Game/Contents/Towers/Cannon.prefab")];

            Assert.That(cannon, Is.SameAs(merged.Clusters.PackOf[FixtureBuild.Guid("Assets/Art/Shared.png")]));
            Assert.That(merged.Clusters.PackOf[FixtureBuild.Guid("Assets/Art/Dragon.png")].Delivery, Is.EqualTo(ContentDelivery.Download));
        }
    }
}
