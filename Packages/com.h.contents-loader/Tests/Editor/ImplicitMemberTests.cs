using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace H.ContentsLoader.Editor.Tests
{
    // 명시 배정을 빼서 Unity가 참조된 하위 오브젝트만 넣게 할 의존 애셋 고르기
    public class ImplicitMemberTests
    {
        private readonly Dictionary<string, ClusterNode> nodes = new();
        private readonly List<ClusterRoot> roots = new();

        private void Node(string key, params string[] dependencies)
        {
            var node = new ClusterNode { Key = key, Size = 100 };
            node.Dependencies.AddRange(dependencies);
            nodes[key] = node;
        }

        private void Root(string key, params string[] dependencies)
        {
            Node(key, dependencies);
            roots.Add(new ClusterRoot { Key = key, Group = key, Delivery = ContentDelivery.BuiltIn });
        }

        private HashSet<string> Select(out ClusterResult clusters)
        {
            clusters = PackClustering.Run(roots, nodes, new ClusterOptions { SmallPackThreshold = 0 });
            return ContentsBuilder.SelectImplicitMembers(clusters, nodes, roots.Select(r => r.Key));
        }

        [SetUp]
        public void SetUp()
        {
            nodes.Clear();
            roots.Clear();
        }

        [Test]
        public void DependencyReferencedOnlyInsideItsPackIsImplicit()
        {
            Node("model/tower.fbx");
            Root("Tower", "model/tower.fbx");

            var implicitMembers = Select(out _);

            Assert.That(implicitMembers, Is.EquivalentTo(new[] { "model/tower.fbx" }));
        }

        [Test]
        public void DependencyReferencedFromAnotherPackStaysExplicit()
        {
            // mat/shared는 A, B 공유 팩에 있고 A 팩과 B 팩의 루트가 직접 참조함 → 배정을 빼면 두 번들에 복제됨
            Node("tex/shared");
            Node("mat/shared", "tex/shared");
            Root("A", "mat/shared");
            Root("B", "mat/shared");

            var implicitMembers = Select(out var clusters);

            Assert.That(implicitMembers, Does.Not.Contain("mat/shared"));
            Assert.That(implicitMembers, Does.Contain("tex/shared"), "tex/shared는 같은 팩의 mat/shared만 참조함");
            Assert.That(clusters.PackOf["tex/shared"], Is.SameAs(clusters.PackOf["mat/shared"]));
        }

        [Test]
        public void RootsAndShadersAreAlwaysExplicit()
        {
            nodes["shader/lit"] = new ClusterNode { Key = "shader/lit", Size = 10, IsShader = true };
            Node("mat/a", "shader/lit");
            Root("Icon");
            Root("Menu", "Icon", "mat/a");

            var implicitMembers = Select(out _);

            Assert.That(implicitMembers, Does.Not.Contain("Icon"), "다른 루트가 참조해도 루트는 이름으로 로드되므로 배정해야 함");
            Assert.That(implicitMembers, Does.Not.Contain("shader/lit"));
            Assert.That(implicitMembers, Does.Contain("mat/a"));
        }
    }
}
