using System;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace H.ContentsLoader
{
    /// <summary>Loads assets placed under any folder named Contents by their path relative to that folder.</summary>
    public static class Content
    {
        /// <summary>Loads an asset synchronously, like Resources.Load.</summary>
        /// <param name="path">Path relative to the Contents folder, without the file extension.</param>
        /// <returns>The asset, or null when the path does not exist or holds no asset of type <typeparamref name="T"/>.</returns>
        /// <exception cref="ContentNotDownloadedException">The asset is Download content that is not on the device yet. Release builds return null instead.</exception>
        public static T Load<T>(string path, ContentLife life = ContentLife.Scene) where T : Object
        {
            return (T)ContentSystem.Lifetime.Load(path, typeof(T), ContentSystem.OwnerOf(life));
        }

        /// <summary>Loads an asset, downloading it first when it is Download content that is not on the device.</summary>
        public static async Task<T> LoadAsync<T>(string path, ContentLife life = ContentLife.Scene) where T : Object
        {
            return (T)await ContentSystem.Lifetime.LoadAsync(path, typeof(T), ContentSystem.OwnerOf(life));
        }

        /// <summary>Downloads every pack needed by the content under a folder, so later loads need no network.</summary>
        /// <param name="folder">Folder path relative to Contents, such as "Season01". An empty string means all content.</param>
        /// <param name="progress">Receives values from 0 to 1.</param>
        public static Task Download(string folder, IProgress<float> progress = null)
        {
            return ContentSystem.Lifetime.Backend.Download(folder ?? "", progress);
        }

        /// <summary>Returns the number of bytes that <see cref="Download"/> would fetch for a folder.</summary>
        public static long GetDownloadSize(string folder)
        {
            return ContentSystem.Lifetime.Backend.GetDownloadSize(folder ?? "");
        }

        /// <summary>Fetches the remote catalog and applies it when it is newer than the one in use.</summary>
        /// <returns>True when a newer catalog was applied.</returns>
        public static Task<bool> UpdateCatalog()
        {
            return ContentSystem.Lifetime.Backend.UpdateCatalog();
        }

        public static bool Exists(string path)
        {
            return ContentSystem.Lifetime.Backend.GetStatus(path) != ContentStatus.NotFound;
        }

        /// <summary>Returns false when loading the path needs a download first.</summary>
        public static bool IsDownloaded(string path)
        {
            return ContentSystem.Lifetime.Backend.GetStatus(path) == ContentStatus.Ready;
        }
    }

    public static class ContentExtensions
    {
        /// <summary>Loads an asset owned by the scene of this component instead of the active scene.</summary>
        public static T LoadContent<T>(this Component owner, string path, ContentLife life = ContentLife.Scene) where T : Object
        {
            return (T)ContentSystem.Lifetime.Load(path, typeof(T), ContentSystem.OwnerOf(owner.gameObject, life));
        }

        /// <summary>Loads an asset asynchronously, owned by the scene of this component.</summary>
        public static async Task<T> LoadContentAsync<T>(this Component owner, string path, ContentLife life = ContentLife.Scene) where T : Object
        {
            var contentOwner = ContentSystem.OwnerOf(owner.gameObject, life);
            return (T)await ContentSystem.Lifetime.LoadAsync(path, typeof(T), contentOwner);
        }
    }
}
