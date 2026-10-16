using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace H.ContentsLoader.Editor
{
    public enum PlayModeSource
    {
        /// <summary>Loads directly from the AssetDatabase without building.</summary>
        AssetDatabase = 0,

        /// <summary>Loads from the last bundle build, like a player does.</summary>
        Bundles = 1,
    }

    public enum EditorDownloadSource
    {
        /// <summary>Copies Download packs from the local Remote build folder at once.</summary>
        LocalFolder = 0,

        /// <summary>Serves the local Remote build folder over HTTP on localhost, with Range requests and an optional speed limit.</summary>
        LocalServer = 1,

        /// <summary>Downloads from RemoteUrl, the real server. Upload the play mode build there first.</summary>
        RemoteUrl = 2,
    }

    public sealed class ContentsSettings : ScriptableObject
    {
        public const string DefaultPath = "Assets/ContentsSettings.asset";

        [Serializable]
        internal struct FolderRule
        {
            public string Guid;
            public ContentDelivery Delivery;
        }

        // 폴더 인스펙터나 Contents Loader 창에서 편집함
        [SerializeField, HideInInspector] private List<FolderRule> folderRules = new();

        internal IReadOnlyList<FolderRule> FolderRules => folderRules;

        [Tooltip("Delivery for every Contents folder without its own rule. Folder rules in the folder inspector override it.")]
        public ContentDelivery DefaultDelivery = ContentDelivery.BuiltIn;

        [Tooltip("Base URL for Download packs. The build target name replaces {Platform}.")]
        public string RemoteUrl = "";

        [Tooltip("Compression of packs uploaded to RemoteUrl. LZMA is smallest but slowest to build; GZip and LZ4 build faster with larger downloads. Players follow the format in the server catalog, but apps built before this setting existed only request LZMA files.")]
        public RemoteCompression RemoteCompression = RemoteCompression.LZMA;

        [Tooltip("Cached assets are evicted, oldest first, while open pack bytes exceed this value. Packs shared by several assets count once. 0 means no limit.")]
        public int MemoryBudgetMB;

        [Tooltip("Packs smaller than this value merge into a pack that serves a superset of their roots.")]
        public int SmallPackThresholdKB = 64;

        [Tooltip("Compute shared-asset signatures per folder instead of per asset. Adding an asset to a folder then keeps other packs unchanged.")]
        public bool SignatureByFolder;

        [Tooltip("Ignore the previous build layout and place every asset again.")]
        public bool IgnorePreviousLayout;

        [Tooltip("A reused pack is split again when this share of its loaded bytes is unused by the roots that open it. Lower keeps memory tighter, higher keeps patches smaller.")]
        [Range(0, 100)]
        public int RepackWastePercent = 25;

        public PlayModeSource PlayModeSource = PlayModeSource.AssetDatabase;

        [Tooltip("Bundles play mode only. Where Download packs and the remote catalog come from.")]
        public EditorDownloadSource EditorDownloadSource = EditorDownloadSource.LocalFolder;

        [Tooltip("LocalServer only. Limits the download speed to mimic a mobile network. 0 means no limit.")]
        public int EditorDownloadSpeedKBps;

        [Tooltip("Build content and copy built-in packs into the player during a player build.")]
        public bool BuildWithPlayer = true;

        [Tooltip("Generate ContentPaths constants on each content build.")]
        public bool GeneratePathConstants;

        public string PathConstantsFile = "Assets/ContentsLoader.Generated/ContentPaths.cs";

        internal ContentDelivery? GetRule(string folderGuid)
        {
            foreach (var rule in folderRules)
                if (rule.Guid == folderGuid) return rule.Delivery;
            return null;
        }

        /// <summary>Sets how content under a folder is delivered. Null makes the folder inherit from its parent.</summary>
        /// <param name="folderPath">A folder path such as "Assets/DLC/Contents".</param>
        public void SetDelivery(string folderPath, ContentDelivery? delivery)
        {
            var guid = AssetDatabase.AssetPathToGUID(folderPath);
            if (string.IsNullOrEmpty(guid) || !AssetDatabase.IsValidFolder(folderPath))
                throw new ArgumentException($"'{folderPath}'은(는) 프로젝트 폴더가 아닙니다.", nameof(folderPath));
            SetRule(guid, delivery);
        }

        internal void SetRule(string folderGuid, ContentDelivery? delivery)
        {
            folderRules.RemoveAll(r => r.Guid == folderGuid);
            if (delivery.HasValue)
                folderRules.Add(new FolderRule { Guid = folderGuid, Delivery = delivery.Value });
            EditorUtility.SetDirty(this);
        }

        internal ContentDelivery ResolveDelivery(ContentsEntry entry)
        {
            return DeliveryResolver.Resolve(entry.AssetPath, entry.RootFolder,
                folder => GetRule(AssetDatabase.AssetPathToGUID(folder)), DefaultDelivery);
        }

        internal long MemoryBudgetBytes => MemoryBudgetMB > 0 ? MemoryBudgetMB * 1024L * 1024L : long.MaxValue;

        public static ContentsSettings Find()
        {
            var guid = AssetDatabase.FindAssets($"t:{nameof(ContentsSettings)}").FirstOrDefault();
            return guid == null ? null : AssetDatabase.LoadAssetAtPath<ContentsSettings>(AssetDatabase.GUIDToAssetPath(guid));
        }

        public static ContentsSettings GetOrCreate()
        {
            var settings = Find();
            if (settings != null) return settings;

            settings = CreateInstance<ContentsSettings>();
            AssetDatabase.CreateAsset(settings, DefaultPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private static ContentsSettings defaults;

        // 설정 애셋이 없어도 빌드와 에디터 로드가 동작하게 기본값을 씀
        internal static ContentsSettings FindOrDefault()
        {
            var settings = Find();
            if (settings != null) return settings;
            if (defaults == null) defaults = CreateInstance<ContentsSettings>();
            defaults.hideFlags = HideFlags.DontSave;
            return defaults;
        }
    }
}
