using UnityEngine;

namespace Showcase
{
    // 폭발 표시. 커지면서 어두워지고 사라짐
    public sealed class Burst : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_Emission");

        public float Radius = 2f;
        public float Duration = 0.35f;

        private float age;
        private Renderer[] renderers;
        private MaterialPropertyBlock block;

        private void Start()
        {
            renderers = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();
            transform.localScale = Vector3.one * 0.2f;
        }

        private void Update()
        {
            age += Time.deltaTime;
            var t = Mathf.Clamp01(age / Duration);
            transform.localScale = Vector3.one * Mathf.Lerp(0.2f, Radius * 2f, 1f - (1f - t) * (1f - t));
            var color = Color.Lerp(new Color(1f, 0.85f, 0.35f), new Color(0.35f, 0.12f, 0.05f), t);
            block.SetColor(ColorId, color);
            block.SetFloat(EmissionId, 1f - t);
            foreach (var r in renderers) r.SetPropertyBlock(block);
            if (t >= 1f) Destroy(gameObject);
        }
    }
}
