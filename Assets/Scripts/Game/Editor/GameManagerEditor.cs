using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.Game.Editor
{
    [CustomEditor(typeof(GameManager))]
    public sealed class GameManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var manager = (GameManager)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Flow Preview (Play Mode)", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || !manager.isActiveAndEnabled))
            {
                EditorGUILayout.LabelField("Current", manager.Phase.ToString());
                if (GUILayout.Button("Start Game")) manager.StartGame();
                if (GUILayout.Button("Next Stage / Finish")) manager.NextStage();
                if (GUILayout.Button("Return To Start")) manager.ReturnToStart();
            }
            if (Application.isPlaying) Repaint();
        }
    }

    public static class GameFlowSetup
    {
        private const string Folder = "Assets/Prefabs/Game Flow";

        [MenuItem("GameObject/Story/Game Manager", false, 10)]
        private static void Create(MenuCommand command)
        {
            CreatePrefabs();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Game Manager.prefab"));
            Undo.RegisterCreatedObjectUndo(go, "Create Game Manager");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Selection.activeGameObject = go;
        }

        // Also callable in batch mode. Existing customized prefabs are never overwritten.
        [MenuItem("Tools/Game Flow/Create Starter Prefabs")]
        public static void CreatePrefabs()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Game Flow");
            var start = MakeScreen("Start Screen", "TAKE ME TO THE MOON", "START", true);
            var main = MakeMain();
            var end = MakeScreen("End Screen", "THE END", "BACK TO START", false);
            string path = Folder + "/Game Manager.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                var go = new GameObject("Game Manager");
                try
                {
                    var manager = go.AddComponent<GameManager>();
                    var so = new SerializedObject(manager);
                    so.FindProperty("startScreenPrefab").objectReferenceValue = start;
                    so.FindProperty("mainPrefab").objectReferenceValue = main;
                    so.FindProperty("endScreenPrefab").objectReferenceValue = end;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(go, path);
                }
                finally { Object.DestroyImmediate(go); }
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Game Flow/Apply Start Screen Artwork")]
        public static void ApplyStartScreenArtwork()
        {
            CreatePrefabs();
            string path = Folder + "/Start Screen.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var canvas = root.GetComponentInChildren<Canvas>(true);
                if (canvas == null) throw new System.InvalidOperationException("Start Screen needs a Canvas.");
                for (int i = canvas.transform.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(canvas.transform.GetChild(i).gameObject);
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.transform.localPosition = new Vector3(0, 1.6f, 3);
                canvas.transform.localRotation = Quaternion.identity;
                canvas.transform.localScale = Vector3.one * .002f;
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(1106, 628);
                if (canvas.GetComponent<GraphicRaycaster>() == null) canvas.gameObject.AddComponent<GraphicRaycaster>();
                Artwork(canvas.transform, "Logo", "Logo.png", 610, 135, false);
                var image = Artwork(canvas.transform, "Start Button", "Start Button.png", 300, -127, true);
                var button = image.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(.9f, .85f, 1f);
                colors.pressedColor = new Color(.7f, .55f, .85f);
                colors.selectedColor = Color.white;
                button.colors = colors;
                Artwork(canvas.transform, "Hint", "Hint.png", 282, -194, false);
                var screen = root.GetComponent<GameFlowScreen>();
                if (screen == null) screen = root.AddComponent<GameFlowScreen>();
                var so = new SerializedObject(screen);
                so.FindProperty("startButton").objectReferenceValue = button;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("Start Screen artwork applied: Logo, clickable START, Hint. No background.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Image Artwork(Transform parent, string name, string file, float width, float y, bool clickable)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Images/Main/" + file);
            if (sprite == null) throw new System.InvalidOperationException("Missing UI sprite: " + file);
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = clickable;
            image.rectTransform.sizeDelta = new Vector2(width, width * sprite.rect.height / sprite.rect.width);
            image.rectTransform.anchoredPosition = new Vector2(0, y);
            return image;
        }

        private static GameObject MakeMain()
        {
            string path = Folder + "/Main.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var root = new GameObject("Main");
            try { return PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject MakeScreen(string name, string title, string buttonLabel, bool start)
        {
            string path = Folder + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var root = new GameObject(name);
            try
            {
                var canvasGo = new GameObject("Screen Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                canvasGo.transform.SetParent(root.transform, false);
                canvasGo.transform.localPosition = new Vector3(0, 1.5f, 2);
                canvasGo.transform.localScale = Vector3.one * .001f;
                ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(1400, 700);
                canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                MakeText(canvasGo.transform, "Logo Placeholder", title, new Vector2(0, 130), new Vector2(1300, 250), 90);
                var buttonGo = new GameObject("Action Button", typeof(RectTransform), typeof(Image), typeof(Button));
                buttonGo.transform.SetParent(canvasGo.transform, false);
                var rect = (RectTransform)buttonGo.transform;
                rect.anchoredPosition = new Vector2(0, -130);
                rect.sizeDelta = new Vector2(600, 150);
                buttonGo.GetComponent<Image>().color = new Color(.12f, .19f, .3f);
                var button = buttonGo.GetComponent<Button>();
                button.targetGraphic = buttonGo.GetComponent<Image>();
                MakeText(buttonGo.transform, "Label", buttonLabel, Vector2.zero, rect.sizeDelta, 54);
                var so = new SerializedObject(root.AddComponent<GameFlowScreen>());
                so.FindProperty(start ? "startButton" : "returnToStartButton").objectReferenceValue = button;
                so.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void MakeText(Transform parent, string name, string text, Vector2 position, Vector2 size, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.rectTransform.anchoredPosition = position;
            label.rectTransform.sizeDelta = size;
        }

    }
}
