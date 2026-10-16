using System;
using UnityEngine;

namespace Showcase
{
    // 프리팹에는 스크립트를 넣지 않고 로드한 뒤 붙임. 콘텐츠는 데이터만 담음
    public sealed class Enemy : MonoBehaviour
    {
        public float MaxHealth;
        public float Health;
        public float Speed;
        public int Reward;
        public int Damage;
        public string DisplayName;

        public Action<Enemy> Killed;
        public Action<Enemy> Escaped;

        private int nextPoint = 1;
        private Transform body;
        private float bobPhase;
        private float baseBodyY;

        public bool IsAlive => Health > 0f;
        public float Progress { get; private set; }

        private void Start()
        {
            body = transform.Find("Body");
            if (body != null) baseBodyY = body.localPosition.y;
            bobPhase = UnityEngine.Random.value * 10f;
        }

        private void Update()
        {
            if (!IsAlive) return;
            var path = ShowcaseContent.Path;
            var target = path[nextPoint];
            var step = Speed * Time.deltaTime;
            var position = transform.position;
            var toTarget = target - position;
            toTarget.y = 0f;

            if (toTarget.magnitude <= step)
            {
                transform.position = new Vector3(target.x, position.y, target.z);
                Progress += toTarget.magnitude;
                if (++nextPoint >= path.Length)
                {
                    Escaped?.Invoke(this);
                    Destroy(gameObject);
                    return;
                }
            }
            else
            {
                var direction = toTarget.normalized;
                transform.position += direction * step;
                Progress += step;
                var look = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 10f * Time.deltaTime);
            }

            if (body != null)
            {
                bobPhase += Time.deltaTime * Speed * 4f;
                var p = body.localPosition;
                p.y = baseBodyY + Mathf.Abs(Mathf.Sin(bobPhase)) * 0.15f * transform.localScale.y;
                body.localPosition = p;
            }
        }

        public void Hit(float damage)
        {
            if (!IsAlive) return;
            Health -= damage;
            if (Health > 0f) return;
            Killed?.Invoke(this);
            Destroy(gameObject);
        }
    }
}
