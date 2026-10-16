using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace H.ContentsLoader.Integration.Tests
{
    // 프로젝트의 PlayModeSource 설정과 무관하게 AssetDatabase 백엔드로 검증함
    public class AssetDatabaseModeTests
    {
        [SetUp]
        public void SetUp()
        {
            ContentSystem.Shutdown();
#if UNITY_EDITOR
            ContentSystem.Initialize(new H.ContentsLoader.Editor.AssetDatabaseBackend(long.MaxValue));
#endif
        }

        [TearDown]
        public void TearDown() => ContentSystem.Shutdown();

        [Test]
        public void LoadsBuiltInContentByPath()
        {
            var cannon = Content.Load<GameObject>("Towers/Cannon");

            Assert.That(cannon, Is.Not.Null);
            Assert.That(cannon.name, Is.EqualTo("Cannon"));
            Assert.That(ContentSystem.Lifetime.Backend.GetType().Name, Is.EqualTo("AssetDatabaseBackend"));
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
        public void LoadsNonPrefabAssets()
        {
            var config = Content.Load<TextAsset>("Data/Config");

            Assert.That(config.text, Is.EqualTo("difficulty=normal"));
        }

        [UnityTest]
        public IEnumerator LoadAsyncReturnsTheSameAsset()
        {
            var task = Content.LoadAsync<GameObject>("Bosses/Dragon");
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.That(task.Result, Is.Not.Null);
            Assert.That(task.Result, Is.SameAs(Content.Load<GameObject>("Bosses/Dragon")));
        }

        [Test]
        public void MissingPathReturnsNullWithWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("Towers/Missing"));

            Assert.That(Content.Load<GameObject>("Towers/Missing"), Is.Null);
            Assert.That(Content.Exists("Towers/Missing"), Is.False);
        }

        [Test]
        public void WrongTypeReturnsNullWithWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("Material"));

            Assert.That(Content.Load<Material>("Towers/Cannon"), Is.Null);
        }
    }
}
