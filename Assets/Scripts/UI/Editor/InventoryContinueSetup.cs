using System.Linq;
using Oculus.Interaction;
using TakeMeToTheMoon.Game;
using TakeMeToTheMoon.Story;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class InventoryContinueSetup
    {
        [MenuItem("Tools/Story/Configure Inventory Continue Button")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var tray = Object.FindFirstObjectByType<StoryInventoryTray>(FindObjectsInactive.Include);
            var controller = Object.FindFirstObjectByType<StoryController>(FindObjectsInactive.Include);
            var selection = Object.FindFirstObjectByType<StorySocketSelection>(FindObjectsInactive.Include);
            var manager = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            if (tray == null || controller == null || selection == null || manager == null)
                throw new System.InvalidOperationException("Inventory, StoryController, socket selection and GameManager are required.");
            var group = tray.transform.parent;
            var existing = group.GetComponentsInChildren<InventoryContinueButton>(true).FirstOrDefault();
            if (existing != null) { Selection.activeGameObject = existing.gameObject; return; }
            var root = new GameObject("Inventory Continue", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(InventoryContinueButton));
            Undo.RegisterCreatedObjectUndo(root, "Add inventory Continue");
            root.transform.SetParent(group, false);
            root.transform.SetPositionAndRotation(tray.transform.position + tray.transform.rotation * new Vector3(0, .28f, -.12f), tray.transform.rotation);
            root.transform.localScale = Vector3.one * .0015f;
            ((RectTransform)root.transform).sizeDelta = new Vector2(278, 72);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = controller.gameObject.scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c => c.name == "CenterEyeAnchor");
            var buttonObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Primary Button.prefab"), root.transform);
            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
            var label = buttonObject.GetComponentInChildren<TMP_Text>(true);
            label.text = "CONTINUE";
            PrefabUtility.RecordPrefabInstancePropertyModifications(label);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("8369d93f7b6b99742bbea0649a41b7b1"));
            var interaction = Object.Instantiate(source, root.transform, false);
            interaction.name = "ISDK_RayCanvasInteraction";
            var ir = (RectTransform)interaction.transform;
            ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
            ir.offsetMin = ir.offsetMax = Vector2.zero;
            interaction.GetComponent<PointableCanvas>().InjectCanvas(canvas);
            var settings = new SerializedObject(root.GetComponent<InventoryContinueButton>());
            settings.FindProperty("controller").objectReferenceValue = controller;
            settings.FindProperty("selection").objectReferenceValue = selection;
            settings.FindProperty("manager").objectReferenceValue = manager;
            settings.FindProperty("button").objectReferenceValue = buttonObject.GetComponent<Button>();
            settings.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log("Inventory Continue configured: preplaced Primary Button, Meta ray UI, visible only with three selected items; starts story then stage 1.");
        }
    }
}
