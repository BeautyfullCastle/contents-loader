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
    [PrebuildSetup("H.ContentsLoader.Integration.Editor.ContentsBuildSetup")]
    public class ResumeDownloadTests
    {
        private string buildRoot;
        private string cacheRoot;
        private RangeServer server;
        private AssetBundleBackend backend;

        [SetUp]
        public void SetUp()
        {
            buildRoot = Path.GetFullPath($"ContentsBuild/{EditorUserBuildSettings.activeBuildTarget}");
            cacheRoot = Path.Combine(Application.temporaryCachePath, "ContentsLoaderResume", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            ContentSystem.Shutdown();
            server?.Dispose();
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
                RemoteUrl = server.Url,
            });
            ContentSystem.Initialize(backend);
        }

        private static IEnumerator Settle(Task task)
        {
            yield return new WaitUntil(() => task.IsCompleted);
        }

        private string PartialFile() => Directory.GetFiles(Path.Combine(cacheRoot, "packs"), "*.download").Single();

        [UnityTest]
        public IEnumerator InterruptedDownloadResumesFromReceivedBytes()
        {
            server = new RangeServer(Path.Combine(buildRoot, "Remote"), cutFirstResponseAfterBytes: 64 * 1024);
            Start();
            var total = Content.GetDownloadSize("Bosses");

            var first = Content.LoadAsync<GameObject>("Bosses/Dragon");
            yield return Settle(first);
            Assert.That(first.IsFaulted, "연결이 끊기면 첫 로드는 실패해야 함");

            var received = new FileInfo(PartialFile()).Length;
            Assert.That(received, Is.GreaterThan(0).And.LessThan(total));

            Start();
            Assert.That(Content.GetDownloadSize("Bosses"), Is.EqualTo(total - received), "다시 켜면 남은 크기만 받아야 함");

            var second = Content.LoadAsync<GameObject>("Bosses/Dragon");
            yield return Settle(second);
            if (second.IsFaulted) throw second.Exception!.InnerException!;

            Assert.That(second.Result, Is.Not.Null);
            Assert.That(server.Ranges, Is.EqualTo(new[] { "", $"bytes={received}-" }));
            Assert.That(Content.IsDownloaded("Bosses/Dragon"));
        }

        [UnityTest]
        public IEnumerator ServerIgnoringRangeStillProducesAValidPack()
        {
            server = new RangeServer(Path.Combine(buildRoot, "Remote"), cutFirstResponseAfterBytes: 64 * 1024, ignoreRange: true);
            Start();

            var first = Content.LoadAsync<GameObject>("Bosses/Dragon");
            yield return Settle(first);
            Assert.That(first.IsFaulted);

            Start();
            var second = Content.LoadAsync<GameObject>("Bosses/Dragon");
            yield return Settle(second);
            if (second.IsFaulted) throw second.Exception!.InnerException!;

            Assert.That(second.Result, Is.Not.Null);
            Assert.That(server.Ranges.Count, Is.EqualTo(3), "Range 무시 응답을 버리고 처음부터 다시 받아야 함");
        }
    }
}
#endif
