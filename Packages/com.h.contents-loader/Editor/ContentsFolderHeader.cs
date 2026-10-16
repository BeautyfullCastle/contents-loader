using UnityEditor;
using UnityEngine;

namespace H.ContentsLoader.Editor
{
    [InitializeOnLoad]
    internal static class ContentsFolderHeader
    {
        private static readonly GUIContent Label = new("Delivery", "Built-in packs ship with the app. Download packs are fetched on first use.");

        private static ContentsSettings cached;
        private static bool resolved;

        static ContentsFolderHeader()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
            EditorApplication.projectChanged += () => resolved = false;
        }

        // 헤더는 repaint마다 그려져서 FindAssets를 매번 부르면 느림
        private static ContentsSettings Settings
        {
            get
            {
                if (resolved) return cached;
                cached = ContentsSettings.Find();
                resolved = true;
                return cached;
            }
        }

        private static void OnHeaderGUI(UnityEditor.Editor editor)
        {
            if (editor.targets.Length != 1) return;
            var folder = AssetDatabase.GetAssetPath(editor.target);
            if (!AssetDatabase.IsValidFolder(folder)) return;
            if (!ContentsScanner.TryGetContentsFolder(folder, out var root)) return;

            var settings = Settings;
            var guid = AssetDatabase.AssetPathToGUID(folder);
            var own = settings != null ? settings.GetRule(guid) : null;
            var projectDefault = settings != null ? settings.DefaultDelivery : ContentDelivery.BuiltIn;
            var inherited = settings != null && folder != root
                ? DeliveryResolver.Resolve(folder, root, f => settings.GetRule(AssetDatabase.AssetPathToGUID(f)), projectDefault)
                : projectDefault;
            var source = folder != root && HasRuleAbove(settings, folder, root) ? "parent folder" : "project default";

            var options = new[]
            {
                new GUIContent($"Inherit ({Name(inherited)}, {source})"),
                new GUIContent(Name(ContentDelivery.BuiltIn)),
                new GUIContent(Name(ContentDelivery.Download)),
            };
            var selected = own.HasValue ? 1 + (int)own.Value : 0;

            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.Popup(Label, selected, options);
            if (!EditorGUI.EndChangeCheck()) return;

            settings = cached = ContentsSettings.GetOrCreate();
            settings.SetRule(guid, next == 0 ? null : (ContentDelivery)(next - 1));
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        private static bool HasRuleAbove(ContentsSettings settings, string folder, string root)
        {
            if (settings == null) return false;
            for (var parent = ParentOf(folder); parent != null && parent.Length >= root.Length; parent = ParentOf(parent))
                if (settings.GetRule(AssetDatabase.AssetPathToGUID(parent)).HasValue) return true;
            return false;
        }

        private static string ParentOf(string path)
        {
            var slash = path.LastIndexOf('/');
            return slash > 0 ? path.Substring(0, slash) : null;
        }

        private static string Name(ContentDelivery delivery) => delivery == ContentDelivery.BuiltIn ? "Built-in" : "Download";
    }
}
