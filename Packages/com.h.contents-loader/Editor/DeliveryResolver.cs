using System;

namespace H.ContentsLoader.Editor
{
    internal static class DeliveryResolver
    {
        public static ContentDelivery Resolve(string assetPath, string rootFolder, Func<string, ContentDelivery?> ruleOfFolder)
        {
            var folder = ParentOf(ContentPath.Normalize(assetPath));
            rootFolder = ContentPath.Normalize(rootFolder);
            while (folder != null)
            {
                var rule = ruleOfFolder(folder);
                if (rule.HasValue) return rule.Value;
                if (folder == rootFolder) break;
                folder = ParentOf(folder);
            }
            return ContentDelivery.BuiltIn;
        }

        static string ParentOf(string path)
        {
            var slash = path.LastIndexOf('/');
            return slash > 0 ? path.Substring(0, slash) : null;
        }
    }
}
