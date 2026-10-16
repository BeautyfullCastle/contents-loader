using System.Linq;
using NUnit.Framework;

namespace H.ContentsLoader.Editor.Tests
{
    public class ContentCatalogTests
    {
        private static ContentCatalog Sample()
        {
            var catalog = new ContentCatalog
            {
                BuildTimestamp = 42,
                RemoteUrl = "https://cdn.example.com/game",
                MemoryBudget = 1024,
                Packs = new[]
                {
                    new ContentCatalog.Pack { FileName = "a.bundle", Delivery = ContentDelivery.BuiltIn, Size = 10, Dependencies = new int[0] },
                    new ContentCatalog.Pack { FileName = "b.bundle", Delivery = ContentDelivery.Download, Size = 20, Dependencies = new[] { 0 } },
                },
                Entries = new[]
                {
                    new ContentCatalog.Entry { PathHash = ContentPath.Hash("Towers/Cannon"), Pack = 0, AssetName = "Assets/C/Towers/Cannon.prefab" },
                    new ContentCatalog.Entry { PathHash = ContentPath.Hash("Bosses/Dragon"), Pack = 1, AssetName = "Assets/C/Bosses/Dragon.prefab", Delivery = ContentDelivery.Download },
                },
                Groups = new[]
                {
                    new ContentCatalog.Group { FolderHash = ContentPath.Hash("Bosses"), Packs = new[] { 0, 1 } },
                },
            };
            catalog.Sort();
            return catalog;
        }

        [Test]
        public void RoundTripsThroughBytes()
        {
            var copy = ContentCatalog.FromBytes(Sample().ToBytes());

            Assert.That(copy.BuildTimestamp, Is.EqualTo(42));
            Assert.That(copy.RemoteUrl, Is.EqualTo("https://cdn.example.com/game"));
            Assert.That(copy.MemoryBudget, Is.EqualTo(1024));
            Assert.That(copy.Packs[1].Dependencies, Is.EqualTo(new[] { 0 }));
            Assert.That(copy.TryFindEntry("Bosses/Dragon", null, out var dragon));
            Assert.That(dragon.Pack, Is.EqualTo(1));
            Assert.That(dragon.Delivery, Is.EqualTo(ContentDelivery.Download));
            Assert.That(copy.TryFindGroup("Bosses", out var group));
            Assert.That(group.Packs, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void LookupIgnoresCaseAndSlashes()
        {
            var catalog = Sample();

            Assert.That(catalog.TryFindEntry("towers/cannon", null, out _));
            Assert.That(catalog.TryFindEntry("/Towers\\Cannon/", null, out _));
            Assert.That(catalog.TryFindEntry("Towers/Archer", null, out _), Is.False);
        }

        [Test]
        public void SharedPathPicksTheEntryHoldingTheRequestedType()
        {
            var hash = ContentPath.Hash("Towers/Cannon");
            var catalog = new ContentCatalog
            {
                TypeNames = new[] { "UnityEngine.GameObject", "UnityEngine.Object", "UnityEngine.Texture2D", "UnityEngine.Texture" },
                Entries = new[]
                {
                    new ContentCatalog.Entry { PathHash = hash, Pack = 0, AssetName = "b.prefab", Types = new[] { 0, 1 } },
                    new ContentCatalog.Entry { PathHash = hash, Pack = 1, AssetName = "a.png", Types = new[] { 1, 2, 3 } },
                    new ContentCatalog.Entry { PathHash = ContentPath.Hash("Other"), Pack = 2, AssetName = "c.txt" },
                },
            };
            catalog.Sort();
            catalog = ContentCatalog.FromBytes(catalog.ToBytes());

            Assert.That(catalog.FindEntries("Towers/Cannon").Count(), Is.EqualTo(2));
            Assert.That(catalog.TryFindEntry("Towers/Cannon", typeof(UnityEngine.GameObject), out var prefab));
            Assert.That(prefab.AssetName, Is.EqualTo("b.prefab"));
            Assert.That(catalog.TryFindEntry("Towers/Cannon", typeof(UnityEngine.Texture), out var texture));
            Assert.That(texture.AssetName, Is.EqualTo("a.png"));
            Assert.That(catalog.TryFindEntry("Towers/Cannon", typeof(UnityEngine.Object), out var first));
            Assert.That(first.AssetName, Is.EqualTo("a.png"), "여럿이 맞으면 애셋 경로 순서상 첫 번째");
            Assert.That(catalog.TryFindEntry("Towers/Cannon", typeof(UnityEngine.AudioClip), out var none));
            Assert.That(none.AssetName, Is.EqualTo("a.png"), "맞는 타입이 없으면 첫 번째를 줌");
        }

        [Test]
        public void RejectsForeignBytes()
        {
            Assert.Throws<System.IO.InvalidDataException>(() => ContentCatalog.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        }
    }
}
