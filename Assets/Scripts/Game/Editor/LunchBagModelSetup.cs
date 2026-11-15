using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class LunchBagModelSetup
    {
        private const string OldPath = "Assets/Models/Closed_Lunch_Bag.fbx";
        [MenuItem("Tools/Story/Replace Lunch Bag With Repaired Model")]
        public static void Configure()
        {
            if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Lunch_Bag_v3_Repaired.fbx");
            var meshes = source.GetComponentsInChildren<MeshFilter>(true);
            if (meshes.Length != 1) throw new System.InvalidOperationException("Expected one repaired bag mesh; found " + meshes.Length);
            var replacement = meshes[0].sharedMesh;
            int count = 0;
            foreach (var mesh in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (mesh.gameObject.scene.IsValid() && Replace(mesh, replacement)) count++;
            var path = "Assets/Prefabs/3D Models/LunchBag.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var mesh in root.GetComponentsInChildren<MeshFilter>(true))
                    if (Replace(mesh, replacement)) count++;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("LUNCH BAG REPLACED: " + count + " mesh instances; preserved interaction roots and material, fitted to previous height.");
        }
        private static bool Replace(MeshFilter filter, Mesh replacement)
        {
            if (AssetDatabase.GetAssetPath(filter.sharedMesh) != OldPath) return false;
            var original = filter.sharedMesh;
            var before = original.bounds;
            var after = replacement.bounds;
            float ratio = before.size.y / Mathf.Max(.0001f, after.size.y);
            var t = filter.transform;
            Undo.RecordObjects(new Object[] { filter, t, filter.gameObject }, "Replace lunch bag mesh");
            t.localPosition += t.localRotation * Vector3.Scale(t.localScale, before.center - after.center * ratio);
            t.localScale *= ratio;
            filter.sharedMesh = replacement;
            filter.gameObject.name = "Lunch_Bag_v3_Repaired";
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterials.Length != replacement.subMeshCount)
            {
                Undo.RecordObject(renderer, "Preserve bag material");
                renderer.sharedMaterials = Enumerable.Repeat(renderer.sharedMaterial, replacement.subMeshCount).ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            foreach (var collider in filter.GetComponents<MeshCollider>())
                if (collider.sharedMesh == original) { Undo.RecordObject(collider, "Update bag collider"); collider.sharedMesh = replacement; }
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter.gameObject);
            return true;
        }
    }
}
