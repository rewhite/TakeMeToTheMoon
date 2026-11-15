using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TakeMeToTheMoon.Story.Editor
{
    public static class StoryInventoryPlaceholderSetup
    {
        [MenuItem("Tools/Story/Configure Empty Slot Placeholders")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var tray = Object.FindAnyObjectByType<StoryInventoryTray>(FindObjectsInactive.Include);
            if (tray == null) { Debug.LogError("Inventory Tray is required."); return; }
            if (tray.GetComponent<StoryInventoryPlaceholders>() != null)
            { Selection.activeGameObject = tray.gameObject; Debug.Log("Empty-slot placeholders already configured."); return; }
            var outline = Material("Inventory Placeholder Outline", new Color(.88f, .86f, .69f, .38f));
            var plusMaterial = Material("Inventory Placeholder Plus", new Color(.94f, .91f, .77f, .85f));
            var sphere = SphereMesh();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure empty inventory slots");
            var feedback = Undo.AddComponent<StoryInventoryPlaceholders>(tray.gameObject);
            var settings = new SerializedObject(feedback);
            settings.FindProperty("tray").objectReferenceValue = tray;
            settings.FindProperty("ghost").objectReferenceValue = tray.GetComponent<StoryInventoryGhost>();
            var positions = new SerializedObject(tray).FindProperty("positions");
            var slots = settings.FindProperty("slots");
            slots.arraySize = positions.arraySize;
            for (int i = 0; i < positions.arraySize; i++)
            {
                var position = (Transform)positions.GetArrayElementAtIndex(i).objectReferenceValue;
                var root = new GameObject("Empty Slot Placeholder");
                Undo.RegisterCreatedObjectUndo(root, "Create preplaced placeholder");
                root.transform.SetParent(position, false);
                var globe = new GameObject("Dashed Sphere", typeof(MeshFilter), typeof(MeshRenderer));
                globe.transform.SetParent(root.transform, false);
                globe.GetComponent<MeshFilter>().sharedMesh = sphere;
                ConfigureRenderer(globe.GetComponent<MeshRenderer>(), outline);
                var plus = new GameObject("Plus");
                plus.transform.SetParent(root.transform, false);
                plus.transform.localPosition = new Vector3(0, .105f, 0);
                Bar(plus.transform, "Horizontal", new Vector3(.045f, .006f, .003f), plusMaterial);
                Bar(plus.transform, "Vertical", new Vector3(.006f, .045f, .003f), plusMaterial);
                slots.GetArrayElementAtIndex(i).FindPropertyRelative("visual").objectReferenceValue = root;
                slots.GetArrayElementAtIndex(i).FindPropertyRelative("plus").objectReferenceValue = plus.transform;
            }
            settings.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(tray.gameObject.scene);
            Selection.activeGameObject = tray.gameObject;
            AssetDatabase.SaveAssets();
            Debug.Log("Three empty-slot markers configured: ivory dashed spheres and viewer-facing plus signs; hidden for occupied/ghost slots. All visuals are preplaced, without colliders.");
        }
        private static void Bar(Transform parent, string name, Vector3 scale, Material material)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = name;
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.transform.SetParent(parent, false);
            bar.transform.localScale = scale;
            ConfigureRenderer(bar.GetComponent<MeshRenderer>(), material);
        }
        private static void ConfigureRenderer(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        private static Material Material(string name, Color color)
        {
            string path = "Assets/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        private static Mesh SphereMesh()
        {
            const string path = "Assets/Materials/Inventory Placeholder Sphere.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            // Three great circles suggest volume. A fourth ring marks the base on the plate.
            for (int plane = 0; plane < 4; plane++)
                for (int dash = 0; dash < 24; dash++)
                    for (int step = 0; step < 3; step++)
                    {
                        float a = (dash + step * .62f / 3) * Mathf.PI * 2 / 24;
                        float b = (dash + (step + 1) * .62f / 3) * Mathf.PI * 2 / 24;
                        Tube(Point(plane, a), Point(plane, b), plane == 3 ? .0013f : .0011f, vertices, triangles);
                    }
            mesh = new Mesh { name = "Dashed inventory sphere" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        private static Vector3 Point(int plane, float angle)
        {
            float r = plane == 3 ? .108f : .095f;
            float x = Mathf.Cos(angle) * r, y = Mathf.Sin(angle) * r;
            if (plane == 3) return new Vector3(x, .002f, y);
            return new Vector3(0, .105f, 0) + (plane == 0 ? new Vector3(x, y, 0)
                : plane == 1 ? new Vector3(0, x, y) : new Vector3(x, 0, y));
        }
        private static void Tube(Vector3 a, Vector3 b, float radius, List<Vector3> vertices, List<int> triangles)
        {
            var axis = (b - a).normalized;
            var side = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right).normalized;
            var up = Vector3.Cross(axis, side);
            int start = vertices.Count;
            for (int end = 0; end < 2; end++)
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * Mathf.PI / 3;
                    vertices.Add((end == 0 ? a : b) + radius * (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)));
                }
            for (int i = 0; i < 6; i++)
            {
                int j = (i + 1) % 6;
                triangles.Add(start + i); triangles.Add(start + j); triangles.Add(start + 6 + i);
                triangles.Add(start + j); triangles.Add(start + 6 + j); triangles.Add(start + 6 + i);
            }
        }
    }
}
