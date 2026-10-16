using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using UnityEngine;

namespace H.ContentsLoader.Editor.Tests
{
    // Android StreamingAssets 경로(jar:file://...apk!/assets/...)를 바쁜 대기 없이 읽는지 확인
    public class CatalogArchiveTests
    {
        private string archive;

        [SetUp]
        public void SetUp()
        {
            archive = Path.Combine(Application.temporaryCachePath, $"catalog-archive-{System.Guid.NewGuid():N}.apk");
            using var zip = ZipFile(archive);
            Write(zip, "assets/ContentsLoader/catalog.bin", new ContentCatalog { BuildTimestamp = 42, RemoteUrl = "https://example.invalid" }.ToBytes());
            Write(zip, "assets/Other/file.txt", new byte[] { 1, 2, 3 });
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(archive)) File.Delete(archive);
        }

        private static ZipArchive ZipFile(string path) => new(File.Create(path), ZipArchiveMode.Create);

        private static void Write(ZipArchive zip, string name, byte[] bytes)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(bytes, 0, bytes.Length);
        }

        private string Url(string entry) => $"jar:file://{archive.Replace('\\', '/')}!/{entry}";

        [Test]
        public void ReadsEntryInsideArchive()
        {
            var bytes = CatalogSet.ReadFromArchive(Url("assets/ContentsLoader/catalog.bin"));
            var catalog = ContentCatalog.FromBytes(bytes);

            Assert.That(catalog.BuildTimestamp, Is.EqualTo(42));
            Assert.That(catalog.RemoteUrl, Is.EqualTo("https://example.invalid"));
        }

        [Test]
        public void MissingEntryOrArchiveReturnsNull()
        {
            Assert.That(CatalogSet.ReadFromArchive(Url("assets/ContentsLoader/missing.bin")), Is.Null);
            Assert.That(CatalogSet.ReadFromArchive("jar:file:///no/such.apk!/assets/ContentsLoader/catalog.bin"), Is.Null);
        }

        [Test]
        public void BuiltInCatalogLoadsFromArchivePath()
        {
            var set = new CatalogSet(Url("assets/ContentsLoader"), Path.Combine(Application.temporaryCachePath, "catalog-archive-cache"));

            Assert.That(set.BuiltIn.BuildTimestamp, Is.EqualTo(42));
        }
    }
}
