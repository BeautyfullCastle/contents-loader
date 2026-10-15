using UnityEditor;
using UnityEngine;

namespace H.ContentsLoader.Editor
{
    [InitializeOnLoad]
    static class ContentsFolderHeader
    {
        static readonly GUIContent Label = new("Delivery", "Built-in packs ship with the app. Download packs are fetched on first use.");

        static ContentsFolderHeader()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
        }

        static void OnHeaderGUI(UnityEditor.Editor editor)
        {
            if (editor.targets.Length != 1) return;
            var folder = AssetDatabase.GetAssetPath(editor.target);
            if (!AssetDatabase.IsValidFolder(folder)) return;
            if (!ContentsScanner.TryGetContentsFolder(folder, out var root)) return;

            var settings = ContentsSettings.Find();
            var guid = AssetDatabase.AssetPathToGUID(folder);
            var own = settings != null ? settings.GetRule(guid) : null;
            var inherited = settings != null && folder != root
                ? DeliveryResolver.Resolve(folder, root, f => settings.GetRule(AssetDatabase.AssetPathToGUID(f)))
                : ContentDelivery.BuiltIn;

            var options = new[]
            {
                new GUIContent($"Inherit ({Name(inherited)})"),
                new GUIContent(Name(ContentDelivery.BuiltIn)),
                new GUIContent(Name(ContentDelivery.Download)),
            };
            var selected = own.HasValue ? 1 + (int)own.Value : 0;

            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.Popup(Label, selected, options);
            if (!EditorGUI.EndChangeCheck()) return;

            settings = ContentsSettings.GetOrCreate();
            settings.SetRule(guid, next == 0 ? null : (ContentDelivery)(next - 1));
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        static string Name(ContentDelivery delivery) => delivery == ContentDelivery.BuiltIn ? "Built-in" : "Download";
    }
}
