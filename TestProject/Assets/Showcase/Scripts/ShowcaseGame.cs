using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using H.ContentsLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Showcase
{
    // 쇼케이스 진입점. 로비(이 씬)와 전투(런타임에 만드는 씬)를 오감
    public sealed class ShowcaseGame : MonoBehaviour
    {
        private enum State
        {
            Lobby,
            Battle,
        }

        // Download()의 0~1 진행률을 바이트와 속도로 바꿔 보여줌
        private sealed class DownloadMeter : IProgress<float>
        {
            public long Total;
            public float Value;
            public double StartedAt;
            private double sampleTime;
            private float sampleValue;
            public double BytesPerSecond;

            public void Report(float value)
            {
                Value = value;
                var now = Time.realtimeSinceStartupAsDouble;
                if (now - sampleTime < 0.5) return;
                if (sampleTime > 0)
                {
                    var instant = (value - sampleValue) * Total / (now - sampleTime);
                    BytesPerSecond = BytesPerSecond <= 0 ? instant : BytesPerSecond * 0.6 + instant * 0.4;
                }
                sampleTime = now;
                sampleValue = value;
            }

            public long Received => (long)(Value * Total);
        }

        private static readonly List<string> LogLines = new();

        private State state;
        private Scene lobbyScene;
        private Scene battleScene;
        private Battle battle;
        private Camera mainCamera;
        private GameObject lobbyRoot;
        private GameObject titanPreview;

        private long dlcRemaining = -1;
        private DownloadMeter meter;
        private Task downloading;
        private Task<GameObject> previewing;
        private string error;

        public static void Log(string line)
        {
            var text = $"[{DateTime.Now:HH:mm:ss}] {line}";
            Debug.Log($"[Showcase] {line}");
            LogLines.Add(text);
            if (LogLines.Count > 9) LogLines.RemoveAt(0);
        }

        // CLI 자동 검증에서 씀
        public static ShowcaseGame Instance { get; private set; }
        public Battle CurrentBattle => battle;
        public long DlcRemaining => dlcRemaining;
        public float DownloadProgress => meter?.Value ?? 0f;
        public bool IsDownloading => downloading != null;
        public string Error => error;

        private void Start()
        {
            Instance = this;
            LogLines.Clear();
            lobbyScene = gameObject.scene;
            mainCamera = Camera.main;
            BuildLobby();
            RefreshDlcSize();
        }

        private void BuildLobby()
        {
            var watch = Stopwatch.StartNew();
            // 로비 애셋은 이 씬 소유라서 전투를 다녀와도 그대로 있음
            var meadow = this.LoadContent<GameObject>(ShowcaseContent.Meadow);
            var cannon = this.LoadContent<GameObject>(ShowcaseContent.Cannon);
            var crossbow = this.LoadContent<GameObject>(ShowcaseContent.Crossbow);
            var grunt = this.LoadContent<GameObject>(ShowcaseContent.Grunt);
            var runner = this.LoadContent<GameObject>(ShowcaseContent.Runner);
            Log($"로비: 빌트인 애셋 5개 동기 로드 {watch.Elapsed.TotalMilliseconds:0.0}ms");

            lobbyRoot = new GameObject("Lobby");
            Place(meadow, Vector3.zero, 0f, 1f);
            Place(cannon, new Vector3(-0.5f, 0f, 0f), 25f, 1.3f);
            Place(crossbow, new Vector3(2.6f, 0f, 1f), -25f, 1.3f);
            Place(grunt, new Vector3(5.4f, 0f, -0.5f), 40f, 1.3f);
            Place(runner, new Vector3(7.8f, 0f, 0.5f), -40f, 1.3f);
            SetLobbyCamera();
        }

        private GameObject Place(GameObject prefab, Vector3 position, float spin, float scale)
        {
            var instance = Instantiate(prefab, position, Quaternion.Euler(0f, 160f, 0f), lobbyRoot.transform);
            instance.transform.localScale *= scale;
            if (spin != 0f) instance.AddComponent<Spin>().DegreesPerSecond = spin;
            return instance;
        }

        private void SetLobbyCamera()
        {
            mainCamera.transform.SetPositionAndRotation(new Vector3(0.5f, 5.5f, -12f), Quaternion.Euler(18f, 0f, 0f));
            mainCamera.fieldOfView = 50f;
        }

        private void SetBattleCamera()
        {
            mainCamera.transform.SetPositionAndRotation(new Vector3(0f, 36f, -27f), Quaternion.Euler(55f, 0f, 0f));
            mainCamera.fieldOfView = 50f;
        }

        private void RefreshDlcSize()
        {
            dlcRemaining = Content.GetDownloadSize(ShowcaseContent.DlcFolder);
        }

        private bool DlcInstalled => dlcRemaining == 0;

        public async void DownloadDlc()
        {
            error = null;
            meter = new DownloadMeter { Total = dlcRemaining, StartedAt = Time.realtimeSinceStartupAsDouble };
            Log($"DLC 다운로드 시작: {Mb(dlcRemaining)}");
            downloading = Content.Download(ShowcaseContent.DlcFolder, meter);
            try
            {
                await downloading;
                Log($"DLC 다운로드 완료: {Time.realtimeSinceStartupAsDouble - meter.StartedAt:0.0}초");
            }
            catch (Exception e)
            {
                error = e.Message;
                Log($"DLC 다운로드 실패: {e.Message}");
            }
            finally
            {
                downloading = null;
                if (this != null) RefreshDlcSize();
            }
        }

        // 폴더 전체를 받지 않고 보스 하나만 바로 부름. 필요한 팩만 받음
        public async void PreviewTitan()
        {
            error = null;
            var before = Content.GetDownloadSize(ShowcaseContent.DlcFolder);
            var watch = Stopwatch.StartNew();
            previewing = this.LoadContentAsync<GameObject>(ShowcaseContent.Titan);
            try
            {
                var titan = await previewing;
                if (this == null) return;
                RefreshDlcSize();
                Log($"타이탄 LoadAsync {watch.Elapsed.TotalSeconds:0.0}초 · 받은 양 {Mb(before - dlcRemaining)}");
                if (titanPreview == null) titanPreview = Place(titan, new Vector3(4f, 0f, 7f), 15f, 0.75f);
            }
            catch (Exception e)
            {
                error = e.Message;
                Log($"타이탄 로드 실패: {e.Message}");
            }
            finally
            {
                previewing = null;
            }
        }

        public void StartBattle(bool desert)
        {
            if (state == State.Battle) return;
            state = State.Battle;
            lobbyRoot.SetActive(false);

            // 전투 씬을 활성 씬으로 두고 그 안에서 로드하면 애셋이 전투 씬 소유가 됨
            battleScene = SceneManager.CreateScene("Battle");
            SceneManager.SetActiveScene(battleScene);
            battle = new GameObject("Battle").AddComponent<Battle>();
            SetBattleCamera();
            Log($"전투 씬 생성 ({(desert ? "사막 DLC" : "초원")})");
            battle.Begin(this, desert);
        }

        public async void EndBattle()
        {
            if (state != State.Battle) return;
            state = State.Lobby;
            battle = null;
            SceneManager.SetActiveScene(lobbyScene);
            var unload = SceneManager.UnloadSceneAsync(battleScene);
            Log("전투 씬 언로드 → 전투 씬 소유 애셋 자동 해제");
            lobbyRoot.SetActive(true);
            SetLobbyCamera();
            if (unload != null) await unload;
        }

        private void OnGUI()
        {
            if (state != State.Lobby)
            {
                DrawLog(BeginGui());
                return;
            }

            var scale = BeginGui();
            var panel = new Rect(24, 24, 470, 540);
            GUI.Box(panel, GUIContent.none, Styles.Bar);
            GUILayout.BeginArea(new Rect(panel.x + 24, panel.y + 18, panel.width - 48, panel.height - 36));

            GUILayout.Label("Contents Loader 쇼케이스", Styles.Title);
            GUILayout.Space(6);
            GUILayout.Label("빌트인 콘텐츠  ·  앱에 포함", Styles.Heading);
            GUILayout.Label("타워 2종, 적 2종, 투사체, 초원 맵. Content.Load로 바로 나옵니다.", Styles.Body);
            GUILayout.Space(14);

            GUILayout.Label("DLC: 사막 전장  ·  다운로드", Styles.Heading);
            GUILayout.Label("4K 사막 지형, 보스 타이탄, 전투 음악.", Styles.Body);
            GUILayout.Space(8);

            if (downloading != null) DrawProgress();
            else if (dlcRemaining < 0) GUILayout.Label("크기 확인 중…", Styles.Body);
            else if (DlcInstalled) GUILayout.Label("설치됨", Styles.Good);
            else if (GUILayout.Button($"DLC 받기  ({Mb(dlcRemaining)})", GUILayout.Height(46))) DownloadDlc();

            GUILayout.Space(6);
            GUI.enabled = downloading == null && previewing == null && titanPreview == null;
            var previewLabel = previewing != null ? "타이탄 받는 중…" : titanPreview != null ? "타이탄 미리보기 중" : "보스만 먼저 불러보기 (LoadAsync)";
            if (GUILayout.Button(previewLabel, GUILayout.Height(38))) PreviewTitan();
            GUI.enabled = true;

            GUILayout.Space(18);
            GUILayout.Label("전투", Styles.Heading);
            GUI.enabled = downloading == null;
            if (GUILayout.Button("초원에서 전투 (빌트인만)", GUILayout.Height(46))) StartBattle(false);
            GUI.enabled = downloading == null;
            var desertLabel = DlcInstalled ? "사막에서 전투 (DLC)" : "사막에서 전투 (들어가면서 받음)";
            if (GUILayout.Button(desertLabel, GUILayout.Height(46))) StartBattle(true);
            GUI.enabled = true;

            if (error != null) GUILayout.Label(error, Styles.Bad);
            GUILayout.EndArea();

            DrawLog(scale);
        }

        private void DrawProgress()
        {
            var rect = GUILayoutUtility.GetRect(10, 30, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.1f, 0.1f, 0.12f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(0.35f, 0.75f, 1f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * meter.Value, rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, $"{meter.Value * 100f:0}%", Styles.Center);

            string detail;
            if (meter.Value >= 0.999f) detail = "설치 중 (LZMA → LZ4 재압축)";
            else
            {
                var speed = meter.BytesPerSecond;
                var eta = speed > 1 ? (meter.Total - meter.Received) / speed : 0;
                detail = $"{Mb(meter.Received)} / {Mb(meter.Total)}   ·   {Mb((long)speed)}/s   ·   남은 시간 {eta:0}초";
            }
            GUILayout.Label(detail, Styles.Body);
        }

        private static void DrawLog(float scale)
        {
            var height = Screen.height / scale;
            var rect = new Rect(24, height - 24 - 22 * LogLines.Count - 20, 700, 22 * LogLines.Count + 20);
            if (LogLines.Count == 0) return;
            GUI.Box(rect, GUIContent.none, Styles.Bar);
            for (var i = 0; i < LogLines.Count; i++)
                GUI.Label(new Rect(rect.x + 12, rect.y + 10 + i * 22, rect.width - 24, 22), LogLines[i], Styles.Small);
        }

        private static string Mb(long bytes) => $"{bytes / (1024f * 1024f):0.0} MB";

        // 고해상도 화면에서도 같은 크기로 보이게 높이 900 기준으로 맞춤
        public static float BeginGui()
        {
            var scale = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            Styles.Ensure();
            return scale;
        }

        public static void Panel(Rect rect, string title)
        {
            GUI.Box(rect, GUIContent.none, Styles.Bar);
            GUI.Label(new Rect(rect.x, rect.y + 20, rect.width, 60), title, Styles.Banner);
        }

        public static class Styles
        {
            public static GUIStyle Bar, Title, Heading, Body, Small, Hud, Banner, Center, Good, Bad;

            public static void Ensure()
            {
                if (Bar != null) return;
                var background = new Texture2D(1, 1);
                background.SetPixel(0, 0, new Color(0.07f, 0.08f, 0.1f, 0.82f));
                background.Apply();
                Bar = new GUIStyle { normal = { background = background } };
                Title = Text(26, FontStyle.Bold, Color.white);
                Heading = Text(18, FontStyle.Bold, new Color(0.75f, 0.85f, 1f));
                Body = Text(15, FontStyle.Normal, new Color(0.88f, 0.88f, 0.9f));
                Body.wordWrap = true;
                Small = Text(13, FontStyle.Normal, new Color(0.8f, 0.82f, 0.85f));
                Hud = Text(19, FontStyle.Bold, Color.white);
                Banner = Text(30, FontStyle.Bold, new Color(1f, 0.9f, 0.55f));
                Banner.alignment = TextAnchor.MiddleCenter;
                Center = Text(14, FontStyle.Bold, Color.white);
                Center.alignment = TextAnchor.MiddleCenter;
                Good = Text(17, FontStyle.Bold, new Color(0.5f, 0.95f, 0.55f));
                Bad = Text(14, FontStyle.Normal, new Color(1f, 0.5f, 0.45f));
                Bad.wordWrap = true;
                GUI.skin.button.fontSize = 16;
            }

            private static GUIStyle Text(int size, FontStyle style, Color color)
            {
                return new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style, normal = { textColor = color } };
            }
        }
    }
}
