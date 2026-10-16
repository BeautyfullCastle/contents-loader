using System;
using System.Collections.Generic;
using System.Diagnostics;
using H.ContentsLoader;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Showcase
{
    // 전투 씬에만 있음. 여기서 로드한 애셋은 전투 씬 소유라서 씬을 내리면 같이 해제됨
    public sealed class Battle : MonoBehaviour
    {
        private sealed class Wave
        {
            public int Grunts;
            public int Runners;
            public bool Boss;
        }

        private sealed class TowerSpec
        {
            public string Name;
            public string Path;
            public int Cost;
            public float Range;
            public float Cooldown;
            public float Damage;
            public float Splash;
        }

        private static readonly TowerSpec[] Specs =
        {
            new() { Name = "대포", Path = ShowcaseContent.Cannon, Cost = 60, Range = 7.5f, Cooldown = 1.3f, Damage = 40f, Splash = 2.4f },
            new() { Name = "석궁", Path = ShowcaseContent.Crossbow, Cost = 40, Range = 9f, Cooldown = 0.45f, Damage = 13f, Splash = 0f },
        };

        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<Enemy> enemies = new();
        private readonly List<Wave> waves = new();
        private readonly Dictionary<int, Tower> built = new();
        private readonly List<Transform> slotObjects = new();

        private ShowcaseGame game;
        private bool desert;
        private bool ready;
        private GameObject gruntPrefab, runnerPrefab, bossPrefab, shellPrefab, boltPrefab, burstPrefab;
        private readonly GameObject[] towerPrefabs = new GameObject[Specs.Length];
        private AudioSource music;

        private int gold = 130;
        private int lives = 20;
        private int waveIndex = -1;
        private int toSpawn;
        private float spawnTimer;
        private float breakTimer = 5f;
        private int selected;
        private int hoverSlot = -1;
        private string banner;
        private float bannerUntil;
        private bool finished;
        private bool victory;
        private MaterialPropertyBlock block;
        private Texture2D white;

        public bool IsReady => ready;

        public async void Begin(ShowcaseGame owner, bool useDlc)
        {
            game = owner;
            desert = useDlc;
            block = new MaterialPropertyBlock();
            white = Texture2D.whiteTexture;

            try
            {
                var watch = Stopwatch.StartNew();
                // Built-in은 동기로 바로 나옴
                gruntPrefab = this.LoadContent<GameObject>(ShowcaseContent.Grunt);
                runnerPrefab = this.LoadContent<GameObject>(ShowcaseContent.Runner);
                shellPrefab = this.LoadContent<GameObject>(ShowcaseContent.Shell);
                boltPrefab = this.LoadContent<GameObject>(ShowcaseContent.Bolt);
                burstPrefab = this.LoadContent<GameObject>(ShowcaseContent.Burst);
                for (var i = 0; i < Specs.Length; i++) towerPrefabs[i] = this.LoadContent<GameObject>(Specs[i].Path);
                var slotPrefab = this.LoadContent<GameObject>(ShowcaseContent.Slot);
                ShowcaseGame.Log($"전투 씬: 빌트인 애셋 8개 동기 로드 {watch.Elapsed.TotalMilliseconds:0.0}ms");

                GameObject map;
                if (desert)
                {
                    watch.Restart();
                    // 이미 받았으면 바로 열리고, 아니면 여기서 받은 뒤 열림
                    map = await this.LoadContentAsync<GameObject>(ShowcaseContent.Desert);
                    bossPrefab = await this.LoadContentAsync<GameObject>(ShowcaseContent.Titan);
                    var clip = await this.LoadContentAsync<AudioClip>(ShowcaseContent.BattleTheme);
                    if (this == null) return;
                    ShowcaseGame.Log($"전투 씬: DLC 맵·보스·음악 비동기 로드 {watch.Elapsed.TotalMilliseconds:0}ms");

                    music = gameObject.AddComponent<AudioSource>();
                    music.clip = clip;
                    music.loop = true;
                    music.volume = 0.35f;
                    music.Play();
                }
                else
                {
                    map = this.LoadContent<GameObject>(ShowcaseContent.Meadow);
                }

                Instantiate(map, Vector3.zero, Quaternion.identity).name = desert ? "Desert" : "Meadow";
                foreach (var slot in ShowcaseContent.Slots)
                    slotObjects.Add(Instantiate(slotPrefab, slot, Quaternion.identity).transform);

                BuildWaves();
                ready = true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ShowcaseGame.Log($"전투 준비 실패: {e.Message}");
                finished = true;
            }
        }

        private void BuildWaves()
        {
            for (var i = 0; i < 6; i++)
                waves.Add(new Wave { Grunts = 5 + i * 2, Runners = i * 2, Boss = i == 5 });
        }

        private void Update()
        {
            if (!ready || finished) return;
            UpdateHover();
            UpdateWaves();

            if (lives <= 0) Finish(false);
            else if (waveIndex >= waves.Count - 1 && toSpawn == 0 && enemies.Count == 0) Finish(true);
        }

        private void UpdateWaves()
        {
            if (toSpawn == 0 && enemies.Count == 0 && waveIndex < waves.Count - 1)
            {
                breakTimer -= Time.deltaTime;
                if (breakTimer <= 0f) StartNextWave();
                return;
            }

            if (toSpawn <= 0) return;
            spawnTimer -= Time.deltaTime;
            if (spawnTimer > 0f) return;
            spawnTimer = 0.75f;

            var next = spawnQueue.Dequeue();
            if (next == SpawnKind.Boss) Spawn(bossPrefab != null ? bossPrefab : gruntPrefab, true);
            else Spawn(next == SpawnKind.Runner ? runnerPrefab : gruntPrefab, false);
            toSpawn = spawnQueue.Count;
        }

        private enum SpawnKind
        {
            Grunt,
            Runner,
            Boss,
        }

        private readonly Queue<SpawnKind> spawnQueue = new();

        private void StartNextWave()
        {
            waveIndex++;
            var wave = waves[waveIndex];
            // 섞어서 내보냄. 보스는 마지막
            spawnQueue.Clear();
            for (var i = 0; i < Mathf.Max(wave.Grunts, wave.Runners); i++)
            {
                if (i < wave.Grunts) spawnQueue.Enqueue(SpawnKind.Grunt);
                if (i < wave.Runners) spawnQueue.Enqueue(SpawnKind.Runner);
            }
            if (wave.Boss) spawnQueue.Enqueue(SpawnKind.Boss);
            toSpawn = spawnQueue.Count;
            spawnTimer = 0f;
            breakTimer = 4f;
            ShowBanner(wave.Boss ? $"마지막 웨이브 · 보스 {(desert ? "타이탄" : "브루트")} 등장" : $"웨이브 {waveIndex + 1}");
        }

        private void Spawn(GameObject prefab, bool boss)
        {
            var instance = Instantiate(prefab, ShowcaseContent.Path[0], Quaternion.LookRotation(Vector3.right));
            var enemy = instance.AddComponent<Enemy>();
            var hp = 1f + waveIndex * 0.25f;
            if (boss)
            {
                enemy.DisplayName = desert ? "타이탄" : "브루트";
                enemy.MaxHealth = (desert ? 2200f : 1100f) * hp;
                enemy.Speed = 1.1f;
                enemy.Reward = 200;
                enemy.Damage = 10;
                if (!desert) instance.transform.localScale = Vector3.one * 2.2f;
            }
            else if (prefab == runnerPrefab)
            {
                enemy.MaxHealth = 35f * hp;
                enemy.Speed = 4.2f;
                enemy.Reward = 6;
                enemy.Damage = 1;
            }
            else
            {
                enemy.MaxHealth = 85f * hp;
                enemy.Speed = 2.2f;
                enemy.Reward = 8;
                enemy.Damage = 1;
            }
            enemy.Health = enemy.MaxHealth;
            enemy.Killed = OnKilled;
            enemy.Escaped = OnEscaped;
            enemies.Add(enemy);
        }

        private void OnKilled(Enemy enemy)
        {
            gold += enemy.Reward;
            enemies.Remove(enemy);
            if (enemy.DisplayName != null)
            {
                ShowBanner($"{enemy.DisplayName} 격파!");
            }
        }

        private void OnEscaped(Enemy enemy)
        {
            lives -= enemy.Damage;
            enemies.Remove(enemy);
        }

        public Enemy FindTarget(Vector3 from, float range)
        {
            Enemy best = null;
            var rangeSq = range * range;
            foreach (var enemy in enemies)
            {
                if (enemy == null || !enemy.IsAlive) continue;
                var offset = enemy.transform.position - from;
                offset.y = 0f;
                if (offset.sqrMagnitude > rangeSq) continue;
                if (best == null || enemy.Progress > best.Progress) best = enemy;
            }
            return best;
        }

        public void Fire(Tower tower, Vector3 from, Enemy target)
        {
            var cannon = tower.Type == Tower.Kind.Cannon;
            var projectile = Instantiate(cannon ? shellPrefab : boltPrefab, from, tower.transform.rotation).AddComponent<Projectile>();
            projectile.Target = target;
            projectile.Damage = tower.Damage;
            projectile.SplashRadius = tower.SplashRadius;
            projectile.Speed = cannon ? 9f : 26f;
            projectile.Lobbed = cannon;
            projectile.Battle = this;
        }

        public void Explode(Vector3 at, float radius, float damage)
        {
            var burst = Instantiate(burstPrefab, at, Quaternion.identity).AddComponent<Burst>();
            burst.Radius = radius;
            var radiusSq = radius * radius;
            foreach (var enemy in enemies.ToArray())
            {
                if (enemy == null) continue;
                var offset = enemy.transform.position - at;
                offset.y = 0f;
                if (offset.sqrMagnitude <= radiusSq) enemy.Hit(damage);
            }
        }

        private void UpdateHover()
        {
            hoverSlot = -1;
            var camera = Camera.main;
            if (camera == null) return;
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return;
            var point = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);
            for (var i = 0; i < ShowcaseContent.Slots.Length; i++)
            {
                var offset = ShowcaseContent.Slots[i] - point;
                offset.y = 0f;
                if (offset.magnitude < 1.6f) hoverSlot = i;
            }

            for (var i = 0; i < slotObjects.Count; i++)
            {
                var color = built.ContainsKey(i) ? new Color(0.35f, 0.35f, 0.38f)
                    : i == hoverSlot ? (gold >= Specs[selected].Cost ? new Color(0.4f, 0.95f, 0.5f) : new Color(0.95f, 0.4f, 0.35f))
                    : new Color(0.85f, 0.8f, 0.6f);
                block.SetColor(ColorId, color);
                foreach (var r in slotObjects[i].GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(block);
            }
        }

        private void TryBuild()
        {
            if (hoverSlot >= 0) Build(hoverSlot, selected);
        }

        public string Status => $"ready={ready} finished={finished} victory={victory} wave={waveIndex + 1}/{waves.Count} gold={gold} lives={lives} enemies={enemies.Count} towers={built.Count}";

        public bool Build(int slotIndex, int type)
        {
            if (!ready || slotIndex < 0 || slotIndex >= ShowcaseContent.Slots.Length || built.ContainsKey(slotIndex)) return false;
            var spec = Specs[type];
            if (gold < spec.Cost)
            {
                ShowBanner("골드가 부족합니다");
                return false;
            }
            gold -= spec.Cost;
            var instance = Instantiate(towerPrefabs[type], ShowcaseContent.Slots[slotIndex], Quaternion.identity);
            var tower = instance.AddComponent<Tower>();
            tower.Type = (Tower.Kind)type;
            tower.Range = spec.Range;
            tower.Cooldown = spec.Cooldown;
            tower.Damage = spec.Damage;
            tower.SplashRadius = spec.Splash;
            tower.Battle = this;
            built.Add(slotIndex, tower);
            return true;
        }

        public void AddGold(int amount) => gold += amount;

        private void Finish(bool won)
        {
            finished = true;
            victory = won;
            Time.timeScale = 1f;
            ShowcaseGame.Log(won ? "전투 승리" : "전투 패배");
        }

        private void ShowBanner(string text)
        {
            banner = text;
            bannerUntil = Time.unscaledTime + 2.5f;
        }

        private void OnDestroy() => Time.timeScale = 1f;

        private void OnGUI()
        {
            var scale = ShowcaseGame.BeginGui();
            var width = Screen.width / scale;
            var height = Screen.height / scale;

            if (!ready)
            {
                if (!finished) ShowcaseGame.Panel(new Rect(width / 2 - 200, height / 2 - 40, 400, 80), desert ? "DLC 전장 여는 중…" : "전장 여는 중…");
                else if (GUI.Button(new Rect(width / 2 - 100, height / 2 + 50, 200, 44), "로비로")) game.EndBattle();
                return;
            }

            DrawHealthBars(scale);

            // 상단 HUD
            GUI.Box(new Rect(0, 0, width, 60), GUIContent.none, ShowcaseGame.Styles.Bar);
            GUI.Label(new Rect(20, 12, 600, 36),
                $"골드 {gold}    생명 {lives}    웨이브 {Mathf.Max(0, waveIndex + 1)}/{waves.Count}    맵 {(desert ? "사막 (DLC)" : "초원 (빌트인)")}",
                ShowcaseGame.Styles.Hud);

            var x = width - 20f;
            x -= 120;
            if (GUI.Button(new Rect(x, 10, 120, 40), "로비로")) game.EndBattle();
            x -= 130;
            if (GUI.Button(new Rect(x, 10, 120, 40), Time.timeScale > 1f ? "속도 x2" : "속도 x1"))
                Time.timeScale = Time.timeScale > 1f ? 1f : 2f;
            x -= 150;
            var canSkip = toSpawn == 0 && enemies.Count == 0 && waveIndex < waves.Count - 1;
            GUI.enabled = canSkip && !finished;
            if (GUI.Button(new Rect(x, 10, 140, 40), canSkip ? $"다음 웨이브 {Mathf.CeilToInt(breakTimer)}" : "진행 중")) StartNextWave();
            GUI.enabled = true;

            // 하단 건설 바
            var bar = new Rect(width / 2 - 230, height - 76, 460, 64);
            GUI.Box(bar, GUIContent.none, ShowcaseGame.Styles.Bar);
            for (var i = 0; i < Specs.Length; i++)
            {
                var rect = new Rect(bar.x + 12 + i * 150, bar.y + 10, 140, 44);
                var label = $"{Specs[i].Name}  {Specs[i].Cost}G";
                if (GUI.Toggle(rect, selected == i, label, GUI.skin.button)) selected = i;
            }
            GUI.Label(new Rect(bar.x + 318, bar.y + 8, 140, 48), "빈 자리를\n클릭해 건설", ShowcaseGame.Styles.Small);

            if (banner != null && Time.unscaledTime < bannerUntil)
                GUI.Label(new Rect(0, 90, width, 50), banner, ShowcaseGame.Styles.Banner);

            if (finished)
            {
                var panel = new Rect(width / 2 - 220, height / 2 - 90, 440, 180);
                ShowcaseGame.Panel(panel, victory ? "승리!" : "패배");
                if (GUI.Button(new Rect(panel.x + 120, panel.y + 110, 200, 46), "로비로 돌아가기")) game.EndBattle();
            }

            var e = Event.current;
            if (!finished && e.type == EventType.MouseDown && e.button == 0)
            {
                TryBuild();
                e.Use();
            }
        }

        private void DrawHealthBars(float scale)
        {
            var camera = Camera.main;
            if (camera == null) return;
            foreach (var enemy in enemies)
            {
                if (enemy == null) continue;
                // 프리팹에 HealthBar 위치 표시가 있으면 그 위에 그림
                var marker = enemy.transform.Find("HealthBar");
                var top = marker != null ? marker.position : enemy.transform.position + Vector3.up * (1.6f * enemy.transform.localScale.y);
                var screen = camera.WorldToScreenPoint(top);
                if (screen.z < 0) continue;
                var boss = enemy.DisplayName != null;
                var w = boss ? 120f : 36f;
                var rect = new Rect(screen.x / scale - w / 2, (Screen.height - screen.y) / scale - 8, w, boss ? 9 : 5);
                GUI.color = new Color(0, 0, 0, 0.7f);
                GUI.DrawTexture(rect, white);
                GUI.color = boss ? new Color(0.95f, 0.3f, 0.25f) : new Color(0.45f, 0.9f, 0.35f);
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(enemy.Health / enemy.MaxHealth), rect.height), white);
                GUI.color = Color.white;
                if (boss) GUI.Label(new Rect(rect.x - 40, rect.y - 24, rect.width + 80, 22), enemy.DisplayName, ShowcaseGame.Styles.Center);
            }
        }
    }
}
