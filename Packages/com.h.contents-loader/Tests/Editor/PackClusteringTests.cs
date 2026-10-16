using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace H.ContentsLoader.Editor.Tests
{
    public class PackClusteringTests
    {
        private readonly Dictionary<string, ClusterNode> nodes = new();
        private readonly List<ClusterRoot> roots = new();
        private readonly ClusterOptions noMerge = new() { SmallPackThreshold = 0 };

        [SetUp]
        public void SetUp()
        {
            nodes.Clear();
            roots.Clear();
        }

        private ClusterNode Node(string key, long size = 100, bool shader = false, params string[] dependencies)
        {
            var node = new ClusterNode { Key = key, Size = size, IsShader = shader };
            node.Dependencies.AddRange(dependencies);
            nodes[key] = node;
            return node;
        }

        private void Root(string key, ContentDelivery delivery = ContentDelivery.BuiltIn, string group = null, params string[] dependencies)
        {
            Node(key, 100, false, dependencies);
            roots.Add(new ClusterRoot { Key = key, Delivery = delivery, Group = group ?? key });
        }

        private ClusterResult Run(ClusterOptions options = null, PackLayout previous = null)
        {
            var result = PackClustering.Run(roots, nodes, options ?? noMerge, previous);
            AssertInvariants(result);
            return result;
        }

        private void AssertInvariants(ClusterResult result)
        {
            var all = result.Layout.Packs.SelectMany(p => p.Members).ToList();
            Assert.That(all, Is.Unique, "모든 애셋은 정확히 한 팩에만 있어야 함");
            Assert.That(result.Layout.Packs.Select(p => p.Id), Is.Unique);

            foreach (var pack in result.Layout.Packs.Where(p => p.Delivery == ContentDelivery.BuiltIn))
            foreach (var member in pack.Members)
            foreach (var dependency in nodes[member].Dependencies.Where(result.PackOf.ContainsKey))
                Assert.That(result.PackOf[dependency].Delivery, Is.EqualTo(ContentDelivery.BuiltIn),
                    $"Built-in 팩 {pack.Id}의 {member}가 Download 팩의 {dependency}에 의존함");
        }

        private static PackLayout Copy(PackLayout layout)
        {
            var copy = new PackLayout();
            foreach (var p in layout.Packs)
                copy.Packs.Add(new PackPlan { Id = p.Id, Delivery = p.Delivery, IsShader = p.IsShader, Members = p.Members.ToList(), Merged = p.Merged.ToList() });
            return copy;
        }

        [Test]
        public void SharedAssetGetsItsOwnPack()
        {
            Node("tex/shared");
            Node("tex/a");
            Node("tex/b");
            Root("A", dependencies: new[] { "tex/a", "tex/shared" });
            Root("B", dependencies: new[] { "tex/b", "tex/shared" });

            var result = Run();

            var shared = result.PackOf["tex/shared"];
            Assert.That(shared.Members, Is.EqualTo(new[] { "tex/shared" }));
            Assert.That(result.PackOf["tex/a"], Is.SameAs(result.PackOf["A"]));
            Assert.That(result.PackOf["tex/a"], Is.Not.SameAs(result.PackOf["tex/b"]));
        }

        [Test]
        public void EachRootNeedsOnlyPacksItReaches()
        {
            Node("tex/shared");
            Node("tex/a");
            Node("tex/b");
            Node("tex/c");
            Root("A", dependencies: new[] { "tex/a", "tex/shared" });
            Root("B", dependencies: new[] { "tex/b", "tex/shared" });
            Root("C", dependencies: new[] { "tex/c" });

            var result = Run();

            foreach (var root in roots)
            {
                var expected = nodes.Keys.Where(k => Reaches(root.Key, k)).Select(k => result.PackOf[k].Id).ToHashSet();
                Assert.That(result.RequiredPacks[root.Key], Is.EquivalentTo(expected), root.Key);
            }
            Assert.That(result.RequiredPacks["A"], Has.Count.EqualTo(2));
            Assert.That(result.RequiredPacks["C"], Has.Count.EqualTo(1));
        }

        private bool Reaches(string from, string to)
        {
            var stack = new Stack<string>(new[] { from });
            var seen = new HashSet<string>();
            while (stack.Count > 0)
            {
                var key = stack.Pop();
                if (key == to) return true;
                if (!seen.Add(key)) continue;
                foreach (var d in nodes[key].Dependencies) stack.Push(d);
            }
            return false;
        }

        [Test]
        public void TransitiveDependenciesFollowTheirReach()
        {
            Node("tex/shared");
            Node("mat/shared", 100, false, "tex/shared");
            Node("mat/a", 100, false, "tex/shared");
            Root("A", dependencies: new[] { "mat/shared", "mat/a" });
            Root("B", dependencies: new[] { "mat/shared" });

            var result = Run();

            Assert.That(result.PackOf["tex/shared"], Is.SameAs(result.PackOf["mat/shared"]));
            Assert.That(result.PackOf["mat/a"], Is.SameAs(result.PackOf["A"]));
        }

        [Test]
        public void SmallPacksMergeIntoTheSmallestSuperset()
        {
            Node("tex/shared", 1000);
            Node("tex/ab", 1000);
            Node("tex/a", 10);
            Root("A", dependencies: new[] { "tex/a", "tex/shared", "tex/ab" });
            Root("B", dependencies: new[] { "tex/shared", "tex/ab" });
            Root("C", dependencies: new[] { "tex/shared" });

            var result = Run(new ClusterOptions { SmallPackThreshold = 500 });

            // {A} 팩(110 B)은 {A,B} 팩으로, {A,B,C} 팩이 아니라 더 작은 상위집합으로 감
            Assert.That(result.PackOf["tex/a"], Is.SameAs(result.PackOf["tex/ab"]));
            Assert.That(result.PackOf["A"], Is.SameAs(result.PackOf["tex/ab"]));
            Assert.That(result.PackOf["tex/shared"], Is.Not.SameAs(result.PackOf["tex/ab"]));
        }

        [Test]
        public void SmallPacksWithoutSupersetStay()
        {
            Node("tex/a", 10);
            Node("tex/b", 10);
            Root("A", dependencies: new[] { "tex/a" });
            Root("B", dependencies: new[] { "tex/b" });

            var result = Run(new ClusterOptions { SmallPackThreshold = 1 << 20 });

            Assert.That(result.Layout.Packs, Has.Count.EqualTo(2));
        }

        [Test]
        public void SmallPacksDoNotCrossDeliveryBoundary()
        {
            Node("tex/shared", 1000);
            Node("tex/dl", 10);
            Root("Core", ContentDelivery.BuiltIn, dependencies: new[] { "tex/shared" });
            Root("Boss", ContentDelivery.Download, dependencies: new[] { "tex/shared", "tex/dl" });

            var result = Run(new ClusterOptions { SmallPackThreshold = 500 });

            Assert.That(result.PackOf["tex/dl"].Delivery, Is.EqualTo(ContentDelivery.Download));
            Assert.That(result.PackOf["tex/shared"].Delivery, Is.EqualTo(ContentDelivery.BuiltIn));
        }

        [Test]
        public void ShadersGoToOneBuiltInPack()
        {
            Node("shader/lit", 10, true);
            Node("shader/fx", 10, true);
            Node("mat/a", 100, false, "shader/lit");
            Node("mat/boss", 100, false, "shader/fx");
            Root("A", ContentDelivery.BuiltIn, dependencies: new[] { "mat/a" });
            Root("Boss", ContentDelivery.Download, dependencies: new[] { "mat/boss" });

            var result = Run();

            var shaders = result.PackOf["shader/lit"];
            Assert.That(shaders.Id, Is.EqualTo(PackClustering.ShaderPackId));
            Assert.That(result.PackOf["shader/fx"], Is.SameAs(shaders));
            Assert.That(shaders.Delivery, Is.EqualTo(ContentDelivery.BuiltIn));
            Assert.That(shaders.Members, Is.EquivalentTo(new[] { "shader/fx", "shader/lit" }));
        }

        [Test]
        public void AssetsSharedWithBuiltInRootsAreBuiltIn()
        {
            Node("tex/shared");
            Node("tex/boss");
            Root("Core", ContentDelivery.BuiltIn, dependencies: new[] { "tex/shared" });
            Root("Boss", ContentDelivery.Download, dependencies: new[] { "tex/shared", "tex/boss" });

            var result = Run();

            Assert.That(result.PackOf["tex/shared"].Delivery, Is.EqualTo(ContentDelivery.BuiltIn));
            Assert.That(result.PackOf["tex/boss"].Delivery, Is.EqualTo(ContentDelivery.Download));
            Assert.That(result.PackOf["Boss"].Delivery, Is.EqualTo(ContentDelivery.Download));
        }

        [Test]
        public void DownloadRootReferencedByBuiltInRootBecomesBuiltIn()
        {
            Node("tex/boss");
            Root("Boss", ContentDelivery.Download, dependencies: new[] { "tex/boss" });
            Root("Menu", ContentDelivery.BuiltIn, dependencies: new[] { "Boss" });

            var result = Run();

            Assert.That(result.PackOf["Boss"].Delivery, Is.EqualTo(ContentDelivery.BuiltIn));
            Assert.That(result.PackOf["tex/boss"].Delivery, Is.EqualTo(ContentDelivery.BuiltIn));
        }

        [Test]
        public void SameInputGivesSameLayout()
        {
            Node("tex/shared");
            Node("tex/a");
            Root("A", dependencies: new[] { "tex/a", "tex/shared" });
            Root("B", dependencies: new[] { "tex/shared" });

            var first = Run();
            var second = Run();

            Assert.That(second.Layout.Packs.Select(p => p.Id), Is.EqualTo(first.Layout.Packs.Select(p => p.Id)));
            Assert.That(second.Layout.Packs.Select(p => string.Join(",", p.Members)),
                Is.EqualTo(first.Layout.Packs.Select(p => string.Join(",", p.Members))));
        }

        [Test]
        public void PreviousLayoutKeepsPacksWhenANewRootSharesAnAsset()
        {
            Node("tex/shared");
            Node("tex/a");
            Node("tex/c");
            Root("A", dependencies: new[] { "tex/a", "tex/shared" });
            Root("B", dependencies: new[] { "tex/shared" });
            var before = Run();

            Root("C", dependencies: new[] { "tex/c", "tex/shared" });
            var after = Run(previous: Copy(before.Layout));

            foreach (var old in before.Layout.Packs)
            {
                var now = after.Layout.Packs.Single(p => p.Id == old.Id);
                Assert.That(now.Members, Is.EqualTo(old.Members), old.Id);
            }
            Assert.That(after.PackOf["tex/c"], Is.SameAs(after.PackOf["C"]));
            Assert.That(after.PackOf["tex/c"].Reused, Is.False);

            var fresh = Run();
            Assert.That(fresh.PackOf["tex/shared"].Id, Is.Not.EqualTo(before.PackOf["tex/shared"].Id),
                "이전 배치가 없으면 시그니처가 바뀐 공유 애셋은 새 팩으로 감");
        }

        [Test]
        public void PreviousLayoutDropsRemovedAssets()
        {
            Node("tex/a");
            Node("tex/old");
            Root("A", dependencies: new[] { "tex/a", "tex/old" });
            var before = Run();

            nodes["A"].Dependencies.Remove("tex/old");
            var after = Run(previous: Copy(before.Layout));

            Assert.That(after.PackOf.ContainsKey("tex/old"), Is.False);
            Assert.That(after.Layout.Packs.SelectMany(p => p.Members), Does.Not.Contain("tex/old"));
        }

        [Test]
        public void PreviousPlacementThatBreaksTheBuiltInRuleIsRedone()
        {
            Node("tex/boss");
            Root("Boss", ContentDelivery.Download, dependencies: new[] { "tex/boss" });
            var before = Run();
            Assert.That(before.PackOf["tex/boss"].Delivery, Is.EqualTo(ContentDelivery.Download));

            Root("Menu", ContentDelivery.BuiltIn, dependencies: new[] { "tex/boss" });
            var after = Run(previous: Copy(before.Layout));

            Assert.That(after.PackOf["tex/boss"].Delivery, Is.EqualTo(ContentDelivery.BuiltIn));
            Assert.That(after.PackOf["Boss"].Id, Is.EqualTo(before.PackOf["Boss"].Id));
        }

        [Test]
        public void GroupSignaturesKeepPacksWhenAFolderGrows()
        {
            Node("tex/shared");
            Node("tex/cannon");
            Node("tex/archer");
            Root("Cannon", group: "Towers", dependencies: new[] { "tex/cannon", "tex/shared" });
            Root("Boss", group: "Bosses", dependencies: new[] { "tex/shared" });
            var options = new ClusterOptions { SmallPackThreshold = 0, SignatureByGroup = true };
            var before = Run(options);

            Root("Archer", group: "Towers", dependencies: new[] { "tex/archer", "tex/shared" });
            var after = Run(options);

            Assert.That(after.PackOf["tex/shared"].Id, Is.EqualTo(before.PackOf["tex/shared"].Id));
            Assert.That(after.PackOf["tex/shared"].Members, Is.EqualTo(before.PackOf["tex/shared"].Members));
            Assert.That(after.PackOf["Archer"], Is.SameAs(after.PackOf["Cannon"]));
        }

        // B가 tex/x를 더는 안 써도 x는 {A,B} 팩에 남음. B가 그 팩을 열 때마다 x까지 올라오는 낭비가 생김
        [Test]
        public void ReusedPackKeepsSmallDrift()
        {
            Node("tex/shared", 1000);
            Node("tex/x", 100);
            Root("A", dependencies: new[] { "tex/shared", "tex/x" });
            Root("B", dependencies: new[] { "tex/shared", "tex/x" });
            var before = Run();

            nodes["B"].Dependencies.Remove("tex/x");
            var after = Run(previous: Copy(before.Layout));

            Assert.That(after.PackOf["tex/x"], Is.SameAs(after.PackOf["tex/shared"]), "낭비가 작으면 패치 크기를 위해 그대로 둠");
            Assert.That(after.PackOf["tex/shared"].Repacked, Is.False);
            Assert.That(after.PackOf["tex/shared"].WasteBytes, Is.EqualTo(100), "B가 안 쓰는 x 100 바이트");
        }

        [Test]
        public void ReusedPackSplitsWhenDriftWastesTooMuch()
        {
            Node("tex/shared", 1000);
            Node("tex/x", 2000);
            Root("A", dependencies: new[] { "tex/shared", "tex/x" });
            Root("B", dependencies: new[] { "tex/shared", "tex/x" });
            var before = Run();
            var oldId = before.PackOf["tex/shared"].Id;

            nodes["B"].Dependencies.Remove("tex/x");
            var after = Run(previous: Copy(before.Layout));

            // 낭비 2000 / (사용 루트 2 x 팩 3000) = 33% > 25%
            Assert.That(after.PackOf["tex/x"], Is.Not.SameAs(after.PackOf["tex/shared"]));
            Assert.That(after.PackOf["tex/x"].Id, Is.EqualTo(oldId), "바이트가 많은 쪽이 원래 팩 ID를 유지함");
            Assert.That(after.PackOf["tex/x"].Repacked, Is.True);
            Assert.That(after.Layout.Packs.Sum(p => p.WasteBytes), Is.EqualTo(0));
        }

        // 병합은 일부러 낭비를 감수한 것이라, 다음 빌드에서 드리프트로 보고 다시 쪼개면 안 됨
        [Test]
        public void IntentionallyMergedPacksStayOnRebuild()
        {
            Node("tex/shared", 1000);
            Node("tex/ab", 1000);
            Node("tex/a", 10);
            Root("A", dependencies: new[] { "tex/a", "tex/shared", "tex/ab" });
            Root("B", dependencies: new[] { "tex/shared", "tex/ab" });
            Root("C", dependencies: new[] { "tex/shared" });
            var options = new ClusterOptions { SmallPackThreshold = 500, MaxReuseWaste = 0.01f };
            var before = Run(options);
            Assert.That(before.PackOf["tex/a"], Is.SameAs(before.PackOf["tex/ab"]), "전제: {A} 팩이 {A,B} 팩에 병합됨");

            var after = Run(options, Copy(before.Layout));

            Assert.That(after.Layout.Packs.Select(p => p.Id + ":" + string.Join(",", p.Members)),
                Is.EqualTo(before.Layout.Packs.Select(p => p.Id + ":" + string.Join(",", p.Members))));
            Assert.That(after.Layout.Packs.Any(p => p.Repacked), Is.False);
        }

        [Test]
        public void PackIdDoesNotDependOnDelivery()
        {
            Node("tex/boss");
            Root("Boss", ContentDelivery.Download, dependencies: new[] { "tex/boss" });
            var remote = Run();

            roots.Clear();
            Root("Boss", ContentDelivery.BuiltIn, dependencies: new[] { "tex/boss" });
            var builtIn = Run();

            // 내용이 같으면 파일 이름이 같아서, Delivery만 바꾼 패치는 다시 받지 않음
            Assert.That(builtIn.PackOf["tex/boss"].Id, Is.EqualTo(remote.PackOf["tex/boss"].Id));
            Assert.That(builtIn.PackOf["tex/boss"].Delivery, Is.EqualTo(ContentDelivery.BuiltIn));
        }
    }
}
