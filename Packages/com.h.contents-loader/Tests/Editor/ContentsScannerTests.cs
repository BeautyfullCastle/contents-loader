using System.Linq;
using NUnit.Framework;

namespace H.ContentsLoader.Editor.Tests
{
    public class ContentsScannerTests
    {
        [Test]
        public void MergesEveryContentsFolderByRelativePath()
        {
            var result = ContentsScanner.Scan(new[]
            {
                "Assets/Game/Contents/Towers/Cannon.prefab",
                "Assets/DLC/Contents/Bosses/Dragon.prefab",
                "Assets/Contents/Config.txt",
                "Assets/Art/NotContent.png",
            });

            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Entries.Select(e => e.Path),
                Is.EquivalentTo(new[] { "Towers/Cannon", "Bosses/Dragon", "Config" }));
            var cannon = result.Entries.Single(e => e.Path == "Towers/Cannon");
            Assert.That(cannon.RootFolder, Is.EqualTo("Assets/Game/Contents"));
            Assert.That(cannon.AssetPath, Is.EqualTo("Assets/Game/Contents/Towers/Cannon.prefab"));
        }

        [Test]
        public void SamePathInTwoContentsFoldersIsAnErrorEvenWithAnotherExtension()
        {
            var result = ContentsScanner.Scan(new[]
            {
                "Assets/A/Contents/Towers/Cannon.prefab",
                "Assets/B/Contents/towers/cannon.png",
            });

            Assert.That(result.Errors, Has.Count.EqualTo(1));
            StringAssert.Contains("Towers/Cannon", result.Errors[0]);
        }

        [Test]
        public void SamePathWithAnotherExtensionInOneContentsFolderIsAllowed()
        {
            var result = ContentsScanner.Scan(new[]
            {
                "Assets/A/Contents/Towers/Cannon.prefab",
                "Assets/A/Contents/Towers/Cannon.png",
            });

            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Entries.Select(e => e.AssetPath),
                Is.EqualTo(new[] { "Assets/A/Contents/Towers/Cannon.png", "Assets/A/Contents/Towers/Cannon.prefab" }));
            Assert.That(result.Entries.Select(e => e.Path), Is.All.EqualTo("Towers/Cannon"));
        }

        [Test]
        public void IgnoresScriptsScenesAndPathsOutsideAssets()
        {
            var result = ContentsScanner.Scan(new[]
            {
                "Assets/Contents/Logic.cs",
                "Assets/Contents/Code.asmdef",
                "Assets/Contents/Level.unity",
                "Packages/com.x/Contents/Thing.prefab",
            });

            Assert.That(result.Entries, Is.Empty);
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
        }

        [Test]
        public void OutermostContentsFolderIsTheRoot()
        {
            Assert.That(ContentsScanner.TryGetContentPath("Assets/Contents/A/Contents/B.prefab", out var root, out var path));
            Assert.That(root, Is.EqualTo("Assets/Contents"));
            Assert.That(path, Is.EqualTo("A/Contents/B"));
        }

        [Test]
        public void KeepsDotsInFolderNames()
        {
            Assert.That(ContentsScanner.TryGetContentPath("Assets/Contents/v1.2/Readme", out _, out var path));
            Assert.That(path, Is.EqualTo("v1.2/Readme"));
        }

        [Test]
        public void DetectsFoldersInsideContents()
        {
            Assert.That(ContentsScanner.TryGetContentsFolder("Assets/Game/Contents", out var root), Is.True);
            Assert.That(root, Is.EqualTo("Assets/Game/Contents"));
            Assert.That(ContentsScanner.TryGetContentsFolder("Assets/Game/Contents/Towers", out _), Is.True);
            Assert.That(ContentsScanner.TryGetContentsFolder("Assets/Game", out _), Is.False);
        }
    }
}
