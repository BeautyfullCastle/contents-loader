using UnityEngine;

namespace Showcase
{
    public sealed class Tower : MonoBehaviour
    {
        public enum Kind
        {
            Cannon,
            Crossbow,
        }

        public Kind Type;
        public float Range;
        public float Cooldown;
        public float Damage;
        public float SplashRadius;
        public Battle Battle;

        private Transform head;
        private Transform muzzle;
        private float reload;

        private void Start()
        {
            head = transform.Find("Head");
            muzzle = head != null ? head.Find("Muzzle") : null;
            if (muzzle == null) muzzle = head != null ? head : transform;
        }

        private void Update()
        {
            reload -= Time.deltaTime;
            var target = Battle.FindTarget(transform.position, Range);
            if (target == null) return;

            if (head != null)
            {
                var direction = target.transform.position - head.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.001f)
                {
                    var look = Quaternion.LookRotation(direction, Vector3.up);
                    head.rotation = Quaternion.RotateTowards(head.rotation, look, 540f * Time.deltaTime);
                }
            }

            if (reload > 0f) return;
            reload = Cooldown;
            Battle.Fire(this, muzzle.position, target);
        }
    }
}
