using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace H.ContentsLoader.Editor.Tests
{
    public class DeliveryTests
    {
        const string Root = "Assets/Game/Contents";

        static ContentDelivery Resolve(string assetPath, Dictionary<string, ContentDelivery> rules)
        {
            return DeliveryResolver.Resolve(assetPath, Root,
                folder => rules.TryGetValue(folder, out var d) ? d : (ContentDelivery?)null);
        }

        [Test]
        public void DefaultsToBuiltIn()
        {
            Assert.That(Resolve($"{Root}/Towers/Cannon.prefab", new()), Is.EqualTo(ContentDelivery.BuiltIn));
        }

        [Test]
        public void SubfoldersInheritTheNearestRule()
        {
            var rules = new Dictionary<string, ContentDelivery> { [$"{Root}/Bosses"] = ContentDelivery.Download };

            Assert.That(Resolve($"{Root}/Bosses/Fire/Dragon.prefab", rules), Is.EqualTo(ContentDelivery.Download));
            Assert.That(Resolve($"{Root}/Towers/Cannon.prefab", rules), Is.EqualTo(ContentDelivery.BuiltIn));
        }

        [Test]
        public void SubfolderRuleOverridesParent()
        {
            var rules = new Dictionary<string, ContentDelivery>
            {
                [Root] = ContentDelivery.Download,
                [$"{Root}/Core"] = ContentDelivery.BuiltIn,
            };

            Assert.That(Resolve($"{Root}/Core/Ui.prefab", rules), Is.EqualTo(ContentDelivery.BuiltIn));
            Assert.That(Resolve($"{Root}/Extra/Skin.prefab", rules), Is.EqualTo(ContentDelivery.Download));
        }

        [Test]
        public void RulesAboveTheContentsRootAreIgnored()
        {
            var rules = new Dictionary<string, ContentDelivery> { ["Assets/Game"] = ContentDelivery.Download };

            Assert.That(Resolve($"{Root}/Towers/Cannon.prefab", rules), Is.EqualTo(ContentDelivery.BuiltIn));
        }

        [Test]
        public void RuleSurvivesFolderMoveAndRename()
        {
            const string temp = "Assets/__ContentsLoaderDeliveryTest";
            var settings = ScriptableObject.CreateInstance<ContentsSettings>();
            try
            {
                AssetDatabase.CreateFolder("Assets", "__ContentsLoaderDeliveryTest");
                AssetDatabase.CreateFolder(temp, "Contents");
                AssetDatabase.CreateFolder($"{temp}/Contents", "Bosses");
                var guid = AssetDatabase.AssetPathToGUID($"{temp}/Contents/Bosses");
                settings.SetRule(guid, ContentDelivery.Download);

                AssetDatabase.CreateFolder($"{temp}/Contents", "Season01");
                var error = AssetDatabase.MoveAsset($"{temp}/Contents/Bosses", $"{temp}/Contents/Season01/Monsters");
                Assert.That(error, Is.Empty);

                var entry = new ContentsEntry
                {
                    AssetPath = $"{temp}/Contents/Season01/Monsters/Dragon.prefab",
                    RootFolder = $"{temp}/Contents",
                };
                Assert.That(settings.ResolveDelivery(entry), Is.EqualTo(ContentDelivery.Download));
            }
            finally
            {
                AssetDatabase.DeleteAsset(temp);
                Object.DestroyImmediate(settings);
            }
        }
    }
}
