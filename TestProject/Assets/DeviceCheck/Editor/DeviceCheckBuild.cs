using H.ContentsLoader.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// run-device-check.ps1이 -executeMethod로 부름. 기기는 adb reverse로 PC의 로컬 서버에 붙음
public static class DeviceCheckBuild
{
    public const string Scene = "Assets/DeviceCheck/DeviceCheck.unity";
    public const string Output = "Builds/DeviceCheck.apk";
    private const string LocalServer = "http://localhost:8765";

    internal static bool Building;

    public static void BuildAndroid()
    {
        EnsureScene();
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.h.contentsloader.devicecheck");
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        var settings = ContentsSettings.GetOrCreate();
        var remoteUrl = settings.RemoteUrl;
        settings.RemoteUrl = LocalServer;
        Building = true;
        try
        {
            var report = ContentsBuilder.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = Output,
                target = BuildTarget.Android,
                options = BuildOptions.Development,
            });
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
        finally
        {
            // 빌드 중 애셋이 다시 로드돼 기존 참조가 파괴될 수 있어서 다시 찾음
            settings = ContentsSettings.Find();
            settings.RemoteUrl = remoteUrl;
            Building = false;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }

    private static void EnsureScene()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene) != null) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        new GameObject("DeviceCheck").AddComponent<DeviceCheck>();
        EditorSceneManager.SaveScene(scene, Scene);
    }
}
