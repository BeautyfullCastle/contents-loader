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

    public sealed class ContentsSettings : ScriptableObject
    {
        public const string DefaultPath = "Assets/ContentsSettings.asset";

        [Serializable]
        internal struct FolderRule
        {
            public string Guid;
            public ContentDelivery Delivery;
        }

        [SerializeField] List<FolderRule> folderRules = new();

        [Tooltip("Base URL for Download packs. The build target name replaces {Platform}.")]
        public string RemoteUrl = "";

        [Tooltip("Cached assets are evicted, oldest first, while total loaded memory exceeds this value. 0 means no limit.")]
        public int MemoryBudgetMB;

        [Tooltip("Packs smaller than this value merge into a pack that serves a superset of their roots.")]
        public int SmallPackThresholdKB = 64;

        [Tooltip("Compute shared-asset signatures per folder instead of per asset. Adding an asset to a folder then keeps other packs unchanged.")]
        public bool SignatureByFolder;

        [Tooltip("Ignore the previous build layout and place every asset again.")]
        public bool IgnorePreviousLayout;

        public PlayModeSource PlayModeSource = PlayModeSource.AssetDatabase;

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
                folder => GetRule(AssetDatabase.AssetPathToGUID(folder)));
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

        // 설정 애셋이 없어도 빌드와 에디터 로드가 동작하게 기본값을 씀
        internal static ContentsSettings FindOrDefault() => Find() ?? CreateInstance<ContentsSettings>();
    }
}
