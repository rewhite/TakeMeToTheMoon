using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using TakeMeToTheMoon.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TakeMeToTheMoon.Story.Editor
{
    public static class StoryInventoryTraySetup
    {
        [MenuItem("Tools/Story/Show Inventory Layout In Editor")]
        public static void ShowLayout()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var tray = Object.FindFirstObjectByType<StoryInventoryTray>(FindObjectsInactive.Include);
            if (tray == null) { Debug.LogWarning("Configure the inventory tray first."); return; }
            if (tray.transform.parent != null && tray.transform.parent.name == StoryInventoryGroupSetup.GroupName)
            {
                Undo.RecordObject(tray.transform.parent.gameObject, "Show selection group");
                tray.transform.parent.gameObject.SetActive(true);
            }
            var data = new SerializedObject(tray);
            var items = data.FindProperty("allowedItems");
            Undo.RecordObject(tray.gameObject, "Show inventory layout");
            tray.gameObject.SetActive(true);
            for (int i = 0; i < items.arraySize; i++)
            {
                var item = items.GetArrayElementAtIndex(i).objectReferenceValue as SnapInteractor;
                if (item == null) continue;
                Undo.RecordObject(item.gameObject, "Show inventory layout");
                item.gameObject.SetActive(true);
                PrefabUtility.RecordPrefabInstancePropertyModifications(item.gameObject);
            }
            Selection.activeGameObject = tray.gameObject;
            EditorSceneManager.MarkSceneDirty(tray.gameObject.scene);
            Debug.Log("Inventory layout visible in Edit Mode. Move the existing scene objects; Play Mode hides them until prologue reveal.");
        }

        [MenuItem("Tools/Story/Configure Inventory Tray")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (Object.FindFirstObjectByType<StoryInventoryTray>(FindObjectsInactive.Include) != null)
            { Debug.Log("Inventory Tray already configured; edit the existing tray in Inspector."); return; }
            var scene = EditorSceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var manager = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            string[] names = { "Alarm Clock", "Textbook", "LunchBag", "Redbull", "Flashlight" };
            string[] assets = { "Alarm clock", "CS textbook", "Mystery paper bag", "Red Bull can", "Flashlight" };
            var models = names.Select(name => roots.FirstOrDefault(go => go.name == name)).ToArray();
            if (manager == null || models.Any(go => go == null))
            { Debug.LogError("Inventory setup needs GameManager and the five item roots in 1114."); return; }
            if (roots.Any(go => go.name == "Inventory Tray"))
            { Debug.Log("Inventory Tray already configured; edit the existing tray in Inspector."); return; }
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Configure inventory tray");
            int group = Undo.GetCurrentGroup();
            var controller = Object.FindFirstObjectByType<StoryController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Story Controller.prefab");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(instance, "Create story controller");
                controller = instance.GetComponent<StoryController>();
            }
            var tray = new GameObject("Inventory Tray");
            Undo.RegisterCreatedObjectUndo(tray, "Create inventory tray");
            // Keep the user's prop transforms; place the tray in front of their existing display.
            Vector3 center = models.Aggregate(Vector3.zero, (sum, go) => sum + go.transform.position) / models.Length;
            tray.transform.position = new Vector3(center.x, center.y - 0.12f, center.z + 0.4f);
            var body = tray.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var zone = tray.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            zone.center = new Vector3(0, 0.22f, 0);
            zone.size = new Vector3(1.2f, 0.5f, 0.6f);
            var socket = tray.AddComponent<SnapInteractable>();
            Set(socket, "_rigidbody", body);
            var socketSettings = new SerializedObject(socket);
            socketSettings.FindProperty("_maxSelectingInteractors").intValue = 3;
            socketSettings.ApplyModifiedProperties();
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.name = "Base Visual";
            visual.transform.SetParent(tray.transform, false);
            visual.transform.localScale = new Vector3(1.2f, 0.025f, 0.6f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            var positions = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                positions[i] = new GameObject("Position " + (i + 1)).transform;
                positions[i].SetParent(tray.transform, false);
                positions[i].localPosition = new Vector3((i - 1) * 0.36f, 0.03f, 0);
            }
            var interactors = new SnapInteractor[5];
            for (int i = 0; i < models.Length; i++)
            {
                var go = models[i];
                var rb = GetOrAdd<Rigidbody>(go);
                Undo.RecordObject(rb, "Configure item body");
                rb.isKinematic = true;
                rb.useGravity = false;
                if (go.GetComponentsInChildren<Collider>(true).Length == 0)
                {
                    var bounds = LocalBounds(go);
                    var collider = Undo.AddComponent<BoxCollider>(go);
                    collider.center = bounds.center;
                    collider.size = bounds.size;
                }
                var grabbable = GetOrAdd<Grabbable>(go);
                Set(grabbable, "_rigidbody", rb);
                Set(grabbable, "_targetTransform", go.transform);
                var grab = GetOrAdd<GrabInteractable>(go);
                Set(grab, "_rigidbody", rb);
                Set(grab, "_pointableElement", grabbable);
                var handGrab = GetOrAdd<HandGrabInteractable>(go);
                Set(handGrab, "_rigidbody", rb);
                Set(handGrab, "_pointableElement", grabbable);
                var item = GetOrAdd<StoryItem>(go);
                Set(item, "definition", AssetDatabase.LoadAssetAtPath<StoryItemDefinition>("Assets/Story/Items/" + assets[i] + ".asset"));
                var snap = GetOrAdd<SnapInteractor>(go);
                Set(snap, "_rigidbody", rb);
                Set(snap, "_pointableElement", grabbable);
                var anchor = new GameObject("Tray Snap Anchor").transform;
                Undo.RegisterCreatedObjectUndo(anchor.gameObject, "Create bottom anchor");
                anchor.SetParent(go.transform, false);
                var itemBounds = LocalBounds(go);
                anchor.localPosition = new Vector3(itemBounds.center.x, itemBounds.min.y, itemBounds.center.z);
                Set(snap, "_snapPoseTransform", anchor);
                interactors[i] = snap;
                PrefabUtility.RecordPrefabInstancePropertyModifications(rb);
            }
            var layout = tray.AddComponent<StoryInventoryTray>();
            Set(layout, "controller", controller);
            Set(layout, "dropZone", zone);
            SetArray(layout, "positions", positions);
            SetArray(layout, "allowedItems", interactors);
            Set(socket, "_snapPoseDelegate", layout);
            var selection = GetOrAdd<StorySocketSelection>(controller.gameObject);
            Set(selection, "controller", controller);
            SetArray(selection, "sockets", new Object[] { socket });
            var settings = new SerializedObject(manager);
            var prologue = settings.FindProperty("prologue");
            prologue.FindPropertyRelative("revealWhen").enumValueIndex = (int)RevealCondition.VideoEnded;
            var reveal = prologue.FindPropertyRelative("revealObjects");
            var targets = models.Concat(new[] { tray }).ToArray();
            foreach (var target in targets)
            {
                bool exists = false;
                for (int i = 0; i < reveal.arraySize; i++)
                    if (reveal.GetArrayElementAtIndex(i).objectReferenceValue == target) exists = true;
                if (!exists) { int i = reveal.arraySize++; reveal.GetArrayElementAtIndex(i).objectReferenceValue = target; }
                Undo.RecordObject(target, "Show editable inventory layout");
                target.SetActive(true);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }
            settings.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = tray;
            Debug.Log("Inventory tray configured: one drop zone, three poses, five Meta grabbable items, prologue reveal.");
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(go);
        }
        private static void Set(Object target, string field, Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedProperties();
        }
        private static void SetArray(Object target, string field, Object[] values)
        {
            var data = new SerializedObject(target);
            var array = data.FindProperty(field);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            data.ApplyModifiedProperties();
        }
        private static Bounds LocalBounds(GameObject go)
        {
            var bounds = new Bounds();
            bool first = true;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var b = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    p = go.transform.InverseTransformPoint(renderer.transform.TransformPoint(p));
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
                }
            }
            return first ? new Bounds(Vector3.zero, Vector3.one * 0.1f) : bounds;
        }
    }
}
