using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Surfaces;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TakeMeToTheMoon.Story.Editor
{
    public static class StoryInventoryRaySetup
    {
        [MenuItem("Tools/Story/Configure Inventory Ray Grab")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var tray = Object.FindAnyObjectByType<StoryInventoryTray>(FindObjectsInactive.Include);
            if (tray == null) { Debug.LogError("Configure the inventory tray first."); return; }
            var rays = Object.FindObjectsByType<RayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(ray => ray.gameObject.scene == tray.gameObject.scene && ray.enabled).ToArray();
            if (rays.Length == 0) { Debug.LogError("Scene needs the Meta rig's hand/controller RayInteractors."); return; }
            var data = new SerializedObject(tray);
            var items = data.FindProperty("allowedItems");
            // Validate before changing any of the five objects.
            for (int i = 0; i < items.arraySize; i++)
            {
                var snap = items.GetArrayElementAtIndex(i).objectReferenceValue as SnapInteractor;
                if (snap == null || snap.GetComponent<Grabbable>() == null || snap.GetComponent<Collider>() == null)
                { Debug.LogError("Inventory ray grab requires a root collider and Grabbable on each item."); return; }
            }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure inventory ray grab");
            for (int i = 0; i < items.arraySize; i++)
            {
                var snap = (SnapInteractor)items.GetArrayElementAtIndex(i).objectReferenceValue;
                var root = snap.gameObject;
                var child = root.transform.Find("Inventory Ray Grab");
                if (child == null)
                {
                    var go = new GameObject("Inventory Ray Grab");
                    Undo.RegisterCreatedObjectUndo(go, "Create preplaced ray interaction");
                    go.transform.SetParent(root.transform, false);
                    var surface = go.AddComponent<ColliderSurface>();
                    Set(surface, "_collider", root.GetComponent<Collider>());
                    var movement = go.AddComponent<MoveFromTargetAtAnchorProvider>();
                    var ray = go.AddComponent<RayInteractable>();
                    Set(ray, "_surface", surface);
                    Set(ray, "_movementProvider", movement);
                    Set(ray, "_pointableElement", root.GetComponent<Grabbable>());
                }
                // Keep the shared Grabbable and SnapInteractor; only replace direct hand/controller inputs.
                foreach (var near in root.GetComponentsInChildren<GrabInteractable>(true)) Disable(near);
                foreach (var near in root.GetComponentsInChildren<HandGrabInteractable>(true)) Disable(near);
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(tray.gameObject.scene);
            Selection.activeGameObject = ((SnapInteractor)items.GetArrayElementAtIndex(0).objectReferenceValue).gameObject;
            Debug.Log("Inventory ray grab configured for five props. Existing rig rays: " + string.Join(", ", rays.Select(ray => ray.name)) + ". Point, hold select, move the ray, then release over the tray.");
        }
        private static void Disable(Behaviour component)
        {
            Undo.RecordObject(component, "Disable direct grab input");
            component.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
        private static void Set(Object target, string field, Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedProperties();
        }
    }
}
