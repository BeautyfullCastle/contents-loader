#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace H.ContentsLoader.Integration.Tests
{
    [PrebuildSetup("H.ContentsLoader.Integration.Editor.PatchBuildSetup")]
    public class PatchTests
    {
        const string Root = "Library/ContentsLoaderPatchTest";
        string cacheRoot;
        AssetBundleBackend backend;

        [SetUp]
        public void SetUp()
        {
            cacheRoot = Path.Combine(Application.temporaryCachePath, "ContentsLoaderPatch", Guid.NewGuid().ToString("N"));
            Start();
        }

        [TearDown]
        public void TearDown()
        {
            ContentSystem.Shutdown();
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, true);
        }

        void Start()
        {
            ContentSystem.Shutdown();
            backend = new AssetBundleBackend(new AssetBundleBackendOptions
            {
                BuiltInRoot = Path.GetFullPath($"{Root}/AppV1"),
                CacheRoot = cacheRoot,
                RemoteUrl = "file:///" + Path.GetFullPath($"{Root}/RemoteV2").Replace('\\', '/'),
            });
            ContentSystem.Initialize(backend);
        }

        static IEnumerator Wait(System.Threading.Tasks.Task task)
        {
            yield return new WaitUntil(() => task.IsCompleted);
            if (task.IsFaulted) throw task.Exception!.InnerException!;
        }

        [UnityTest]
        public IEnumerator RemoteCatalogOverridesBuiltInAndDownloadsOnlyChangedPacks()
        {
            Assert.That(Content.Load<TextAsset>("Data/Config").text, Is.EqualTo("difficulty=normal"));
            ContentSystem.Lifetime.UnloadAll();

            var update = Content.UpdateCatalog();
            yield return Wait(update);
            Assert.That(update.Result, Is.True);

            Assert.That(Content.IsDownloaded("Data/Config"), Is.False, "바뀐 Built-in 팩은 앱에 없으니 받아야 함");
            Assert.That(Content.IsDownloaded("Towers/Cannon"), Is.True, "안 바뀐 팩은 앱 내장본을 그대로 씀");

            var load = Content.LoadAsync<TextAsset>("Data/Config");
            yield return Wait(load);
            Assert.That(load.Result.text, Is.EqualTo("difficulty=hard"));
            Assert.That(backend.DownloadCount, Is.EqualTo(1));
            Assert.That(Content.Load<GameObject>("Towers/Cannon"), Is.Not.Null);

            // 앱을 다시 켜도 저장된 원격 카탈로그를 씀
            Start();
            Assert.That(Content.Load<TextAsset>("Data/Config").text, Is.EqualTo("difficulty=hard"));
            Assert.That(backend.DownloadCount, Is.Zero);
        }
    }
}
#endif
