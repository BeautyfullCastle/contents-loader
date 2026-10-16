using System.IO;
using UnityEditor;
using UnityEditor.Build;

namespace H.ContentsLoader.Editor
{
    // 플레이어 빌드 콜백 안에서 BuildAssetBundles를 부르면 빌드 리포트가 깨져 에디터가 크래시함
    // 그래서 콘텐츠는 플레이어 빌드 전에 빌드하고, 콜백은 결과를 StreamingAssets에 넣기만 함
    [InitializeOnLoad]
    internal sealed class ContentsPlayerBuild : BuildPlayerProcessor
    {
        static ContentsPlayerBuild()
        {
            BuildPlayerWindow.RegisterBuildPlayerHandler(BuildFromWindow);
        }

        public override int callbackOrder => 0;

        private static void BuildFromWindow(BuildPlayerOptions options)
        {
            if (ContentsSettings.FindOrDefault().BuildWithPlayer) BuildContent(options.target);
            BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
        }

        internal static void BuildContent(BuildTarget target)
        {
            if (!ContentsBuilder.Build(target).Success)
                throw new BuildFailedException("Contents Loader 콘텐츠 빌드에 실패했습니다. 콘솔 로그를 확인하세요.");
        }

        public override void PrepareForBuild(BuildPlayerContext context)
        {
            var target = context.BuildPlayerOptions.target;
            var builtIn = ContentsBuildPaths.BuiltIn(target);
            if (!File.Exists($"{builtIn}/{ContentCatalog.FileName}"))
                throw new BuildFailedException($"{target}용 콘텐츠 빌드가 없습니다. Tools > Contents Loader > Build Content를 먼저 실행하거나 ContentsBuilder.BuildPlayer로 빌드하세요.");

            context.AddAdditionalPathToStreamingAssets(Path.GetFullPath(builtIn), AssetBundleBackendOptions.FolderName);
            // 콘텐츠 타입은 ContentsLinkerProcessor가 link.xml로 링커에 넘김. 이 앱에 들어간 타입을 기록해 패치 때 비교함
            ContentTypes.RecordPlayerBuild(target);
        }
    }
}
