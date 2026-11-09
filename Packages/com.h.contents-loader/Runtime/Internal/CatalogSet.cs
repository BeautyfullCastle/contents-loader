using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;

namespace H.ContentsLoader
{
    // 원격 카탈로그가 있으면 먼저 찾고, 없는 경로는 내장 카탈로그에서 찾음
    internal sealed class CatalogSet
    {
        private readonly string remotePath;
        private ContentCatalog remote;

        public CatalogSet(string builtInRoot, string cacheRoot)
            : this(ReadBuiltIn(Path.Combine(builtInRoot, ContentCatalog.FileName)), cacheRoot)
        {
        }

        // 내장 카탈로그를 이미 읽은 경우. WebGL은 웹 주소에서 받아 넘김
        public CatalogSet(ContentCatalog builtIn, string cacheRoot)
        {
            BuiltIn = builtIn;
            remotePath = Path.Combine(cacheRoot, ContentCatalog.FileName);
            remote = ReadCachedRemote();
        }

        public ContentCatalog BuiltIn { get; }
        public ContentCatalog Current => remote ?? BuiltIn;

        private IEnumerable<ContentCatalog> Layers
        {
            get
            {
                if (remote != null) yield return remote;
                yield return BuiltIn;
            }
        }

        // 첫 원소가 애셋이 들어 있는 팩임
        public bool TryResolve(string path, Type type, out ContentCatalog.Entry entry, out ContentCatalog.Pack[] packs)
        {
            foreach (var layer in Layers)
            {
                if (!layer.TryFindEntry(path, type, out entry)) continue;
                packs = PacksOf(layer, entry);
                return true;
            }
            entry = default;
            packs = null;
            return false;
        }

        // 경로에 걸린 애셋 전부의 팩. 경로가 없으면 빈 목록임
        public IEnumerable<ContentCatalog.Pack> PacksOfPath(string path)
        {
            foreach (var layer in Layers)
            {
                var entries = layer.FindEntries(path).ToList();
                if (entries.Count > 0) return entries.SelectMany(e => PacksOf(layer, e));
            }
            return Enumerable.Empty<ContentCatalog.Pack>();
        }

        private static ContentCatalog.Pack[] PacksOf(ContentCatalog layer, ContentCatalog.Entry entry)
        {
            var main = layer.Packs[entry.Pack];
            return main.Dependencies.Select(i => layer.Packs[i]).Prepend(main).ToArray();
        }

        public IEnumerable<ContentCatalog.Pack> PacksOfFolder(string folder)
        {
            folder = ContentPath.Normalize(folder);
            if (folder.Length == 0) return Current.Packs;

            foreach (var layer in Layers)
                if (layer.TryFindGroup(folder, out var group))
                    return group.Packs.Select(i => layer.Packs[i]);

            throw new ArgumentException($"Contents 폴더 '{folder}'가 없습니다.", nameof(folder));
        }

        public bool ApplyRemote(byte[] bytes)
        {
            var catalog = ContentCatalog.FromBytes(bytes);
            if (catalog.BuildTimestamp <= Current.BuildTimestamp) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(remotePath)!);
            File.WriteAllBytes(remotePath, bytes);
            CacheFiles.ExcludeFromBackup(remotePath);
            remote = catalog;
            return true;
        }

        private ContentCatalog ReadCachedRemote()
        {
            if (!File.Exists(remotePath)) return null;
            try
            {
                var catalog = ContentCatalog.FromBytes(File.ReadAllBytes(remotePath));
                if (catalog.BuildTimestamp > BuiltIn.BuildTimestamp) return catalog;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException)
            {
                Debug.LogWarning($"[Contents Loader] 저장된 원격 카탈로그를 버립니다: {e.Message}");
            }
            // 앱 업데이트로 내장 카탈로그가 더 새로우면 예전 패치는 버림
            File.Delete(remotePath);
            return null;
        }

        private static ContentCatalog ReadBuiltIn(string path)
        {
            var bytes = path.Contains("!/") ? ReadFromArchive(path) : File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (bytes != null) return ContentCatalog.FromBytes(bytes);
            Debug.LogWarning($"[Contents Loader] 카탈로그가 없습니다: {path}. 콘텐츠를 빌드했는지 확인하세요.");
            return new ContentCatalog();
        }

        // Android의 StreamingAssets는 APK(zip) 안에 있어서 File API로 못 읽음
        // UnityWebRequest는 비동기라 동기 Load를 위해 기다리면 메인 스레드가 막힘. zip 목차만 읽고 항목 하나를 바로 꺼냄
        // 형식: jar:file:///data/app/.../base.apk!/assets/ContentsLoader/catalog.bin
        internal static byte[] ReadFromArchive(string url)
        {
            var bang = url.IndexOf("!/", StringComparison.Ordinal);
            var archive = url.Substring(0, bang);
            var scheme = archive.IndexOf("file://", StringComparison.Ordinal);
            if (scheme >= 0) archive = archive.Substring(scheme + "file://".Length);
            // Path.Combine이 붙인 구분자가 섞여도 zip 항목 이름은 항상 '/'임
            var entryName = url.Substring(bang + 2).Replace('\\', '/');
            if (!File.Exists(archive)) return null;

            using var stream = File.OpenRead(archive);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = zip.GetEntry(entryName);
            if (entry == null) return null;
            using var input = entry.Open();
            using var output = new MemoryStream((int)entry.Length);
            input.CopyTo(output);
            return output.ToArray();
        }
    }
}
