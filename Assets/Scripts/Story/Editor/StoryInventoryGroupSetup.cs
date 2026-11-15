using System;
using System.Linq;
using Oculus.Interaction;
using TakeMeToTheMoon.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TakeMeToTheMoon.Story.Editor
{
    public static class StoryInventoryGroupSetup
    {
        public const string GroupName = "Item Selection";

        [MenuItem("Tools/Story/Group Item Selection Objects")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var tray = Object.FindFirstObjectByType<StoryInventoryTray>(FindObjectsInactive.Include);
            var manager = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            if (tray == null || manager == null) throw new InvalidOperationException("Inventory tray and GameManager are required.");
            var scene = tray.gameObject.scene;
            var all = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Transform>(true)).ToArray();
            var panel = all.FirstOrDefault(t => t.name == "Item Selection Panel");
            if (panel == null) throw new InvalidOperationException("Item Selection Panel is required.");
            var items = new SerializedObject(tray).FindProperty("allowedItems");
            var targets = Enumerable.Range(0, items.arraySize)
                .Select(i => ((SnapInteractor)items.GetArrayElementAtIndex(i).objectReferenceValue).gameObject)
                .Concat(new[] { tray.gameObject, panel.gameObject }).Distinct().ToArray();
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Group item selection objects");
            var root = scene.GetRootGameObjects().FirstOrDefault(go => go.name == GroupName);
            if (root == null)
            {
                root = new GameObject(GroupName);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = tray.transform.position;
                Undo.RegisterCreatedObjectUndo(root, "Create item selection group");
            }
            foreach (var target in targets)
            {
                Vector3 position = target.transform.position;
                Quaternion rotation = target.transform.rotation;
                Vector3 scale = target.transform.lossyScale;
                Undo.SetTransformParent(target.transform, root.transform, "Group item selection");
                Undo.RecordObject(target, "Keep authored selection visible");
                target.SetActive(true);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target.transform);
                if (Vector3.Distance(position, target.transform.position) > .0001f ||
                    Quaternion.Angle(rotation, target.transform.rotation) > .001f ||
                    Vector3.Distance(scale, target.transform.lossyScale) > .0001f)
                    throw new InvalidOperationException("Grouping changed world transform: " + target.name);
            }
            Undo.RecordObject(root, "Show selection layout in Editor");
            root.SetActive(true);
            var settings = new SerializedObject(manager);
            var prologue = settings.FindProperty("prologue");
            var reveal = prologue.FindPropertyRelative("revealObjects");
            var kept = Enumerable.Range(0, reveal.arraySize)
                .Select(i => reveal.GetArrayElementAtIndex(i).objectReferenceValue as GameObject)
                .Where(go => go != null && go != root && !go.transform.IsChildOf(root.transform)).ToList();
            kept.Add(root);
            reveal.arraySize = kept.Count;
            for (int i = 0; i < kept.Count; i++) reveal.GetArrayElementAtIndex(i).objectReferenceValue = kept[i];
            prologue.FindPropertyRelative("revealWhen").enumValueIndex = (int)RevealCondition.VideoEnded;
            settings.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log("Item Selection grouped: five items, tray and panel; world transforms preserved. Prologue VideoEnded reveals the preplaced parent using SetActive, with no runtime spawning.");
        }
    }
}
