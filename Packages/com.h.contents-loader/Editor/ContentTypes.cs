using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace H.ContentsLoader.Editor
{
    // 플레이어 빌드는 씬과 코드만 보고 안 쓰는 엔진 코드를 지움(Strip Engine Code, Managed Stripping)
    // 번들 안에서만 쓰는 타입(예: 프리팹의 BoxCollider)은 지워져서, 로드하면 "Could not produce class" 에러와 함께 컴포넌트가 빠짐
    // 그래서 콘텐츠에 들어가는 모든 타입을 link.xml로 남겨 링커에 넘김
    internal static class ContentTypes
    {
        public static string TypesFile(BuildTarget target) => $"{ContentsBuildPaths.Root(target)}/types.txt";
        public static string LinkXml(BuildTarget target) => $"{ContentsBuildPaths.Root(target)}/link.xml";

        // 마지막 플레이어 빌드에 들어간 타입. 패치로만 새 타입을 내보내는지 비교할 때 씀
        public static string PlayerTypesFile(BuildTarget target) => $"{ContentsBuildPaths.Root(target)}/player-types.txt";

        // "어셈블리|타입" 형식
        public static SortedSet<string> Collect(IEnumerable<string> assetPaths)
        {
            var types = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var path in assetPaths)
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset == null) continue;
                var type = asset.GetType();
                var assembly = type.Assembly.GetName().Name;
                // 에디터 전용 타입은 플레이어에 없음
                if (assembly.StartsWith("UnityEditor", StringComparison.Ordinal) || assembly.EndsWith(".Editor", StringComparison.Ordinal)) continue;
                types.Add($"{assembly}|{type.FullName}");
            }
            return types;
        }

        public static void Write(BuildTarget target, SortedSet<string> types)
        {
            File.WriteAllLines(TypesFile(target), types);

            var xml = new StringBuilder();
            xml.AppendLine("<!-- Contents Loader가 생성함. 번들에서만 쓰는 타입이 플레이어 빌드에서 제거되지 않게 함 -->");
            xml.AppendLine("<linker>");
            foreach (var group in types.Select(t => t.Split('|')).GroupBy(p => p[0]))
            {
                xml.AppendLine($"  <assembly fullname=\"{SecurityElement.Escape(group.Key)}\">");
                foreach (var parts in group)
                    xml.AppendLine($"    <type fullname=\"{SecurityElement.Escape(parts[1])}\" preserve=\"all\"/>");
                xml.AppendLine("  </assembly>");
            }
            xml.AppendLine("</linker>");
            File.WriteAllText(LinkXml(target), xml.ToString());
        }

        // 이미 나간 앱에 없는 타입이 콘텐츠에 생기면 패치만으로는 그 앱에서 로드되지 않음
        public static List<string> MissingFromPlayer(BuildTarget target, SortedSet<string> types)
        {
            var file = PlayerTypesFile(target);
            if (!File.Exists(file)) return new List<string>();
            var shipped = new HashSet<string>(File.ReadAllLines(file), StringComparer.Ordinal);
            return types.Where(t => !shipped.Contains(t)).Select(t => t.Split('|')[1]).ToList();
        }

        public static void RecordPlayerBuild(BuildTarget target)
        {
            if (File.Exists(TypesFile(target))) File.Copy(TypesFile(target), PlayerTypesFile(target), true);
        }
    }

    internal sealed class ContentsLinkerProcessor : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var path = ContentTypes.LinkXml(report.summary.platform);
            return File.Exists(path) ? Path.GetFullPath(path) : null;
        }
    }
}
