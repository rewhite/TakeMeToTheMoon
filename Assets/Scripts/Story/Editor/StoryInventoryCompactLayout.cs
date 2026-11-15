using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TakeMeToTheMoon.Story.Editor
{
    public static class StoryInventoryCompactLayout
    {
        [MenuItem("Tools/Story/Apply Compact Inventory Layout")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var tray = Object.FindFirstObjectByType<StoryInventoryTray>(FindObjectsInactive.Include);
            if (tray == null) return;
            var visual = tray.transform.Find("Base Visual");
            if (visual == null || tray.Slots == null || tray.Slots.Length != 3) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Compact inventory tray");
            Undo.RecordObject(visual, "Reduce tray dimensions");
            visual.localScale = new Vector3(.84f, .012f, .38f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
            for (int i = 0; i < tray.Slots.Length; i++)
            {
                var slot = tray.Slots[i];
                Undo.RecordObject(slot.transform, "Tighten inventory spacing");
                slot.transform.localPosition = new Vector3((i - 1) * .25f, .017f, 0);
                var zone = slot.GetComponent<BoxCollider>();
                Undo.RecordObject(zone, "Resize slot volume");
                zone.center = new Vector3(0, .08f, 0);
                zone.size = new Vector3(.22f, .28f, .24f);
                var placeholder = slot.transform.Find("Empty Slot Placeholder");
                if (placeholder != null)
                {
                    Undo.RecordObject(placeholder, "Resize empty marker");
                    placeholder.localScale = Vector3.one * .75f;
                }
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(tray.gameObject.scene);
            Selection.activeGameObject = tray.gameObject;
            Debug.Log("Compact inventory applied: 84x38cm tray, 2.4cm thickness, 25cm slot spacing, smaller markers and independent 22cm-wide colliders with 3cm gaps. Root and item scales preserved.");
        }
    }
}
