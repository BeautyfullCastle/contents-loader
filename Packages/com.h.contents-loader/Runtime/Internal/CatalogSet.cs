using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace H.ContentsLoader
{
    // 원격 카탈로그가 있으면 먼저 찾고, 없는 경로는 내장 카탈로그에서 찾음
    internal sealed class CatalogSet
    {
        readonly string remotePath;
        ContentCatalog remote;

        public CatalogSet(string builtInRoot, string cacheRoot)
        {
            BuiltIn = ReadBuiltIn(Path.Combine(builtInRoot, ContentCatalog.FileName));
            remotePath = Path.Combine(cacheRoot, ContentCatalog.FileName);
            remote = ReadCachedRemote();
        }

        public ContentCatalog BuiltIn { get; }
        public ContentCatalog Current => remote ?? BuiltIn;

        IEnumerable<ContentCatalog> Layers
        {
            get
            {
                if (remote != null) yield return remote;
                yield return BuiltIn;
            }
        }

        // 첫 원소가 애셋이 들어 있는 팩임
        public bool TryResolve(string path, out ContentCatalog.Entry entry, out ContentCatalog.Pack[] packs)
        {
            foreach (var layer in Layers)
            {
                if (!layer.TryFindEntry(path, out entry)) continue;
                var main = layer.Packs[entry.Pack];
                packs = main.Dependencies.Select(i => layer.Packs[i]).Prepend(main).ToArray();
                return true;
            }
            entry = default;
            packs = null;
            return false;
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
            remote = catalog;
            return true;
        }

        ContentCatalog ReadCachedRemote()
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

        static ContentCatalog ReadBuiltIn(string path)
        {
            byte[] bytes;
            if (path.Contains("://"))
            {
                // Android의 StreamingAssets는 APK 안에 있어서 File API로 못 읽음
                using var request = UnityWebRequest.Get(path);
                var operation = request.SendWebRequest();
                while (!operation.isDone) { }
                bytes = request.result == UnityWebRequest.Result.Success ? request.downloadHandler.data : null;
            }
            else
            {
                bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
            }

            if (bytes != null) return ContentCatalog.FromBytes(bytes);
            Debug.LogWarning($"[Contents Loader] 카탈로그가 없습니다: {path}. 콘텐츠를 빌드했는지 확인하세요.");
            return new ContentCatalog();
        }
    }
}
