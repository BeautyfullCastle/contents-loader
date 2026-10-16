#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace H.ContentsLoader.Integration.Tests
{
    [PrebuildSetup("H.ContentsLoader.Integration.Editor.ContentsBuildSetup")]
    public class BundleModeTests
    {
        private sealed class ProgressRecorder : IProgress<float>
        {
            public readonly List<float> Values = new();
            public void Report(float value) => Values.Add(value);
        }

        private string buildRoot;
        private string cacheRoot;
        private AssetBundleBackend backend;

        [SetUp]
        public void SetUp()
        {
            buildRoot = Path.GetFullPath($"ContentsBuild/{EditorUserBuildSettings.activeBuildTarget}");
            cacheRoot = Path.Combine(Application.temporaryCachePath, "ContentsLoaderTests", Guid.NewGuid().ToString("N"));
            Start();
        }

        [TearDown]
        public void TearDown()
        {
            ContentSystem.Shutdown();
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, true);
        }

        // 같은 캐시 폴더로 앱을 다시 켠 상황
        private void Start()
        {
            ContentSystem.Shutdown();
            backend = new AssetBundleBackend(new AssetBundleBackendOptions
            {
                BuiltInRoot = Path.Combine(buildRoot, "BuiltIn"),
                CacheRoot = cacheRoot,
                RemoteUrl = "file:///" + Path.Combine(buildRoot, "Remote").Replace('\\', '/'),
            });
            ContentSystem.Initialize(backend);
        }

        private static IEnumerator Wait(System.Threading.Tasks.Task task)
        {
            yield return new WaitUntil(() => task.IsCompleted);
            if (task.IsFaulted) throw task.Exception!.InnerException!;
        }

        [Test]
        public void LoadsBuiltInPrefabWithItsDependencies()
        {
            var cannon = Content.Load<GameObject>("Towers/Cannon");

            Assert.That(cannon, Is.Not.Null);
            Assert.That(ContentSystem.Lifetime.Backend, Is.SameAs(backend));
            var materials = cannon.GetComponentsInChildren<MeshRenderer>().Select(r => r.sharedMaterial).ToList();
            Assert.That(materials.Select(m => m.name), Is.EquivalentTo(new[] { "Cannon", "Shared" }));
            Assert.That(materials.All(m => m.mainTexture != null));
        }

        [Test]
        public void SamePathPicksTheAssetOfTheRequestedType()
        {
            var prefab = Content.Load<GameObject>("Towers/Cannon");
            var texture = Content.Load<Texture2D>("Towers/Cannon");

            Assert.That(prefab, Is.Not.Null);
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(16));
            Assert.That(Content.Exists("Towers/Cannon"));
        }

        [Test]
        public void LoadsTextAssets()
        {
            Assert.That(Content.Load<TextAsset>("Data/Config").text, Is.EqualTo("difficulty=normal"));
        }

        [UnityTest]
        public IEnumerator LoadAsyncMatchesSyncLoad()
        {
            var task = Content.LoadAsync<GameObject>("Towers/Archer");
            yield return Wait(task);

            Assert.That(task.Result, Is.Not.Null);
            Assert.That(Content.Load<GameObject>("Towers/Archer"), Is.SameAs(task.Result));
        }

        [Test]
        public void SyncLoadOfUndownloadedContentThrows()
        {
            Assert.That(Content.IsDownloaded("Bosses/Dragon"), Is.False);
            Assert.That(Content.GetDownloadSize("Bosses"), Is.GreaterThan(0));

            var error = Assert.Throws<ContentNotDownloadedException>(() => Content.Load<GameObject>("Bosses/Dragon"));
            StringAssert.Contains("Bosses/Dragon", error.Message);
        }

        [UnityTest]
        public IEnumerator LoadAsyncDownloadsOnceThenLoadsFromCache()
        {
            var task = Content.LoadAsync<GameObject>("Bosses/Dragon");
            yield return Wait(task);

            var dragon = task.Result;
            Assert.That(dragon, Is.Not.Null);
            Assert.That(dragon.GetComponentsInChildren<MeshRenderer>().Select(r => r.sharedMaterial.shader.name),
                Does.Contain("ContentsLoader/Simple"));
            Assert.That(backend.DownloadCount, Is.EqualTo(1), "Dragon 전용 팩 하나만 받아야 함");

            var cached = Directory.GetFiles(Path.Combine(cacheRoot, "packs"));
            Assert.That(cached, Has.Length.EqualTo(1));
            var remote = Path.Combine(buildRoot, "Remote", Path.GetFileName(cached[0]));
            Assert.That(new FileInfo(cached[0]).Length, Is.Not.EqualTo(new FileInfo(remote).Length), "LZ4로 재압축돼야 함");

            Start();
            Assert.That(Content.IsDownloaded("Bosses/Dragon"));
            Assert.That(Content.GetDownloadSize("Bosses"), Is.Zero);
            Assert.That(Content.Load<GameObject>("Bosses/Dragon"), Is.Not.Null);
            Assert.That(backend.DownloadCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DownloadFolderReportsProgressAndEnablesSyncLoad()
        {
            var progress = new ProgressRecorder();
            yield return Wait(Content.Download("Season01", progress));

            Assert.That(progress.Values, Is.Not.Empty);
            Assert.That(progress.Values.Last(), Is.EqualTo(1f));
            Assert.That(progress.Values, Is.Ordered);
            Assert.That(Content.Load<GameObject>("Season01/Goblin"), Is.Not.Null);
            Assert.That(Content.IsDownloaded("Bosses/Dragon"), Is.False);
        }

        [Test]
        public void DownloadOfUnknownFolderThrows()
        {
            Assert.Throws<ArgumentException>(() => Content.Download("NoSuchFolder"));
        }

        [UnityTest]
        public IEnumerator SceneUnloadUnloadsBundles()
        {
            var scene = SceneManager.CreateScene($"Owner-{Guid.NewGuid():N}");
            var caller = new GameObject("Caller");
            SceneManager.MoveGameObjectToScene(caller, scene);
            caller.transform.LoadContent<GameObject>("Towers/Cannon");
            Assert.That(AssetBundle.GetAllLoadedAssetBundles().Count(), Is.GreaterThan(0));

            var unload = SceneManager.UnloadSceneAsync(scene);
            while (!unload.isDone) yield return null;
            yield return null;
            yield return null;

            Assert.That(AssetBundle.GetAllLoadedAssetBundles(), Is.Empty);
            Assert.That(Content.Load<GameObject>("Towers/Cannon"), Is.Not.Null, "해제 후 다시 로드할 수 있어야 함");
        }

        // Cannon과 Archer는 Shared.mat 팩을 같이 씀. 메모리는 열린 팩 기준으로 한 번만 세야 함
        [UnityTest]
        public IEnumerator MemoryCountsSharedPacksOnceAndDropsOnUnload()
        {
            var scene = SceneManager.CreateScene($"Owner-{Guid.NewGuid():N}");
            var caller = new GameObject("Caller");
            SceneManager.MoveGameObjectToScene(caller, scene);
            caller.transform.LoadContent<GameObject>("Towers/Cannon");
            caller.transform.LoadContent<GameObject>("Towers/Archer");

            var lifetime = ContentSystem.Lifetime;
            var cannon = lifetime.Find("Towers/Cannon", typeof(GameObject)).Size;
            var archer = lifetime.Find("Towers/Archer", typeof(GameObject)).Size;
            var openPacks = backend.GetPackInfos().Where(p => p.RefCount > 0).ToList();
            Assert.That(cannon, Is.GreaterThan(1024), "프리팹 크기에 의존 팩(텍스처, 메시)이 들어가야 함");
            Assert.That(backend.LoadedBytes, Is.EqualTo(openPacks.Sum(p => p.LoadedBytes)));
            Assert.That(lifetime.LoadedSize, Is.EqualTo(backend.LoadedBytes));
            Assert.That(cannon + archer, Is.GreaterThan(backend.LoadedBytes), "공유 팩을 두 번 세면 안 됨");

            var unload = SceneManager.UnloadSceneAsync(scene);
            while (!unload.isDone) yield return null;
            yield return null;
            yield return null;

            Assert.That(backend.LoadedBytes, Is.Zero);
        }

        // 예산은 실제로 열린 팩 바이트로 판단함. 씬이 내려가 대기 상태가 되면 다음 프레임을 기다리지 않고 바로 내려감
        [Test]
        public void BudgetEvictsCachedAssetsByOpenPackBytes()
        {
            var scene = SceneManager.CreateScene($"Owner-{Guid.NewGuid():N}");
            var caller = new GameObject("Caller");
            SceneManager.MoveGameObjectToScene(caller, scene);
            caller.transform.LoadContent<GameObject>("Towers/Cannon");
            var lifetime = ContentSystem.Lifetime;
            lifetime.MemoryBudget = backend.LoadedBytes - 1;
            Assert.That(lifetime.Find("Towers/Cannon", typeof(GameObject)).State, Is.EqualTo(ContentState.Loaded), "Loaded 애셋은 예산을 넘어도 유지함");

            lifetime.OnSceneUnloaded(scene);

            Assert.That(lifetime.Find("Towers/Cannon", typeof(GameObject)), Is.Null);
            Assert.That(backend.LoadedBytes, Is.Zero);
            SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator RestartRemovesDownloadsNoCatalogUses()
        {
            yield return Wait(Content.Download("Bosses"));
            var packs = Path.Combine(cacheRoot, "packs");
            var downloaded = Directory.GetFiles(packs);
            Assert.That(downloaded, Is.Not.Empty);

            // 이전 패치에서 받았다가 이제 아무 카탈로그도 안 쓰는 팩과 받다 만 파일
            File.WriteAllText(Path.Combine(packs, "pold_0000.bundle"), "stale");
            File.WriteAllText(Path.Combine(packs, "pold_0000.bundle.download"), "stale");
            Start();

            Assert.That(Directory.GetFiles(packs), Is.EquivalentTo(downloaded));
            Assert.That(Content.IsDownloaded("Bosses/Dragon"), "카탈로그가 쓰는 팩은 그대로 남아야 함");
        }

        [UnityTest]
        public IEnumerator UpdateCatalogIgnoresTheSameBuild()
        {
            var task = Content.UpdateCatalog();
            yield return Wait(task);

            Assert.That(task.Result, Is.False);
        }
    }
}
#endif
