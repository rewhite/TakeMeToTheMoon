using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class FuelTargetPrefabSetup
    {
        [MenuItem("Tools/Game Flow/Fix Fuel Panel Physical Collision")]
        public static void FixPanel()
        {
            if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first");
            var game = Object.FindAnyObjectByType<FuelThrowGame>(FindObjectsInactive.Include);
            game.PreparePanelForThrows();
            foreach (var collider in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (collider.gameObject.scene.IsValid() && collider.isTrigger)
                    PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
            EditorSceneManager.SaveScene(game.gameObject.scene);
            Debug.Log("FUEL PANEL FIXED: UI colliders are non-solid triggers, including linked external PointableCanvas surfaces.");
        }
        [MenuItem("Tools/Game Flow/Replace Fuel Cubes With Target Prefab")]
        public static void Configure()
        {
            if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first");
            var game = Object.FindAnyObjectByType<FuelThrowGame>(FindObjectsInactive.Include);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/3D Models/Target.prefab");
            if (game == null || prefab == null) throw new System.InvalidOperationException("Fuel game and Target prefab required");
            foreach (var target in game.targets)
            {
                if (target.transform.Find("Target Visual") != null) continue;
                Undo.RecordObject(target.transform, "Replace target visual");
                var renderer = target.GetComponent<MeshRenderer>();
                if (renderer != null) Undo.DestroyObjectImmediate(renderer);
                var filter = target.GetComponent<MeshFilter>();
                if (filter != null) Undo.DestroyObjectImmediate(filter);
                target.transform.localScale = Vector3.one;
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, target.transform);
                Undo.RegisterCreatedObjectUndo(visual, "Add target prefab");
                visual.name = "Target Visual";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;
                var bounds = LocalBounds(visual, target.transform);
                float scale = Mathf.Min(.52f / Mathf.Max(.001f, bounds.size.y), .55f / Mathf.Max(.001f, bounds.size.x));
                visual.transform.localScale = Vector3.one * scale;
                visual.transform.localPosition = -bounds.center * scale;
                PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
                var collider = target.GetComponent<BoxCollider>();
                Undo.RecordObject(collider, "Fit target collider");
                collider.center = Vector3.zero;
                collider.size = new Vector3(bounds.size.x * scale, bounds.size.y * scale, Mathf.Max(.08f, bounds.size.z * scale));
                // Keep the existing physical body, home, supports and scoring reference.
                target.body.ResetCenterOfMass(); target.body.ResetInertiaTensor();
                Debug.Log($"Fuel target {target.name}: visual size {bounds.size * scale}, prefab scale {scale}");
            }
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
            EditorSceneManager.SaveScene(game.gameObject.scene);
            Selection.activeGameObject = game.targets[1].gameObject;
        }
        private static Bounds LocalBounds(GameObject visual, Transform relativeTo)
        {
            var result = new Bounds(); bool first = true;
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var bounds = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = relativeTo.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                    else result.Encapsulate(point);
                }
            }
            if (first) throw new System.InvalidOperationException("Target prefab has no renderers");
            return result;
        }
    }
}
