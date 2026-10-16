using System;
using H.ContentsLoader;
using UnityEngine;

namespace H.ContentsLoader.Samples
{
    // Contents 폴더에 Towers/Cannon(Built-in)과 Bosses/Dragon(Download) 프리팹이 있다고 가정함
    public sealed class ContentsLoaderSample : MonoBehaviour
    {
        [SerializeField] private string builtInPath = "Towers/Cannon";
        [SerializeField] private string downloadPath = "Bosses/Dragon";
        [SerializeField] private string downloadFolder = "Bosses";

        private async void Start()
        {
            var tower = this.LoadContent<GameObject>(builtInPath);
            if (tower != null) Instantiate(tower, Vector3.left * 2, Quaternion.identity);

            Debug.Log($"Download size of {downloadFolder}: {Content.GetDownloadSize(downloadFolder)} bytes");
            try
            {
                await Content.Download(downloadFolder, new Progress<float>(p => Debug.Log($"Downloading {p:P0}")));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Download failed: {e.Message}");
                return;
            }

            var boss = this.LoadContent<GameObject>(downloadPath);
            if (boss != null) Instantiate(boss, Vector3.right * 2, Quaternion.identity);
        }
    }
}
