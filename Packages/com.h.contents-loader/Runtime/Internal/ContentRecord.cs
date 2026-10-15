using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace H.ContentsLoader
{
    // Loading -> Owned <-> Cached -> Unloaded
    internal enum ContentState
    {
        Loading,
        Owned,
        Cached,
        Unloaded,
    }

    internal readonly struct ContentOwner
    {
        public static readonly ContentOwner App = new(default, true);

        public readonly Scene Scene;
        public readonly bool IsApp;

        ContentOwner(Scene scene, bool isApp)
        {
            Scene = scene;
            IsApp = isApp;
        }

        public static ContentOwner Of(Scene scene) => new(scene, false);
    }

    internal sealed class ContentRecord
    {
        public string Path;
        public Type Type;
        public ContentState State;
        public readonly HashSet<Scene> Scenes = new();
        public bool App;
        public IContentLease Lease;
        public Task<Object> Loading;
        public int LastUsedFrame;
        public long Size;

        public Object Asset => Lease?.Asset;
        public bool HasOwners => App || Scenes.Count > 0;
    }
}
