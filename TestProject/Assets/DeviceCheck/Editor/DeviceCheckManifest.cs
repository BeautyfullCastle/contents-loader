using System.IO;
using UnityEditor.Android;

// 검증 빌드만 로컬 HTTP 서버를 쓰므로 그때만 앱 매니페스트에 HTTP 허용을 넣음
// Unity의 insecureHttpOption은 UnityWebRequest에만 적용되고 DownloadManager에는 안 통함
sealed class DeviceCheckManifest : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 0;

    public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
    {
        if (!DeviceCheckBuild.Building) return;
        var manifest = Path.Combine(unityLibraryPath, "src/main/AndroidManifest.xml");
        var text = File.ReadAllText(manifest);
        if (text.Contains("usesCleartextTraffic")) return;
        File.WriteAllText(manifest, text.Replace("<application", "<application android:usesCleartextTraffic=\"true\""));
    }
}
