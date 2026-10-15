using UnityEngine;

namespace H.ContentsLoader
{
    [AddComponentMenu("")]
    internal sealed class ContentRunner : MonoBehaviour
    {
        void LateUpdate() => ContentSystem.Tick();
    }
}
