using System.Linq;
using TMPro;
using TakeMeToTheMoon.Game;
using TakeMeToTheMoon.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class ItemSelectionPanelMenu
    {
        private const string Path = "Assets/Prefabs/Item Selection Panel.prefab";

        [MenuItem("Tools/Story/Create Item Selection Panel")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var manager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            var tray = Object.FindAnyObjectByType<StoryInventoryTray>(FindObjectsInactive.Include);
            if (manager == null || tray == null) { Debug.LogError("Scene needs GameManager and Inventory Tray."); return; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (prefab == null)
            {
                var root = new GameObject("Item Selection Panel", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var rect = (RectTransform)root.transform;
                    rect.sizeDelta = new Vector2(780, 222);
                    rect.localScale = Vector3.one * .0015f;
                    root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                    var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
                    var bg = (RectTransform)background.transform;
                    bg.SetParent(root.transform, false);
                    bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
                    bg.offsetMin = bg.offsetMax = Vector2.zero;
                    var image = background.GetComponent<Image>();
                    image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Images/Canvas Panel.png");
                    image.type = Image.Type.Tiled;
                    image.pixelsPerUnitMultiplier = 3;
                    image.raycastTarget = false;
                    AddText(root.transform, "Title", "Choose 3 Items.", "RubikDirt-Regular", 36,
                        new Color(.91f, .9f, .80f), new Vector2(44, -40), new Vector2(690, 48));
                    AddText(root.transform, "Body", "The item you select now may influence your later\nchoices.", "EastSeaDokdo-Regular", 42,
                        new Color(.65f, .64f, .55f), new Vector2(44, -104), new Vector2(690, 94));
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, Path);
                }
                finally { Object.DestroyImmediate(root); }
            }
            var scene = manager.gameObject.scene;
            var instance = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).FirstOrDefault(go => go.name == "Item Selection Panel");
            if (instance == null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(instance, "Add item selection panel");
                instance.transform.position = tray.transform.position + new Vector3(0, .6f, .25f);
                instance.transform.rotation = tray.transform.rotation;
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            }
            var revealTarget = instance.transform.parent != null && instance.transform.parent.name == "Item Selection"
                ? instance.transform.parent.gameObject : instance;
            var settings = new SerializedObject(manager);
            var reveal = settings.FindProperty("prologue").FindPropertyRelative("revealObjects");
            bool exists = false;
            for (int i = 0; i < reveal.arraySize; i++)
                if (reveal.GetArrayElementAtIndex(i).objectReferenceValue == revealTarget) exists = true;
            if (!exists) { int i = reveal.arraySize++; reveal.GetArrayElementAtIndex(i).objectReferenceValue = revealTarget; }
            settings.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = instance;
            Debug.Log("Item Selection Panel placed in scene and linked to prologue reveal. Title and Body remain editable TMP text; no input raycaster blocks item rays.");
            Preview(prefab);
        }

        private static void AddText(Transform parent, string name, string copy, string font, float size, Color color, Vector2 position, Vector2 dimensions)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/" + font + " SDF.asset");
            text.text = copy;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.raycastTarget = false;
            text.enableAutoSizing = false;
            text.overflowMode = TextOverflowModes.Overflow;
        }

        private static void Preview(GameObject prefab)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            Texture2D image = null;
            var old = RenderTexture.active;
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                root.transform.position = new Vector3(0, -.005f, 2);
                root.transform.localScale = Vector3.one * .001f;
                var cameraGo = new GameObject("Preview Camera", typeof(Camera));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo, scene);
                var camera = cameraGo.GetComponent<Camera>();
                camera.scene = scene;
                camera.orthographic = true;
                camera.orthographicSize = .146f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.12f, .12f, .12f);
                camera.nearClipPlane = .01f;
                camera.farClipPlane = 10;
                foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();
                foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>())
                    if (text.preferredHeight > text.rectTransform.rect.height + 1 || text.preferredWidth > text.rectTransform.rect.width + 1)
                        Debug.LogWarning("Selection panel text exceeds allocated area: " + text.name);
                target = new RenderTexture(850, 292, 24);
                camera.targetTexture = target;
                camera.Render();
                camera.targetTexture = null;
                RenderTexture.active = target;
                image = new Texture2D(850, 292, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 850, 292), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes("/tmp/item-selection-panel-preview.png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = old;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (image != null) Object.DestroyImmediate(image);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
