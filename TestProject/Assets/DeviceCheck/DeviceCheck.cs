using System;
using System.Collections;
using H.ContentsLoader;
using UnityEngine;

// 기기 검증용. 로그를 [DeviceCheck] 접두사로 남기고 run-device-check.ps1이 logcat에서 읽음
public sealed class DeviceCheck : MonoBehaviour
{
    sealed class LogProgress : IProgress<float>
    {
        int lastDecile = -1;

        public void Report(float value)
        {
            var decile = (int)(value * 10);
            if (decile == lastDecile) return;
            lastDecile = decile;
            Debug.Log($"[DeviceCheck] progress {value:0.00}");
        }
    }

    IEnumerator Start()
    {
        Debug.Log($"[DeviceCheck] start persistentDataPath={Application.persistentDataPath}");
        var cannon = Content.Load<GameObject>("Towers/Cannon");
        Debug.Log($"[DeviceCheck] builtin {(cannon != null ? "ok" : "FAIL")}");
        Debug.Log($"[DeviceCheck] remaining {Content.GetDownloadSize("Bosses")}");

        var download = Content.Download("Bosses", new LogProgress());
        yield return new WaitUntil(() => download.IsCompleted);
        if (download.IsFaulted)
        {
            Debug.Log($"[DeviceCheck] download FAIL {download.Exception?.InnerException?.Message}");
            yield break;
        }

        var dragon = Content.Load<GameObject>("Bosses/Dragon");
        Debug.Log($"[DeviceCheck] dragon {(dragon != null ? "ok" : "FAIL")}");
    }

    void OnApplicationPause(bool paused) => Debug.Log($"[DeviceCheck] paused={paused}");
}
