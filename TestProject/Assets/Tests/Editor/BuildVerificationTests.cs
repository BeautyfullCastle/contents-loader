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

        public static ContentsBuildResult Run(int smallPackThresholdKB, bool ignorePrevious = true, RemoteCompression compression = RemoteCompression.LZMA)
        {
            var settings = Object.Instantiate(ContentsSettings.Find());
            try
            {
                settings.SmallPackThresholdKB = smallPackThresholdKB;
                settings.IgnorePreviousLayout = ignorePrevious;
                settings.RemoteCompression = compression;
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
        private ContentsBuildResult result;

        [OneTimeSetUp]
        public void Build() => result = FixtureBuild.Run(smallPackThresholdKB: 0);

        private PackPlan PackOf(string assetPath) => result.Clusters.PackOf[FixtureBuild.Guid(assetPath)];

        [Test]
        public void CatalogListsEveryContentPath()
        {
            var catalog = ContentCatalog.FromBytes(File.ReadAllBytes($"{result.BuiltInDirectory}/{ContentCatalog.FileName}"));

            foreach (var path in new[] { "Towers/Cannon", "Towers/Archer", "UI/Shop", "Data/Config", "Bosses/Dragon", "Season01/Goblin" })
                Assert.That(catalog.TryFindEntry(path, null, out _), path);
            // 쇼케이스 콘텐츠는 빼고 테스트 픽스처만 셈
            Assert.That(catalog.Entries.Count(e => !e.AssetName.StartsWith("Assets/Showcase/")), Is.EqualTo(7));
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

                // 명시 배정 안 된 의존 애셋은 Unity가 참조하는 번들마다 복제함
                // 그래서 배정을 뺀 애셋(ImplicitMembers)은 같은 팩 안에서만 참조돼야 함
                foreach (var pack in result.Clusters.Layout.Packs)
                foreach (var member in pack.Members.Select(AssetDatabase.GUIDToAssetPath))
                foreach (var dependency in AssetDatabase.GetDependencies(member, false))
                {
                    if (dependency == member || dependency.EndsWith(".cs")) continue;
                    var guid = FixtureBuild.Guid(dependency);
                    if (owner.ContainsKey(dependency.ToLowerInvariant())) continue;
                    Assert.That(result.ImplicitMembers, Does.Contain(guid), $"{member}의 의존 애셋 {dependency}가 배정되지 않았는데 오브젝트 단위 포함 대상도 아님");
                    Assert.That(result.Clusters.PackOf[guid], Is.SameAs(pack), $"{dependency}는 다른 팩 {pack.Id}에서 참조돼 복제됨");
                }
                // Shared.png는 Shared.mat만 참조하고 둘은 같은 팩이라 오브젝트 단위로 들어감
                Assert.That(result.ImplicitMembers, Does.Contain(FixtureBuild.Guid("Assets/Art/Shared.png")));
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

    public class ContentTypeTests
    {
        // 픽스처 프리팹의 BoxCollider는 테스트 씬 어디에도 없음. link.xml에 없으면 플레이어 빌드에서 엔진 코드가 제거됨
        [Test]
        public void LinkXmlKeepsTypesUsedOnlyInBundles()
        {
            FixtureBuild.Run(smallPackThresholdKB: 64);
            var xml = File.ReadAllText($"ContentsBuild/{FixtureBuild.Target}/link.xml");

            Assert.That(xml, Does.Contain("<type fullname=\"UnityEngine.BoxCollider\" preserve=\"all\"/>"));
            Assert.That(xml, Does.Contain("<assembly fullname=\"UnityEngine.PhysicsModule\">"));
            Assert.That(xml, Does.Contain("UnityEngine.MeshRenderer"));
            Assert.That(xml, Does.Not.Contain("UnityEditor"));
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

    public class RemoteCompressionBuildTests
    {
        // 다른 테스트가 기본 빌드 결과를 쓰므로 끝나면 되돌림
        [OneTimeTearDown]
        public void RestoreDefaultBuild() => FixtureBuild.Run(smallPackThresholdKB: 64);

        private static string RemoteFile(ContentsBuildResult result, ContentCatalog.Pack pack) =>
            $"{result.RemoteDirectory}/{ContentCatalog.RemoteFileName(pack.FileName, result.Catalog.RemoteCompression)}";

        [Test]
        public void GZipPacksAreUncompressedBundlesAndSizesMatch()
        {
            var result = FixtureBuild.Run(smallPackThresholdKB: 64, compression: RemoteCompression.GZip);
            var catalog = ContentCatalog.FromBytes(File.ReadAllBytes($"{result.RemoteDirectory}/{ContentCatalog.FileName}"));

            Assert.That(catalog.RemoteCompression, Is.EqualTo(RemoteCompression.GZip));
            foreach (var pack in catalog.Packs)
            {
                var file = RemoteFile(result, pack);
                Assert.That(new FileInfo(file).Length, Is.EqualTo(pack.Size), file);

                using var input = new System.IO.Compression.GZipStream(File.OpenRead(file), System.IO.Compression.CompressionMode.Decompress);
                using var buffer = new MemoryStream();
                input.CopyTo(buffer);
                var bundle = AssetBundle.LoadFromMemory(buffer.ToArray());
                Assert.That(bundle, Is.Not.Null, file);
                bundle.Unload(true);
            }
        }

        // 안 바뀐 팩은 이전 gzip 결과를 그대로 씀
        [Test]
        public void GZipRebuildReusesUnchangedPacks()
        {
            var first = FixtureBuild.Run(smallPackThresholdKB: 64, ignorePrevious: false, compression: RemoteCompression.GZip);
            var times = first.Catalog.Packs.ToDictionary(p => p.FileName, p => File.GetLastWriteTimeUtc($"{first.WorkDirectory}/gzip/{p.FileName.Replace(".bundle", "")}.gz"));

            var second = FixtureBuild.Run(smallPackThresholdKB: 64, ignorePrevious: false, compression: RemoteCompression.GZip);

            foreach (var pack in second.Catalog.Packs)
                Assert.That(File.GetLastWriteTimeUtc($"{second.WorkDirectory}/gzip/{pack.FileName.Replace(".bundle", "")}.gz"), Is.EqualTo(times[pack.FileName]), pack.FileName);
        }

        [Test]
        public void Lz4RemotePacksAreTheBuiltLz4Bundles()
        {
            var result = FixtureBuild.Run(smallPackThresholdKB: 64, compression: RemoteCompression.LZ4);

            foreach (var pack in result.Catalog.Packs)
            {
                var id = pack.FileName.Substring(0, pack.FileName.IndexOf('_'));
                Assert.That(File.ReadAllBytes(RemoteFile(result, pack)), Is.EqualTo(File.ReadAllBytes($"{result.WorkDirectory}/lz4/{id}")), pack.FileName);
            }
        }
    }
}
