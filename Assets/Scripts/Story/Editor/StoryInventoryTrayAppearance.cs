using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TakeMeToTheMoon.Story.Editor
{
    public static class StoryInventoryTrayAppearance
    {
        [MenuItem("Tools/Story/Apply Black Glass Tray")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var tray = Object.FindAnyObjectByType<StoryInventoryTray>(FindObjectsInactive.Include);
            var visual = tray != null ? tray.transform.Find("Base Visual") : null;
            if (visual == null) { Debug.LogError("Inventory Tray needs its Base Visual."); return; }
            var glass = MakeMaterial("Inventory Black Glass", new Color(.035f, .041f, .043f, 1), .12f, .94f);
            var edge = MakeMaterial("Inventory Glass Rim", new Color(.22f, .225f, .135f, 1), .55f, .78f);
            var renderer = visual.GetComponent<MeshRenderer>();
            Undo.RecordObject(renderer, "Apply opaque black glass");
            renderer.sharedMaterial = glass;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            var rim = visual.Find("Glass Rim");
            if (rim == null)
            {
                var go = new GameObject("Glass Rim", typeof(MeshFilter), typeof(MeshRenderer));
                Undo.RegisterCreatedObjectUndo(go, "Add preplaced glass edge");
                rim = go.transform;
                rim.SetParent(visual, false);
                go.GetComponent<MeshFilter>().sharedMesh = MakeRing();
                var rimRenderer = go.GetComponent<MeshRenderer>();
                rimRenderer.sharedMaterial = edge;
                rimRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            EditorSceneManager.MarkSceneDirty(tray.gameObject.scene);
            Selection.activeGameObject = visual.gameObject;
            AssetDatabase.SaveAssets();
            Debug.Log("Tray appearance applied: opaque black gloss (smoothness 0.94), subtle olive rim. Materials and rim mesh are saved assets; no runtime spawning.");
        }
        private static Material MakeMaterial(string name, Color color, float metallic, float smoothness)
        {
            string path = "Assets/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = name;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 0);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_SpecularHighlights", 1);
            material.SetFloat("_EnvironmentReflections", 1);
            material.SetFloat("_ZWrite", 1);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.renderQueue = (int)RenderQueue.Geometry;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        private static Mesh MakeRing()
        {
            const string path = "Assets/Materials/Inventory Glass Rim Mesh.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            const int segments = 128;
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                vertices[2 * i] = direction * .497f + Vector3.up * 1.005f;
                vertices[2 * i + 1] = direction * .493f + Vector3.up * 1.005f;
                int next = (i + 1) % segments * 2;
                int t = i * 6;
                triangles[t] = 2 * i; triangles[t + 1] = 2 * i + 1; triangles[t + 2] = next;
                triangles[t + 3] = next; triangles[t + 4] = 2 * i + 1; triangles[t + 5] = next + 1;
            }
            var mesh = new Mesh { name = "Inventory Glass Rim", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
