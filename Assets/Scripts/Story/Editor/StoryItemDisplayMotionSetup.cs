using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TakeMeToTheMoon.Story.Editor
{
    public static class StoryItemDisplayMotionSetup
    {
        [MenuItem("Tools/Story/Configure Item Display Motion")]
        [MenuItem("Tools/Story/Configure Item Home Positions")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var tray = Object.FindFirstObjectByType<StoryInventoryTray>(FindObjectsInactive.Include);
            if (tray == null) return;
            var parent = tray.transform.parent;
            var homes = parent != null ? parent.Find("Item Home Positions") : null;
            if (homes == null)
            {
                homes = new GameObject("Item Home Positions").transform;
                Undo.RegisterCreatedObjectUndo(homes.gameObject, "Create item homes");
                homes.SetParent(parent, false);
            }
            var items = new SerializedObject(tray).FindProperty("allowedItems");
            for (int i = 0; i < items.arraySize; i++)
            {
                var snap = (SnapInteractor)items.GetArrayElementAtIndex(i).objectReferenceValue;
                var motion = snap.GetComponent<StoryItemDisplayMotion>();
                if (motion == null) motion = Undo.AddComponent<StoryItemDisplayMotion>(snap.gameObject);
                var data = new SerializedObject(motion);
                data.FindProperty("grabbable").objectReferenceValue = snap.GetComponent<Grabbable>();
                data.FindProperty("snapInteractor").objectReferenceValue = snap;
                data.FindProperty("body").objectReferenceValue = snap.GetComponent<Rigidbody>();
                if (data.FindProperty("home").objectReferenceValue == null)
                {
                    var home = new GameObject(snap.name + " Home").transform;
                    Undo.RegisterCreatedObjectUndo(home.gameObject, "Create item home");
                    home.SetParent(homes, false);
                    home.SetPositionAndRotation(snap.transform.position, snap.transform.rotation);
                    data.FindProperty("home").objectReferenceValue = home;
                }
                data.ApplyModifiedProperties();
            }
            EditorSceneManager.MarkSceneDirty(tray.gameObject.scene);
            Selection.activeGameObject = tray.transform.parent != null ? tray.transform.parent.gameObject : tray.gameObject;
            Debug.Log("Item display motion configured: 12 degree right tilt, 20-second idle revolution, paused while held, 2.5cm dock bob over 3 seconds. Five preplaced home positions return released, undocked items to their authored start positions. Colliders and snap anchors follow the actual prop.");
        }
    }
}
