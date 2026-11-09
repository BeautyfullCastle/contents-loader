#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace H.ContentsLoader.Integration.Tests
{
    // WebGL처럼 카탈로그와 팩을 주소에서 메모리로 받는 모드. 빌드 결과 폴더를 file:// 주소로 흉내 냄
    [PrebuildSetup("H.ContentsLoader.Integration.Editor.ContentsBuildSetup")]
    public class MemoryModeTests
    {
        private string buildRoot;
        private string cacheRoot;
        private AssetBundleBackend backend;

        private static string Url(string path) => "file:///" + path.Replace('\\', '/');

        [SetUp]
        public void SetUp()
        {
            buildRoot = Path.GetFullPath($"ContentsBuild/{EditorUserBuildSettings.activeBuildTarget}");
            cacheRoot = Path.Combine(Application.temporaryCachePath, "ContentsLoaderMemoryTests", Guid.NewGuid().ToString("N"));
            ContentSystem.Shutdown();
            backend = new AssetBundleBackend(new AssetBundleBackendOptions
            {
                BuiltInRoot = Url(Path.Combine(buildRoot, "BuiltIn")),
                CacheRoot = cacheRoot,
                RemoteUrl = Url(Path.Combine(buildRoot, "Remote")),
                MemoryPacks = true,
            });
            ContentSystem.Initialize(backend);
        }

        [TearDown]
        public void TearDown()
        {
            ContentSystem.Shutdown();
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, true);
        }

        private static IEnumerator Wait(Task task)
        {
            yield return new WaitUntil(() => task.IsCompleted);
            if (task.IsFaulted) throw task.Exception!.InnerException!;
        }

        [Test]
        public void SyncLoadBeforeTheCatalogArrivesThrows()
        {
            Assert.That(backend.Ready.IsCompleted, Is.False);
            var error = Assert.Throws<InvalidOperationException>(() => Content.Load<GameObject>("Towers/Cannon"));
            StringAssert.Contains("Content.Download", error.Message);
        }

        [UnityTest]
        public IEnumerator DownloadOfBuiltInContentEnablesSyncLoad()
        {
            yield return Wait(backend.Ready);
            Assert.That(Content.IsDownloaded("Towers/Cannon"), Is.False, "내장 팩도 받기 전에는 메모리에 없음");
            Assert.That(Content.GetDownloadSize("Towers"), Is.GreaterThan(0));

            yield return Wait(Content.Download("Towers"));

            var cannon = Content.Load<GameObject>("Towers/Cannon");
            Assert.That(cannon, Is.Not.Null);
            var materials = cannon.GetComponentsInChildren<MeshRenderer>().Select(r => r.sharedMaterial).ToList();
            Assert.That(materials.Select(m => m.name), Is.EquivalentTo(new[] { "Cannon", "Shared" }));
            Assert.That(materials.All(m => m.mainTexture != null));
            Assert.That(Content.GetDownloadSize("Towers"), Is.Zero);
            Assert.That(backend.DownloadCount, Is.Zero, "내장 팩은 원격 다운로드로 세지 않음");
        }

        [UnityTest]
        public IEnumerator LoadAsyncWorksBeforeTheCatalogArrives()
        {
            var task = Content.LoadAsync<TextAsset>("Data/Config");
            yield return Wait(task);

            Assert.That(task.Result.text, Is.EqualTo("difficulty=normal"));
            Assert.That(Content.Load<TextAsset>("Data/Config"), Is.SameAs(task.Result));
        }

        [UnityTest]
        public IEnumerator DownloadsRemotePacksIntoMemory()
        {
            var task = Content.LoadAsync<GameObject>("Bosses/Dragon");
            yield return Wait(task);

            Assert.That(task.Result, Is.Not.Null);
            Assert.That(backend.DownloadCount, Is.EqualTo(1), "Dragon 전용 팩 하나만 받아야 함");
            Assert.That(Directory.Exists(Path.Combine(cacheRoot, "packs")), Is.False, "메모리 모드는 팩을 파일로 남기지 않음");
        }

        [UnityTest]
        public IEnumerator ReopensAPackFromMemoryAfterItIsUnloaded()
        {
            yield return Wait(Content.Download("Towers"));
            Assert.That(Content.Load<GameObject>("Towers/Cannon"), Is.Not.Null);

            ContentSystem.Lifetime.UnloadAll();
            Assert.That(backend.LoadedBytes, Is.Zero);

            Assert.That(Content.Load<GameObject>("Towers/Cannon"), Is.Not.Null, "다시 받지 않고 메모리에서 다시 열어야 함");
        }

        [UnityTest]
        public IEnumerator MissingCatalogStartsEmpty()
        {
            ContentSystem.Shutdown();
            backend = new AssetBundleBackend(new AssetBundleBackendOptions
            {
                BuiltInRoot = Url(Path.Combine(cacheRoot, "nothing")),
                CacheRoot = cacheRoot,
                MemoryPacks = true,
            });
            ContentSystem.Initialize(backend);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("카탈로그를 받지 못했습니다"));
            yield return Wait(backend.Ready);

            Assert.That(Content.Exists("Towers/Cannon"), Is.False);
        }
    }
}
#endif
