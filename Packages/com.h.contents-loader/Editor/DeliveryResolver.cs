using System;

namespace H.ContentsLoader.Editor
{
    internal static class DeliveryResolver
    {
        // 가장 가까운 폴더 규칙을 따르고, 없으면 프로젝트 기본값을 씀
        public static ContentDelivery Resolve(string assetPath, string rootFolder, Func<string, ContentDelivery?> ruleOfFolder,
            ContentDelivery projectDefault = ContentDelivery.BuiltIn)
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
            return projectDefault;
        }

        private static string ParentOf(string path)
        {
            var slash = path.LastIndexOf('/');
            return slash > 0 ? path.Substring(0, slash) : null;
        }
    }
}
