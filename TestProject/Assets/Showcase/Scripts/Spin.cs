using UnityEngine;

namespace Showcase
{
    // 로비 진열용
    public sealed class Spin : MonoBehaviour
    {
        public float DegreesPerSecond = 30f;

        private void Update() => transform.Rotate(0f, DegreesPerSecond * Time.deltaTime, 0f, Space.World);
    }
}
