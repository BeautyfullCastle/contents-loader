using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace H.ContentsLoader.Integration.Editor
{
    // 의존 애셋의 배정을 빼면 Unity가 파일 전체가 아니라 참조된 하위 오브젝트만 넣는지 확인
    // ContentsBuilder.SelectImplicitMembers가 기대는 Unity 동작임
    public class ImplicitSubObjectTests
    {
        private const string Dir = "Assets/_ImplicitProbe";
        private const string MeshesPath = Dir + "/Meshes.asset";
        private const string PrefabPath = Dir + "/Probe.prefab";
        private const string Output = "Temp/ImplicitProbe";

        [OneTimeSetUp]
        public void CreateAssets()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "_ImplicitProbe");

            // 한 파일에 작은 메시와 큰 메시. 프리팹은 작은 메시만 씀 (FBX에서 메시 하나만 쓰는 상황)
            var small = new Mesh { name = "Small", vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
            var big = new Mesh { name = "Big" };
            var random = new System.Random(1);
            var vertices = new Vector3[60000];
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] = new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
            big.vertices = vertices;
            big.triangles = Enumerable.Range(0, vertices.Length / 3 * 3).ToArray();

            AssetDatabase.CreateAsset(small, MeshesPath);
            AssetDatabase.AddObjectToAsset(big, MeshesPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MeshesPath, ImportAssetOptions.ForceUpdate);

            var go = new GameObject("Probe");
            // 메인 오브젝트 이름은 Unity가 파일 이름으로 바꾸므로 정점 수로 찾음
            go.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAllAssetsAtPath(MeshesPath).OfType<Mesh>().Single(m => m.vertexCount == 3);
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
        }

        [OneTimeTearDown]
        public void DeleteAssets()
        {
            AssetDatabase.DeleteAsset(Dir);
            if (Directory.Exists(Output)) Directory.Delete(Output, true);
        }

        private static long BuildSize(string name, params string[] assets)
        {
            // 출력 폴더 이름이 매니페스트 번들 이름이 되므로 번들 이름과 달라야 함
            var output = $"{Output}/out-{name}";
            Directory.CreateDirectory(output);
            var manifest = BuildPipeline.BuildAssetBundles(output,
                new[] { new AssetBundleBuild { assetBundleName = name, assetNames = assets } },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                EditorUserBuildSettings.activeBuildTarget);
            Assert.That(manifest, Is.Not.Null);
            return new FileInfo($"{output}/{name}").Length;
        }

        [Test]
        public void UnassignedDependencyIncludesOnlyReferencedObjects()
        {
            var implicitSize = BuildSize("implicit", PrefabPath);
            var explicitSize = BuildSize("explicit", PrefabPath, MeshesPath);

            TestContext.WriteLine($"implicit {implicitSize} bytes, explicit {explicitSize} bytes");
            Assert.That(implicitSize * 10, Is.LessThan(explicitSize), "배정을 빼면 안 쓰는 큰 메시가 빠져야 함");
        }
    }
}
