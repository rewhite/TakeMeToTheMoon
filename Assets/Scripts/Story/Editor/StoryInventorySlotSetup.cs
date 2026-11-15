using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TakeMeToTheMoon.Story.Editor
{
    public static class StoryInventorySlotSetup
    {
        [MenuItem("Tools/Story/Configure Three Inventory Sockets")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var tray = Object.FindFirstObjectByType<StoryInventoryTray>(FindObjectsInactive.Include);
            if (tray == null) return;
            var data = new SerializedObject(tray);
            var positions = data.FindProperty("positions");
            var slots = data.FindProperty("slots");
            slots.arraySize = positions.arraySize;
            var controller = (StoryController)data.FindProperty("controller").objectReferenceValue;
            var adapter = new SerializedObject(controller.GetComponent<StorySocketSelection>());
            var sockets = adapter.FindProperty("sockets");
            sockets.arraySize = positions.arraySize;
            for (int i = 0; i < positions.arraySize; i++)
            {
                var position = (Transform)positions.GetArrayElementAtIndex(i).objectReferenceValue;
                var go = position.gameObject;
                var body = go.GetComponent<Rigidbody>();
                if (body == null) body = Undo.AddComponent<Rigidbody>(go);
                body.isKinematic = true; body.useGravity = false;
                var zone = go.GetComponent<BoxCollider>();
                if (zone == null) zone = Undo.AddComponent<BoxCollider>(go);
                Undo.RecordObject(zone, "Configure separate slot zone");
                zone.isTrigger = true; zone.center = new Vector3(0, .1f, 0);
                zone.size = new Vector3(.28f, .32f, .28f);
                var socket = go.GetComponent<SnapInteractable>();
                if (socket == null) socket = Undo.AddComponent<SnapInteractable>(go);
                var slot = go.GetComponent<StoryInventorySlot>();
                if (slot == null) slot = Undo.AddComponent<StoryInventorySlot>(go);
                var slotData = new SerializedObject(slot);
                slotData.FindProperty("tray").objectReferenceValue = tray;
                slotData.FindProperty("index").intValue = i;
                slotData.FindProperty("zone").objectReferenceValue = zone;
                slotData.ApplyModifiedProperties();
                var socketData = new SerializedObject(socket);
                socketData.FindProperty("_rigidbody").objectReferenceValue = body;
                socketData.FindProperty("_snapPoseDelegate").objectReferenceValue = slot;
                socketData.FindProperty("_maxSelectingInteractors").intValue = 1;
                socketData.ApplyModifiedProperties();
                slots.GetArrayElementAtIndex(i).objectReferenceValue = slot;
                sockets.GetArrayElementAtIndex(i).objectReferenceValue = socket;
            }
            data.FindProperty("dropZone").objectReferenceValue = null;
            data.ApplyModifiedProperties(); adapter.ApplyModifiedProperties();
            var shared = tray.GetComponent<SnapInteractable>();
            if (shared != null) Undo.DestroyObjectImmediate(shared);
            var oldZone = tray.GetComponent<BoxCollider>();
            if (oldZone != null) Undo.DestroyObjectImmediate(oldZone);
            EditorSceneManager.MarkSceneDirty(tray.gameObject.scene);
            Selection.activeGameObject = tray.gameObject;
            Debug.Log("Three independent inventory sockets configured on Position 1/2/3. Each has its own collider and capacity one; shared tray socket and collider removed.");
        }
    }
}
