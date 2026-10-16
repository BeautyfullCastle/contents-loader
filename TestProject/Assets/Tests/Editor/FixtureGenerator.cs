using System.IO;
using H.ContentsLoader.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace H.ContentsLoader.Integration.Editor
{
    // 통합 테스트용 애셋을 다시 만듦. 결과물은 커밋해서 씀
    public static class FixtureGenerator
    {
        private const string Art = "Assets/Art";
        private const string Game = "Assets/Game/Contents";
        private const string Dlc = "Assets/DLC/Contents";
        private const string Scenes = "Assets/Tests/Scenes";

        private const string SimpleShader = @"Shader ""ContentsLoader/Simple""
{
    Properties { _MainTex (""Texture"", 2D) = ""white"" {} }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            sampler2D _MainTex;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            fixed4 frag (v2f i) : SV_Target { return tex2D(_MainTex, i.uv); }
            ENDCG
        }
    }
}
private ";

        [MenuItem("Tools/Contents Loader/Regenerate Test Fixtures")]
        public static void Generate()
        {
            foreach (var folder in new[] { Art, "Assets/Game", "Assets/DLC", Scenes, ContentsSettings.DefaultPath })
                AssetDatabase.DeleteAsset(folder);

            CreateFolders(Art, $"{Game}/Towers", $"{Game}/UI", $"{Game}/Data", $"{Dlc}/Bosses", $"{Dlc}/Season01", Scenes);

            File.WriteAllText($"{Art}/Simple.shader", SimpleShader);
            Texture("Shared", 32, 1);
            Texture("Cannon", 16, 2);
            Texture("Archer", 16, 3);
            Texture("Goblin", 16, 4);
            Texture("Dragon", 512, 5);
            // 프리팹과 같은 경로의 텍스처. 타입으로 고르는지 확인함
            Texture("Cannon", 16, 6, $"{Game}/Towers");
            File.WriteAllText($"{Game}/Data/Config.txt", "difficulty=normal");
            AssetDatabase.Refresh();

            var standard = Shader.Find("Standard");
            var simple = AssetDatabase.LoadAssetAtPath<Shader>($"{Art}/Simple.shader");
            var shared = Material("Shared", standard);
            var cannon = Material("Cannon", standard);
            var archer = Material("Archer", standard);
            var goblin = Material("Goblin", standard);
            var dragon = Material("Dragon", simple);

            Prefab($"{Game}/Towers/Cannon.prefab", cannon, shared);
            Prefab($"{Game}/Towers/Archer.prefab", archer, shared);
            Prefab($"{Game}/UI/Shop.prefab");
            Prefab($"{Dlc}/Bosses/Dragon.prefab", dragon, shared);
            Prefab($"{Dlc}/Season01/Goblin.prefab", goblin);

            foreach (var name in new[] { "SceneA", "SceneB", "SceneC" })
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, $"{Scenes}/{name}.unity");
            }

            var settings = ContentsSettings.GetOrCreate();
            settings.SetRule(AssetDatabase.AssetPathToGUID(Dlc), ContentDelivery.Download);
            settings.RemoteUrl = "https://cdn.invalid/contents/{Platform}";
            AssetDatabase.SaveAssets();
        }

        private static void CreateFolders(params string[] paths)
        {
            foreach (var path in paths)
            {
                var parent = "Assets";
                foreach (var part in path.Substring("Assets/".Length).Split('/'))
                {
                    if (!AssetDatabase.IsValidFolder($"{parent}/{part}")) AssetDatabase.CreateFolder(parent, part);
                    parent = $"{parent}/{part}";
                }
            }
        }

        private static void Texture(string name, int size, int seed, string folder = Art)
        {
            var random = new System.Random(seed);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
            texture.SetPixels32(pixels);
            File.WriteAllBytes($"{folder}/{name}.png", texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static Material Material(string name, Shader shader)
        {
            var material = new Material(shader) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Art}/{name}.png") };
            AssetDatabase.CreateAsset(material, $"{Art}/{name}.mat");
            return material;
        }

        private static void Prefab(string path, params Material[] materials)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            foreach (var material in materials)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                part.name = material.name;
                part.transform.SetParent(root.transform);
                part.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }
    }
}
