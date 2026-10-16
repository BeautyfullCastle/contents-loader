using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace H.ContentsLoader
{
    internal static class ContentSystem
    {
        // 에디터 어셈블리가 InitializeOnLoad에서 채움. 런타임 어셈블리는 UnityEditor를 참조하지 않음
        internal static Func<IContentBackend> EditorBackendFactory;

        private static ContentLifetime lifetime;
        private static ContentRunner runner;

        public static ContentLifetime Lifetime
        {
            get
            {
                if (lifetime == null) Initialize(CreateDefaultBackend());
                return lifetime;
            }
        }

        public static bool IsInitialized => lifetime != null;

        internal static void Initialize(IContentBackend backend)
        {
            Shutdown();
            lifetime = new ContentLifetime(backend);
            if (!Application.isPlaying) return;

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Application.quitting += Shutdown;

            var go = new GameObject("[Contents Loader]") { hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable };
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<ContentRunner>();
        }

        internal static void Shutdown()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Application.quitting -= Shutdown;

            if (runner != null) UnityEngine.Object.Destroy(runner.gameObject);
            runner = null;

            if (lifetime == null) return;
            lifetime.UnloadAll();
            lifetime.Backend.Dispose();
            lifetime = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Shutdown();

        internal static void Tick() => lifetime?.Tick();

        private static IContentBackend CreateDefaultBackend()
        {
            return EditorBackendFactory?.Invoke() ?? new AssetBundleBackend(AssetBundleBackendOptions.ForPlayer());
        }

        public static ContentOwner OwnerOf(ContentLife life)
        {
            if (life == ContentLife.App) return ContentOwner.App;
            var scene = SceneManager.GetActiveScene();
            return scene.IsValid() ? ContentOwner.Of(scene) : ContentOwner.App;
        }

        public static ContentOwner OwnerOf(GameObject context, ContentLife life)
        {
            if (life == ContentLife.App) return ContentOwner.App;
            var scene = context.scene;
            // 프리팹 애셋 등 씬 밖 오브젝트는 활성 씬에 귀속함
            if (!scene.IsValid()) return OwnerOf(life);
            return IsDontDestroyOnLoad(scene) ? ContentOwner.App : ContentOwner.Of(scene);
        }

        private static bool IsDontDestroyOnLoad(Scene scene)
        {
            return runner != null ? scene == runner.gameObject.scene : scene.buildIndex == -1 && scene.name == "DontDestroyOnLoad";
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => lifetime?.OnSceneLoaded(scene);

        private static void OnSceneUnloaded(Scene scene) => lifetime?.OnSceneUnloaded(scene);
    }
}
