using Oculus.Interaction;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class UIInputModeSwitcherMenu
    {
        [MenuItem("Tools/Game Flow/Configure Automatic UI Input")]
        public static void Configure()
        {
            var system = Object.FindFirstObjectByType<EventSystem>();
            if (system == null) { Debug.LogError("Add an EventSystem to the scene first."); return; }
            if (system.GetComponent<InputSystemUIInputModule>() == null) Undo.AddComponent<InputSystemUIInputModule>(system.gameObject);
            if (system.GetComponent<PointableCanvasModule>() == null) Undo.AddComponent<PointableCanvasModule>(system.gameObject);
            if (system.GetComponent<UIInputModeSwitcher>() == null) Undo.AddComponent<UIInputModeSwitcher>(system.gameObject);
            Selection.activeGameObject = system.gameObject;
            Debug.Log("Automatic UI input configured: Editor mouse; Android Meta. Save the scene to include it in builds.");
        }

        [MenuItem("Tools/Game Flow/Validate UI Input Switching")]
        public static void Validate()
        {
            if (Application.isPlaying) { Debug.LogWarning("Run validation outside Play Mode."); return; }
            var go = new GameObject("UI input validation");
            go.SetActive(false);
            try
            {
                go.AddComponent<EventSystem>();
                var mouse = go.AddComponent<InputSystemUIInputModule>();
                var meta = go.AddComponent<PointableCanvasModule>();
                var switcher = go.AddComponent<UIInputModeSwitcher>();
                meta.ExclusiveMode = true;
                mouse.enabled = false;
                switcher.ApplyMode();
                if (!mouse.enabled || meta.enabled || meta.ExclusiveMode) throw new System.Exception("Editor mouse selection failed.");
                var so = new SerializedObject(switcher);
                so.FindProperty("useMetaInputInEditor").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                switcher.ApplyMode();
                if (mouse.enabled || !meta.enabled || !meta.ExclusiveMode) throw new System.Exception("Meta selection failed.");
                Debug.Log("PASS: automatic Editor mouse input and explicit Meta override select only the intended module.");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
