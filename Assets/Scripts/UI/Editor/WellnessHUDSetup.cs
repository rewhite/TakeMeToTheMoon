using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class WellnessHUDSetup
    {
        [MenuItem("Tools/Game Flow/Configure Right Lower Wellness HUD")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = EditorSceneManager.GetActiveScene();
            var transforms = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Transform>(true)).ToArray();
            var eye = transforms.FirstOrDefault(t => t.name == "CenterEyeAnchor" && t.GetComponent<Camera>() != null);
            if (eye == null) { Debug.LogError("Scene needs XR CenterEyeAnchor."); return; }
            var bar = Object.FindFirstObjectByType<WellnessBar>(FindObjectsInactive.Include);
            if (bar == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Wellness Bar.prefab");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(instance, "Add persistent wellness HUD");
                bar = instance.GetComponent<WellnessBar>();
            }
            // Keep this scene-authored HUD outside disposable stage / reveal roots.
            Undo.SetTransformParent(bar.transform, null, "Keep wellness HUD persistent");
            Undo.RecordObject(bar.gameObject, "Enable wellness HUD");
            bar.gameObject.SetActive(true);
            var follower = bar.GetComponent<HeadFollowingHUD>();
            if (follower == null) follower = Undo.AddComponent<HeadFollowingHUD>(bar.gameObject);
            var data = new SerializedObject(follower);
            data.FindProperty("head").objectReferenceValue = eye;
            data.ApplyModifiedProperties();
            Undo.RecordObject(bar.transform, "Position compact wellness HUD");
            bar.transform.localScale = Vector3.one * .00055f;
            follower.PreviewPosition();
            var canvas = bar.GetComponent<Canvas>();
            Undo.RecordObject(canvas, "Assign XR HUD camera");
            canvas.worldCamera = eye.GetComponent<Camera>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            foreach (var graphic in bar.GetComponentsInChildren<Graphic>(true))
            {
                Undo.RecordObject(graphic, "Keep HUD noninteractive");
                graphic.raycastTarget = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(graphic);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(bar.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(bar.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(canvas);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = bar.gameObject;
            Debug.Log("Persistent wellness HUD configured on existing scene instance: simulated torso-relative right 38cm, down 22cm, forward 90cm; 26.4cm-wide UI with smooth torso following. No runtime spawn or ray interception.");
        }
    }
}
