using UnityEngine;

namespace H.ContentsLoader
{
    [AddComponentMenu("")]
    internal sealed class ContentRunner : MonoBehaviour
    {
        private void LateUpdate() => ContentSystem.Tick();
    }
}
