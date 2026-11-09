using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace H.ContentsLoader.Tests
{
    public class LifetimeTests
    {
        private FakeBackend backend;
        private ContentLifetime lifetime;

        [SetUp]
        public void SetUp()
        {
            backend = new FakeBackend();
            backend.ReadyPaths.UnionWith(new[] { "A/Only", "B/Only", "Shared", "Global", "Big1", "Big2", "Big3" });
            backend.Remote.Add("Remote/Boss");
            ContentSystem.Initialize(backend);
            lifetime = ContentSystem.Lifetime;
        }

        [TearDown]
        public void TearDown() => ContentSystem.Shutdown();

        private static Scene CreateScene(string name) => SceneManager.CreateScene($"{name}-{System.Guid.NewGuid():N}");

        private static IEnumerator Unload(Scene scene)
        {
            var operation = SceneManager.UnloadSceneAsync(scene);
            while (!operation.isDone) yield return null;
        }

        private ContentState? StateOf(string path) => lifetime.Find(path, typeof(ScriptableObject))?.State;

        private Object Load(string path, Scene scene) => lifetime.Load(path, typeof(ScriptableObject), ContentOwner.Of(scene));

        [UnityTest]
        public IEnumerator SceneAssetIsCachedOnUnloadThenReleasedNextFrame()
        {
            var a = CreateScene("A");
            Load("A/Only", a);
            Assert.That(StateOf("A/Only"), Is.EqualTo(ContentState.Loaded));

            yield return Unload(a);
            Assert.That(StateOf("A/Only"), Is.EqualTo(ContentState.Cached).Or.Null);

            yield return null;
            yield return null;
            Assert.That(StateOf("A/Only"), Is.Null);
            Assert.That(backend.Released, Does.Contain("A/Only"));
        }

        [UnityTest]
        public IEnumerator AdditiveUnloadReleasesOnlyThatScenesAssets()
        {
            var a = CreateScene("A");
            var b = CreateScene("B");
            Load("A/Only", a);
            Load("B/Only", b);
            Load("Shared", a);
            Load("Shared", b);

            yield return Unload(a);
            yield return null;
            yield return null;

            Assert.That(StateOf("A/Only"), Is.Null);
            Assert.That(StateOf("B/Only"), Is.EqualTo(ContentState.Loaded));
            Assert.That(StateOf("Shared"), Is.EqualTo(ContentState.Loaded));
            yield return Unload(b);
        }

        [UnityTest]
        public IEnumerator AppAssetsSurviveSceneUnload()
        {
            var a = CreateScene("A");
            Content.Load<ScriptableObject>("Global", ContentLife.App);
            Load("Global", a);

            yield return Unload(a);
            yield return null;
            yield return null;

            Assert.That(StateOf("Global"), Is.EqualTo(ContentState.Loaded));
        }

        [UnityTest]
        public IEnumerator CachedAssetReclaimedBeforeSweepIsNotReloaded()
        {
            var a = CreateScene("A");
            var b = CreateScene("B");
            var first = Load("Shared", a);

            lifetime.OnSceneUnloaded(a);
            Assert.That(StateOf("Shared"), Is.EqualTo(ContentState.Cached));
            var second = Load("Shared", b);

            yield return null;
            yield return null;

            Assert.That(second, Is.SameAs(first));
            Assert.That(backend.Loads, Is.EqualTo(1));
            Assert.That(StateOf("Shared"), Is.EqualTo(ContentState.Loaded));
            yield return Unload(a);
            yield return Unload(b);
        }

        [Test]
        public void BudgetEvictsLeastRecentlyUsedCachedAssets()
        {
            var frame = 0;
            lifetime.Frame = () => frame;
            backend.SizePerAsset = 100;
            lifetime.MemoryBudget = 250;
            var a = CreateScene("A");
            var keep = CreateScene("Keep");

            frame = 1; Load("Big1", a);
            frame = 2; Load("Big2", a);
            frame = 3; Load("Big3", keep);
            Assert.That(StateOf("Big1"), Is.EqualTo(ContentState.Loaded), "Loaded 애셋은 예산을 넘어도 유지함");

            lifetime.OnSceneUnloaded(a);

            Assert.That(StateOf("Big1"), Is.Null);
            Assert.That(StateOf("Big2"), Is.EqualTo(ContentState.Cached));
            Assert.That(StateOf("Big3"), Is.EqualTo(ContentState.Loaded));
            SceneManager.UnloadSceneAsync(a);
            SceneManager.UnloadSceneAsync(keep);
        }

        [Test]
        public void BudgetNeverEvictsLoadedAssets()
        {
            backend.SizePerAsset = 100;
            lifetime.MemoryBudget = 50;

            Content.Load<ScriptableObject>("Big1", ContentLife.App);
            lifetime.Tick();

            Assert.That(StateOf("Big1"), Is.EqualTo(ContentState.Loaded));
        }

        [Test]
        public void SyncLoadOfUndownloadedContentThrows()
        {
            var error = Assert.Throws<ContentNotDownloadedException>(() => Content.Load<ScriptableObject>("Remote/Boss"));

            Assert.That(error.Path, Is.EqualTo("Remote/Boss"));
            StringAssert.Contains("LoadAsync", error.Message);
        }

        [UnityTest]
        public IEnumerator LoadAsyncDownloadsThenSyncLoadSucceeds()
        {
            var task = Content.LoadAsync<ScriptableObject>("Remote/Boss");
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.That(task.Result, Is.Not.Null);
            Assert.That(Content.Load<ScriptableObject>("Remote/Boss"), Is.SameAs(task.Result));
            Assert.That(backend.Loads, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ConcurrentAsyncLoadsShareOneBackendLoad()
        {
            backend.AsyncGate = new TaskCompletionSource<bool>();
            var first = Content.LoadAsync<ScriptableObject>("Remote/Boss");
            var second = Content.LoadAsync<ScriptableObject>("Remote/Boss");
            backend.AsyncGate.SetResult(true);
            yield return new WaitUntil(() => first.IsCompleted && second.IsCompleted);

            Assert.That(first.Result, Is.SameAs(second.Result));
            Assert.That(backend.Loads, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AsyncLoadFinishingAfterOwnerUnloadIsReleased()
        {
            var a = CreateScene("A");
            backend.AsyncGate = new TaskCompletionSource<bool>();
            var task = lifetime.LoadAsync("Remote/Boss", typeof(ScriptableObject), ContentOwner.Of(a));

            yield return Unload(a);
            backend.AsyncGate.SetResult(true);
            yield return new WaitUntil(() => task.IsCompleted);
            yield return null;
            yield return null;

            Assert.That(StateOf("Remote/Boss"), Is.Null);
            Assert.That(backend.Released, Does.Contain("Remote/Boss"));
        }

        [Test]
        public void ComponentOwnerIsItsScene()
        {
            var scene = CreateScene("Owner");
            var go = new GameObject("Caller");
            SceneManager.MoveGameObjectToScene(go, scene);

            go.transform.LoadContent<ScriptableObject>("A/Only");

            var record = lifetime.Find("A/Only", typeof(ScriptableObject));
            Assert.That(record.Scenes, Does.Contain(scene));
            Assert.That(record.App, Is.False);
            SceneManager.UnloadSceneAsync(scene);
        }

        [Test]
        public void DontDestroyOnLoadCallerOwnsForTheAppLifetime()
        {
            var go = new GameObject("Persistent");
            Object.DontDestroyOnLoad(go);

            go.transform.LoadContent<ScriptableObject>("Global");

            var record = lifetime.Find("Global", typeof(ScriptableObject));
            Assert.That(record.App, Is.True);
            Assert.That(record.Scenes, Is.Empty);
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator UnloadCachedReleasesOnlyCachedAssets()
        {
            var a = CreateScene("A");
            var b = CreateScene("B");
            Load("A/Only", a);
            Load("B/Only", b);
            // 다음 프레임 정리 전에 확인하려고 씬 언로드 이벤트를 직접 보냄
            lifetime.OnSceneUnloaded(a);
            Assert.That(StateOf("A/Only"), Is.EqualTo(ContentState.Cached));

            Assert.That(lifetime.UnloadCached(), Is.EqualTo(1));
            Assert.That(StateOf("A/Only"), Is.Null);
            Assert.That(backend.Released, Does.Contain("A/Only"));
            Assert.That(StateOf("B/Only"), Is.EqualTo(ContentState.Loaded));
            yield return Unload(a);
            yield return Unload(b);
        }
    }
}
