using UnityEngine;

namespace Showcase
{
    public sealed class Projectile : MonoBehaviour
    {
        public Enemy Target;
        public float Damage;
        public float SplashRadius;
        public float Speed;
        public bool Lobbed;
        public Battle Battle;

        private Vector3 start;
        private Vector3 end;
        private float travelled;
        private float duration;

        private void Start()
        {
            start = transform.position;
            end = Target != null ? Target.transform.position : start;
            duration = Mathf.Max(0.15f, Vector3.Distance(start, end) / Speed);
        }

        private void Update()
        {
            if (Target != null && Target.IsAlive) end = Target.transform.position + Vector3.up * 0.6f;

            if (Lobbed)
            {
                travelled += Time.deltaTime;
                var t = Mathf.Clamp01(travelled / duration);
                var position = Vector3.Lerp(start, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 3f);
                transform.LookAt(position);
                transform.position = position;
                if (t >= 1f) Land();
                return;
            }

            var toEnd = end - transform.position;
            var step = Speed * Time.deltaTime;
            if (toEnd.magnitude <= step)
            {
                Land();
                return;
            }
            transform.rotation = Quaternion.LookRotation(toEnd);
            transform.position += toEnd.normalized * step;
        }

        private void Land()
        {
            if (SplashRadius > 0f) Battle.Explode(end, SplashRadius, Damage);
            else if (Target != null) Target.Hit(Damage);
            Destroy(gameObject);
        }
    }
}
