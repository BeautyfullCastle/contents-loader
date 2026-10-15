using System.IO;
using UnityEditor;
using UnityEditor.Build;

namespace H.ContentsLoader.Editor
{
    // 프로젝트 Assets에 복사하지 않고 빌드 중에만 StreamingAssets에 포함시킴
    sealed class ContentsPlayerBuild : BuildPlayerProcessor
    {
        public override int callbackOrder => 0;

        public override void PrepareForBuild(BuildPlayerContext context)
        {
            var settings = ContentsSettings.FindOrDefault();
            if (!settings.BuildWithPlayer) return;

            var target = context.BuildPlayerOptions.target;
            var result = ContentsBuilder.Build(target, settings);
            if (!result.Success)
                throw new BuildFailedException("Contents Loader 콘텐츠 빌드에 실패했습니다. 콘솔 로그를 확인하세요.");

            context.AddAdditionalPathToStreamingAssets(Path.GetFullPath(result.BuiltInDirectory), AssetBundleBackendOptions.FolderName);
        }
    }
}
