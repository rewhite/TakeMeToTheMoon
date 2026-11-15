using System.Linq;
using TakeMeToTheMoon.Game;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class PalmWellnessSetup
    {
        [MenuItem("Tools/Game Flow/Configure Palm Wellness")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var bar = Object.FindFirstObjectByType<WellnessBar>(FindObjectsInactive.Include);
            var scene = EditorSceneManager.GetActiveScene();
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var eye = all.First(t => t.name == "CenterEyeAnchor" && t.GetComponent<Camera>() != null);
            var hand = all.Select(t => t.GetComponent<Hand>()).FirstOrDefault(h => h != null && AnimationUtility.CalculateTransformPath(h.transform, null).ToLowerInvariant().Contains("left"));
            var rightHand = all.Select(t => t.GetComponent<Hand>()).FirstOrDefault(h => h != null && AnimationUtility.CalculateTransformPath(h.transform, null).ToLowerInvariant().Contains("right"));
            if (bar == null || hand == null || rightHand == null) throw new System.InvalidOperationException("Preplaced wellness bar and Meta left/right Hands required.");
            var old = bar.GetComponent<HeadFollowingHUD>();
            if (old != null) { Undo.RecordObject(old, "Disable torso HUD"); old.enabled = false; }
            var group = bar.GetComponent<CanvasGroup>();
            if (group == null) group = Undo.AddComponent<CanvasGroup>(bar.gameObject);
            var display = bar.GetComponent<PalmWellnessDisplay>();
            if (display == null) display = Undo.AddComponent<PalmWellnessDisplay>(bar.gameObject);
            var data = new SerializedObject(display);
            data.FindProperty("manager").objectReferenceValue = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            data.FindProperty("handSource").objectReferenceValue = hand;
            data.FindProperty("rightHandSource").objectReferenceValue = rightHand;
            data.FindProperty("head").objectReferenceValue = eye;
            data.FindProperty("visibility").objectReferenceValue = group;
            data.FindProperty("heightAbovePalm").floatValue = .22f;
            data.ApplyModifiedProperties();
            Undo.RecordObject(bar.transform, "Size palm wellness");
            bar.transform.localScale = Vector3.one * .00024f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(bar.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = bar.gameObject;
            Debug.Log("Palm wellness configured with both Meta hands: palm up reveals, palm down or tracking loss hides. Existing story value and heartbeat retained.");
        }
    }
}
