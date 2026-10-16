using System.IO;
using UnityEditor;
using UnityEngine.TestTools;

namespace H.ContentsLoader.Integration.Editor
{
    // 앱에 들어간 v1과 Config.txt만 바뀐 원격 v2를 만듦. 끝나면 픽스처를 원래대로 되돌려 다시 빌드함
    public sealed class PatchBuildSetup : IPrebuildSetup
    {
        public const string Root = "Library/ContentsLoaderPatchTest";
        private const string Config = "Assets/Game/Contents/Data/Config.txt";

        public void Setup()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            Directory.CreateDirectory(Root);
            var original = File.ReadAllText(Config);

            var v1 = FixtureBuild.Run(smallPackThresholdKB: 64, ignorePrevious: false);
            FileUtil.CopyFileOrDirectory(v1.BuiltInDirectory, $"{Root}/AppV1");
            try
            {
                File.WriteAllText(Config, "difficulty=hard");
                AssetDatabase.ImportAsset(Config);
                var v2 = FixtureBuild.Run(smallPackThresholdKB: 64, ignorePrevious: false);
                FileUtil.CopyFileOrDirectory(v2.RemoteDirectory, $"{Root}/RemoteV2");
            }
            finally
            {
                File.WriteAllText(Config, original);
                AssetDatabase.ImportAsset(Config);
                FixtureBuild.Run(smallPackThresholdKB: 64, ignorePrevious: false);
            }
        }
    }
}
