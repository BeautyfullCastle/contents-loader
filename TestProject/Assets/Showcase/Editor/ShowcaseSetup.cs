using System;
using System.IO;
using H.ContentsLoader;
using H.ContentsLoader.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Showcase.Editor
{
    // 쇼케이스 애셋을 전부 코드로 만듦. 대용량 텍스처와 음악은 저장소에 넣지 않고 여기서 생성함
    public static class ShowcaseSetup
    {
        private const string Root = "Assets/Showcase/Generated";
        private const string Art = Root + "/Art";
        private const string BuiltInContents = Root + "/Contents";
        private const string DlcArt = Root + "/DLC/Art";
        private const string DlcContents = Root + "/DLC/Contents";
        private const string ShaderPath = "Assets/Showcase/Art/ShowcaseLit.shader";
        public const string ScenePath = Root + "/Showcase.unity";

        private const int DesertSize = 4096;
        private const int DownloadSpeedKBps = 6 * 1024;

        private static Shader shader;

        [MenuItem("Tools/Contents Loader/Showcase/Setup (Generate, Build, Open)")]
        public static void Setup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            try
            {
                Generate();
                Configure();
                EditorUtility.DisplayProgressBar("Showcase", "콘텐츠 빌드 (에디터 플레이 모드용)", 0.9f);
                var built = ContentsBuilder.BuildForPlayMode();
                EditorUtility.ClearProgressBar();
                CreateScene();
                Debug.Log(built
                    ? "[Showcase] 준비 완료. Play를 누르세요. 다운로드를 다시 보려면 Tools > Contents Loader > Clear Play Mode Download Cache."
                    : "[Showcase] 콘텐츠 빌드에 실패했습니다. 콘솔 오류를 확인하세요.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Tools/Contents Loader/Showcase/Rebuild Content")]
        private static void Rebuild() => ContentsBuilder.BuildForPlayMode();

        private static void Generate()
        {
            foreach (var folder in new[]
                     {
                         Art, BuiltInContents + "/Showcase/Towers", BuiltInContents + "/Showcase/Enemies",
                         BuiltInContents + "/Showcase/Projectiles", BuiltInContents + "/Showcase/Effects",
                         BuiltInContents + "/Showcase/Maps", DlcArt, DlcContents + "/ShowcaseDLC/Maps",
                         DlcContents + "/ShowcaseDLC/Boss", DlcContents + "/ShowcaseDLC/Audio",
                     })
                EnsureFolder(folder);

            AssetDatabase.ImportAsset(ShaderPath);
            shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) throw new InvalidOperationException($"{ShaderPath}이 없습니다.");

            Progress("초원 텍스처", 0.02f);
            var meadowTex = WriteTexture($"{Art}/Meadow.png", 512, MeadowPixel, false);
            Progress("사막 텍스처 4K (수십 초 걸립니다)", 0.05f);
            var desertTex = WriteTexture($"{DlcArt}/Desert.png", DesertSize, DesertPixel, true);
            Progress("타이탄 텍스처", 0.6f);
            var titanTex = WriteTexture($"{DlcArt}/TitanArmor.png", 1024, TitanPixel, false);
            Progress("전투 음악", 0.7f);
            WriteMusic($"{DlcContents}/ShowcaseDLC/Audio/BattleTheme.wav");

            Progress("머티리얼과 프리팹", 0.8f);
            var stone = Mat(Art, "Stone", new Color(0.62f, 0.6f, 0.58f));
            var metal = Mat(Art, "Metal", new Color(0.32f, 0.35f, 0.4f));
            var dark = Mat(Art, "Dark", new Color(0.13f, 0.13f, 0.15f));
            var wood = Mat(Art, "Wood", new Color(0.55f, 0.37f, 0.2f));
            var brass = Mat(Art, "Brass", new Color(0.88f, 0.67f, 0.25f));
            var grunt = Mat(Art, "GruntSkin", new Color(0.36f, 0.66f, 0.3f));
            var runner = Mat(Art, "RunnerSkin", new Color(0.96f, 0.74f, 0.18f));
            var eye = Mat(Art, "Eye", Color.white, null, 0.7f);
            var slot = Mat(Art, "Slot", Color.white);
            var burst = Mat(Art, "Burst", new Color(1f, 0.8f, 0.3f), null, 1f);
            var meadow = Mat(Art, "Meadow", Color.white, meadowTex);
            var desert = Mat(DlcArt, "Desert", Color.white, desertTex);
            var armor = Mat(DlcArt, "TitanArmor", Color.white, titanTex);
            var glow = Mat(DlcArt, "TitanGlow", new Color(1f, 0.35f, 0.2f), null, 1f);

            var s = BuiltInContents + "/Showcase";
            Save(Cannon(stone, brass, metal, dark), $"{s}/Towers/Cannon.prefab");
            Save(Crossbow(wood, metal, brass), $"{s}/Towers/Crossbow.prefab");
            Save(Grunt(grunt, eye), $"{s}/Enemies/Grunt.prefab");
            Save(Runner(runner, eye, dark), $"{s}/Enemies/Runner.prefab");
            Save(Single("Shell", PrimitiveType.Sphere, Vector3.one * 0.35f, dark), $"{s}/Projectiles/Shell.prefab");
            Save(Single("Bolt", PrimitiveType.Cube, new Vector3(0.07f, 0.07f, 0.8f), brass), $"{s}/Projectiles/Bolt.prefab");
            Save(Single("Burst", PrimitiveType.Sphere, Vector3.one, burst), $"{s}/Effects/Burst.prefab");
            Save(Slot(slot), $"{s}/Maps/Slot.prefab");
            Save(Ground("Meadow", meadow), $"{s}/Maps/Meadow.prefab");

            var d = DlcContents + "/ShowcaseDLC";
            Save(Ground("Desert", desert), $"{d}/Maps/Desert.prefab");
            Save(Titan(armor, glow, dark), $"{d}/Boss/Titan.prefab");
            AssetDatabase.SaveAssets();
        }

        private static void Configure()
        {
            var settings = ContentsSettings.GetOrCreate();
            settings.SetDelivery(DlcContents, ContentDelivery.Download);
            settings.PlayModeSource = PlayModeSource.Bundles;
            // 로컬 HTTP 서버로 받아서 실제 다운로드 경로(UnityWebRequest, 이어받기)와 로딩바를 그대로 봄
            settings.EditorDownloadSource = EditorDownloadSource.LocalServer;
            if (settings.EditorDownloadSpeedKBps <= 0) settings.EditorDownloadSpeedKBps = DownloadSpeedKBps;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("Showcase").AddComponent<ShowcaseGame>();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // ---------- 프리팹 ----------

        private static GameObject Cannon(Material stone, Material brass, Material metal, Material dark)
        {
            var root = new GameObject("Cannon");
            Part(PrimitiveType.Cylinder, root.transform, "Base", new Vector3(0, 0.3f, 0), new Vector3(1.5f, 0.3f, 1.5f), stone);
            Part(PrimitiveType.Cylinder, root.transform, "Ring", new Vector3(0, 0.62f, 0), new Vector3(1.6f, 0.05f, 1.6f), brass);
            var head = Empty(root.transform, "Head", new Vector3(0, 1f, 0));
            Part(PrimitiveType.Sphere, head, "Turret", Vector3.zero, Vector3.one, metal);
            Part(PrimitiveType.Cylinder, head, "Barrel", new Vector3(0, 0.05f, 0.6f), new Vector3(0.32f, 0.55f, 0.32f), dark, new Vector3(90, 0, 0));
            Empty(head, "Muzzle", new Vector3(0, 0.05f, 1.15f));
            return root;
        }

        private static GameObject Crossbow(Material wood, Material metal, Material brass)
        {
            var root = new GameObject("Crossbow");
            Part(PrimitiveType.Cylinder, root.transform, "Base", new Vector3(0, 0.45f, 0), new Vector3(1.1f, 0.45f, 1.1f), wood);
            Part(PrimitiveType.Cylinder, root.transform, "Cap", new Vector3(0, 0.92f, 0), new Vector3(1.2f, 0.04f, 1.2f), brass);
            var head = Empty(root.transform, "Head", new Vector3(0, 1.2f, 0));
            Part(PrimitiveType.Cube, head, "Stock", Vector3.zero, new Vector3(0.28f, 0.25f, 1.3f), wood);
            Part(PrimitiveType.Cube, head, "Bow", new Vector3(0, 0.05f, 0.45f), new Vector3(1.8f, 0.1f, 0.14f), metal);
            Empty(head, "Muzzle", new Vector3(0, 0.1f, 0.75f));
            return root;
        }

        private static GameObject Grunt(Material skin, Material eye)
        {
            var root = new GameObject("Grunt");
            var body = Empty(root.transform, "Body", Vector3.zero);
            Part(PrimitiveType.Capsule, body, "Torso", new Vector3(0, 0.62f, 0), new Vector3(0.9f, 0.55f, 0.9f), skin);
            Part(PrimitiveType.Sphere, body, "EyeL", new Vector3(-0.17f, 0.92f, 0.38f), Vector3.one * 0.16f, eye);
            Part(PrimitiveType.Sphere, body, "EyeR", new Vector3(0.17f, 0.92f, 0.38f), Vector3.one * 0.16f, eye);
            Empty(root.transform, "HealthBar", new Vector3(0, 1.6f, 0));
            return root;
        }

        private static GameObject Runner(Material skin, Material eye, Material dark)
        {
            var root = new GameObject("Runner");
            var body = Empty(root.transform, "Body", Vector3.zero);
            Part(PrimitiveType.Sphere, body, "Ball", new Vector3(0, 0.45f, 0), Vector3.one * 0.75f, skin);
            Part(PrimitiveType.Cube, body, "EarL", new Vector3(-0.22f, 0.85f, 0), new Vector3(0.1f, 0.3f, 0.1f), dark, new Vector3(0, 0, 20));
            Part(PrimitiveType.Cube, body, "EarR", new Vector3(0.22f, 0.85f, 0), new Vector3(0.1f, 0.3f, 0.1f), dark, new Vector3(0, 0, -20));
            Part(PrimitiveType.Sphere, body, "Eye", new Vector3(0, 0.55f, 0.33f), Vector3.one * 0.18f, eye);
            Empty(root.transform, "HealthBar", new Vector3(0, 1.3f, 0));
            return root;
        }

        private static GameObject Titan(Material armor, Material glow, Material dark)
        {
            var root = new GameObject("Titan");
            var body = Empty(root.transform, "Body", Vector3.zero);
            Part(PrimitiveType.Cube, body, "LegL", new Vector3(-0.65f, 1f, 0), new Vector3(0.8f, 2f, 0.8f), armor);
            Part(PrimitiveType.Cube, body, "LegR", new Vector3(0.65f, 1f, 0), new Vector3(0.8f, 2f, 0.8f), armor);
            Part(PrimitiveType.Cube, body, "Torso", new Vector3(0, 3.1f, 0), new Vector3(2.4f, 2.2f, 1.4f), armor);
            Part(PrimitiveType.Sphere, body, "Core", new Vector3(0, 3.2f, 0.62f), Vector3.one * 0.75f, glow);
            Part(PrimitiveType.Cube, body, "Head", new Vector3(0, 4.7f, 0), new Vector3(1f, 0.9f, 0.9f), armor);
            Part(PrimitiveType.Cube, body, "EyeL", new Vector3(-0.22f, 4.78f, 0.46f), new Vector3(0.26f, 0.12f, 0.05f), glow);
            Part(PrimitiveType.Cube, body, "EyeR", new Vector3(0.22f, 4.78f, 0.46f), new Vector3(0.26f, 0.12f, 0.05f), glow);
            Part(PrimitiveType.Sphere, body, "ShoulderL", new Vector3(-1.45f, 4f, 0), Vector3.one * 0.9f, dark);
            Part(PrimitiveType.Sphere, body, "ShoulderR", new Vector3(1.45f, 4f, 0), Vector3.one * 0.9f, dark);
            Part(PrimitiveType.Cube, body, "ArmL", new Vector3(-1.65f, 2.8f, 0), new Vector3(0.6f, 2.2f, 0.6f), armor, new Vector3(0, 0, 8));
            Part(PrimitiveType.Cube, body, "ArmR", new Vector3(1.65f, 2.8f, 0), new Vector3(0.6f, 2.2f, 0.6f), armor, new Vector3(0, 0, -8));
            Empty(root.transform, "HealthBar", new Vector3(0, 5.8f, 0));
            return root;
        }

        private static GameObject Slot(Material material)
        {
            var root = new GameObject("Slot");
            Part(PrimitiveType.Cylinder, root.transform, "Pad", new Vector3(0, 0.04f, 0), new Vector3(2.4f, 0.04f, 2.4f), material);
            return root;
        }

        // 쿼드를 눕혀서 uv (0,0)이 월드 (-20,-20)에 오게 함. 텍스처에 그린 길과 실제 경로가 맞음
        private static GameObject Ground(string name, Material material)
        {
            var root = new GameObject(name);
            Part(PrimitiveType.Quad, root.transform, "Ground", Vector3.zero, new Vector3(ShowcaseContent.MapSize, ShowcaseContent.MapSize, 1f), material, new Vector3(90, 0, 0));
            return root;
        }

        private static GameObject Single(string name, PrimitiveType type, Vector3 scale, Material material)
        {
            var root = new GameObject(name);
            Part(type, root.transform, "Mesh", Vector3.zero, scale, material);
            return root;
        }

        private static Transform Part(PrimitiveType type, Transform parent, string name, Vector3 position, Vector3 scale, Material material, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        private static Transform Empty(Transform parent, string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        private static void Save(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static Material Mat(string folder, string name, Color color, Texture texture = null, float emission = 0f)
        {
            var path = $"{folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_Color", color);
            material.SetFloat("_Emission", emission);
            material.SetTexture("_MainTex", texture);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------- 텍스처 ----------

        private delegate Color32 PixelShader(float worldX, float worldZ, float pathDistance, uint hash);

        private static Texture2D WriteTexture(string path, int size, PixelShader pixel, bool large)
        {
            var pixels = new Color32[size * size];
            var half = ShowcaseContent.MapSize / 2f;
            for (var y = 0; y < size; y++)
            {
                if (large && (y & 255) == 0) Progress($"사막 텍스처 4K {y * 100 / size}%", 0.05f + 0.5f * y / size);
                var z = (y + 0.5f) / size * ShowcaseContent.MapSize - half;
                for (var x = 0; x < size; x++)
                {
                    var wx = (x + 0.5f) / size * ShowcaseContent.MapSize - half;
                    pixels[y * size + x] = pixel(wx, z, PathDistance(wx, z), Hash((uint)x, (uint)y));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.maxTextureSize = size;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            // 대용량 다운로드를 보이려고 사막은 무압축으로 둠
            importer.textureCompression = large ? TextureImporterCompression.Uncompressed : TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Color32 MeadowPixel(float x, float z, float pathDistance, uint hash)
        {
            var n = Fbm(x * 0.15f, z * 0.15f, 3);
            var grain = (hash & 255) / 255f - 0.5f;
            var grass = new Color(0.28f + n * 0.12f, 0.52f + n * 0.18f + grain * 0.05f, 0.22f + n * 0.05f);
            var dirt = new Color(0.58f + grain * 0.06f, 0.46f + grain * 0.05f, 0.3f);
            return Color.Lerp(dirt, grass, Mathf.SmoothStep(0f, 1f, (pathDistance - ShowcaseContent.PathHalfWidth) / 0.5f + 0.5f));
        }

        private static Color32 DesertPixel(float x, float z, float pathDistance, uint hash)
        {
            var dune = Fbm(x * 0.08f, z * 0.08f, 4);
            var ripple = Mathf.Sin(x * 3.1f + z * 1.3f + dune * 9f) * 0.5f + 0.5f;
            var rock = Fbm(x * 0.21f + 40f, z * 0.21f - 13f, 2);
            var grain = (hash & 255) / 255f - 0.5f;
            var grain2 = ((hash >> 8) & 255) / 255f - 0.5f;

            var sand = new Color(0.8f + dune * 0.1f + ripple * 0.04f + grain * 0.08f,
                0.64f + dune * 0.09f + ripple * 0.03f + grain * 0.07f,
                0.42f + dune * 0.06f + grain2 * 0.06f);
            if (rock > 0.68f)
                sand = Color.Lerp(sand, new Color(0.46f + grain * 0.1f, 0.38f + grain * 0.08f, 0.32f + grain2 * 0.06f), Mathf.Clamp01((rock - 0.68f) * 12f));

            var track = new Color(0.55f + grain * 0.09f, 0.41f + grain * 0.07f, 0.29f + grain2 * 0.05f);
            return Color.Lerp(track, sand, Mathf.SmoothStep(0f, 1f, (pathDistance - ShowcaseContent.PathHalfWidth) / 0.4f + 0.5f));
        }

        // 장갑판 무늬. x,z 대신 uv 범위로 씀
        private static Color32 TitanPixel(float x, float z, float pathDistance, uint hash)
        {
            var u = (x / ShowcaseContent.MapSize + 0.5f) * 8f;
            var v = (z / ShowcaseContent.MapSize + 0.5f) * 8f;
            var fu = u - Mathf.Floor(u);
            var fv = v - Mathf.Floor(v);
            var seam = Mathf.Min(Mathf.Min(fu, 1f - fu), Mathf.Min(fv, 1f - fv));
            var rivet = new Vector2(Mathf.Min(fu, 1f - fu), Mathf.Min(fv, 1f - fv)).magnitude < 0.09f;
            var grain = (hash & 255) / 255f - 0.5f;
            var plate = new Color(0.36f + grain * 0.05f, 0.24f + grain * 0.04f, 0.3f + grain * 0.05f);
            if (seam < 0.03f) plate *= 0.45f;
            if (rivet) plate = new Color(0.7f, 0.62f, 0.5f);
            plate.a = 1f;
            return plate;
        }

        private static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, total = 0f;
            for (var i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x, y) * amp;
                total += amp;
                x *= 2.03f;
                y *= 2.03f;
                amp *= 0.5f;
            }
            return sum / total;
        }

        private static float PathDistance(float x, float z)
        {
            var path = ShowcaseContent.Path;
            var best = float.MaxValue;
            for (var i = 0; i < path.Length - 1; i++)
            {
                float ax = path[i].x, az = path[i].z, dx = path[i + 1].x - ax, dz = path[i + 1].z - az;
                var t = Mathf.Clamp01(((x - ax) * dx + (z - az) * dz) / (dx * dx + dz * dz));
                var px = ax + dx * t - x;
                var pz = az + dz * t - z;
                best = Mathf.Min(best, px * px + pz * pz);
            }
            return Mathf.Sqrt(best);
        }

        private static uint Hash(uint x, uint y)
        {
            var h = x * 374761393u + y * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }

        // ---------- 음악 ----------

        // 140 BPM 칩튠 루프. Am F C G 진행
        private static void WriteMusic(string path)
        {
            const int rate = 44100;
            const double step = 60.0 / 140.0 / 4.0;
            const int bars = 28;
            var frames = (int)(bars * 16 * step * rate);
            var roots = new[] { 110.0, 87.31, 130.81, 98.0 };
            var chords = new[]
            {
                new[] { 220.0, 261.63, 329.63, 440.0 }, new[] { 174.61, 220.0, 261.63, 349.23 },
                new[] { 261.63, 329.63, 392.0, 523.25 }, new[] { 196.0, 246.94, 293.66, 392.0 },
            };
            var samples = new short[frames * 2];
            var noise = 22222u;

            for (var i = 0; i < frames; i++)
            {
                var t = i / (double)rate;
                var sixteenth = (int)(t / step);
                var within = t - sixteenth * step;
                var bar = sixteenth / 16 % 4;
                var beat = sixteenth % 16;

                var arpFreq = chords[bar][(beat + sixteenth / 64) % 4] * 2.0;
                var arp = (t * arpFreq % 1.0 < 0.25 ? 1.0 : -1.0) * Math.Exp(-within * 18) * 0.16;

                var bassFreq = roots[bar] * (beat % 4 == 2 ? 2.0 : 1.0);
                var bass = (t * bassFreq % 1.0 < 0.5 ? 1.0 : -1.0) * 0.12 * Math.Exp(-(t % (step * 2)) * 4);

                var beatTime = t % (step * 4);
                var kick = Math.Sin(2 * Math.PI * (45 + 90 * Math.Exp(-beatTime * 30)) * beatTime) * Math.Exp(-beatTime * 9) * 0.5;

                noise = noise * 1664525u + 1013904223u;
                var hatTime = (t + step * 2) % (step * 4);
                var hat = ((noise >> 9) / (double)(1 << 23) - 1.0) * Math.Exp(-hatTime * 60) * 0.08;

                var intro = Math.Min(1.0, t / 2.0);
                var left = (arp * 0.7 + bass + kick + hat) * intro;
                var right = (arp * 1.0 + bass + kick + hat * 0.6) * intro;
                samples[i * 2] = (short)(Math.Clamp(left, -1, 1) * 30000);
                samples[i * 2 + 1] = (short)(Math.Clamp(right, -1, 1) * 30000);
            }

            using (var writer = new BinaryWriter(File.Create(path)))
            {
                var dataBytes = samples.Length * 2;
                writer.Write("RIFF".ToCharArray());
                writer.Write(36 + dataBytes);
                writer.Write("WAVEfmt ".ToCharArray());
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)2);
                writer.Write(rate);
                writer.Write(rate * 4);
                writer.Write((short)4);
                writer.Write((short)16);
                writer.Write("data".ToCharArray());
                writer.Write(dataBytes);
                foreach (var sample in samples) writer.Write(sample);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        // ---------- 유틸 ----------

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void Progress(string message, float value) => EditorUtility.DisplayProgressBar("Showcase 셋업", message, value);
    }
}
