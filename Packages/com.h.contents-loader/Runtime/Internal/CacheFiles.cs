namespace H.ContentsLoader
{
    internal static class CacheFiles
    {
        // iOS의 persistentDataPath(Documents)는 iCloud 백업 대상임. 다시 받을 수 있는 콘텐츠는 백업에서 빼야
        // 사용자 iCloud 용량을 안 먹고 App Store 심사(데이터 저장 가이드라인)에도 안 걸림
        // Library/Caches로 옮기지 않는 이유: 저장 공간이 부족하면 OS가 비워서 오프라인에서 콘텐츠가 사라짐
        public static void ExcludeFromBackup(string path)
        {
#if UNITY_IOS && !UNITY_EDITOR
            UnityEngine.iOS.Device.SetNoBackupFlag(path);
#endif
        }
    }
}
