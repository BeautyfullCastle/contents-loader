using UnityEngine.TestTools;

namespace H.ContentsLoader.Integration.Editor
{
    // PlayMode 번들 테스트가 이름으로 참조함. 플레이 모드 진입 전에 기본 설정으로 빌드함
    public sealed class ContentsBuildSetup : IPrebuildSetup
    {
        public void Setup() => FixtureBuild.Run(smallPackThresholdKB: 64);
    }
}
