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
        sealed class ProgressRecorder : IProgress<float>
        {
            public readonly List<float> Values = new();
            public void Report(float value) => Values.Add(value);
        }

        string buildRoot;
        string cacheRoot;
        AssetBundleBackend backend;

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
        void Start()
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

        static IEnumerator Wait(System.Threading.Tasks.Task task)
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
