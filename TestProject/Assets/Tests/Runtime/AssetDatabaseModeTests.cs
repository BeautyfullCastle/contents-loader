using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace H.ContentsLoader.Integration.Tests
{
    // 기본 설정(PlayModeSource = AssetDatabase)의 에디터 백엔드를 그대로 씀
    public class AssetDatabaseModeTests
    {
        [SetUp]
        public void SetUp() => ContentSystem.Shutdown();

        [TearDown]
        public void TearDown() => ContentSystem.Shutdown();

        [Test]
        public void LoadsBuiltInContentByPath()
        {
            var cannon = Content.Load<GameObject>("Towers/Cannon");

            Assert.That(cannon, Is.Not.Null);
            Assert.That(cannon.name, Is.EqualTo("Cannon"));
            Assert.That(ContentSystem.Lifetime.Backend.Name, Is.EqualTo("AssetDatabase"));
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
