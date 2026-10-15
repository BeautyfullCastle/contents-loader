#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace H.ContentsLoader.Integration.Tests
{
    public class SceneSwitchTests
    {
        const string Scenes = "Assets/Tests/Scenes";

        [SetUp]
        public void SetUp() => ContentSystem.Shutdown();

        [TearDown]
        public void TearDown() => ContentSystem.Shutdown();

        static IEnumerator LoadSingle(string name)
        {
            var operation = EditorSceneManager.LoadSceneAsyncInPlayMode($"{Scenes}/{name}.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!operation.isDone) yield return null;
        }

        static ContentState? StateOf(string path) => ContentSystem.Lifetime.Find(path, typeof(GameObject))?.State;

        [UnityTest]
        public IEnumerator SingleSwitchReleasesOnlyAssetsTheNewSceneDoesNotClaim()
        {
            yield return LoadSingle("SceneA");
            Content.Load<GameObject>("Towers/Cannon");
            Content.Load<GameObject>("Towers/Archer");
            Content.Load<GameObject>("UI/Shop", ContentLife.App);
            var loadsBefore = ContentSystem.Lifetime.BackendLoadCount;

            // 새 씬의 Awake에서 같은 애셋을 다시 요청하는 상황을 흉내냄
            GameObject archerInB = null;
            void ClaimInB(Scene scene, LoadSceneMode mode) => archerInB = Content.Load<GameObject>("Towers/Archer");
            SceneManager.sceneLoaded += ClaimInB;
            try
            {
                yield return LoadSingle("SceneB");
            }
            finally
            {
                SceneManager.sceneLoaded -= ClaimInB;
            }
            yield return null;
            yield return null;

            Assert.That(archerInB, Is.Not.Null);
            Assert.That(StateOf("Towers/Cannon"), Is.Null, "A 전용 애셋은 해제돼야 함");
            Assert.That(StateOf("Towers/Archer"), Is.EqualTo(ContentState.Owned));
            Assert.That(StateOf("UI/Shop"), Is.EqualTo(ContentState.Owned));
            Assert.That(ContentSystem.Lifetime.BackendLoadCount, Is.EqualTo(loadsBefore), "공유 애셋은 다시 로드하지 않아야 함");

            var archer = ContentSystem.Lifetime.Find("Towers/Archer", typeof(GameObject));
            Assert.That(archer.Scenes, Has.Count.EqualTo(1));
            Assert.That(archer.Scenes, Does.Contain(SceneManager.GetActiveScene()));
        }

        [UnityTest]
        public IEnumerator AdditiveSceneUnloadReleasesItsOwnAssets()
        {
            yield return LoadSingle("SceneA");
            var operation = EditorSceneManager.LoadSceneAsyncInPlayMode($"{Scenes}/SceneC.unity", new LoadSceneParameters(LoadSceneMode.Additive));
            while (!operation.isDone) yield return null;
            var sceneC = SceneManager.GetSceneByPath($"{Scenes}/SceneC.unity");

            var caller = new GameObject("CallerInC");
            SceneManager.MoveGameObjectToScene(caller, sceneC);
            caller.transform.LoadContent<GameObject>("Towers/Cannon");
            Content.Load<GameObject>("Towers/Archer");

            var unload = SceneManager.UnloadSceneAsync(sceneC);
            while (!unload.isDone) yield return null;
            yield return null;
            yield return null;

            Assert.That(StateOf("Towers/Cannon"), Is.Null);
            Assert.That(StateOf("Towers/Archer"), Is.EqualTo(ContentState.Owned));
        }
    }
}
#endif
