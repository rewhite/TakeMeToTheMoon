using TMPro;
using TakeMeToTheMoon.Story;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.Game.Editor
{
    [CustomEditor(typeof(GameManager))]
    public sealed class GameManagerEditor : UnityEditor.Editor
    {
        private bool previewSkybox;
        private int previewStage;
        private Material previewMaterial, previousSkybox;
        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += OnSceneSaving;
        }
        private void OnDisable()
        {
            StopSkyboxPreview();
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSceneSaving;
        }
        private void OnPlayModeChanged(PlayModeStateChange state) => StopSkyboxPreview();
        private void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) => StopSkyboxPreview();
        private void StopSkyboxPreview()
        {
            if (previewMaterial != null)
            {
                if (RenderSettings.skybox == previewMaterial) RenderSettings.skybox = previousSkybox;
                DestroyImmediate(previewMaterial);
            }
            previewMaterial = null;
            previousSkybox = null;
            previewSkybox = false;
            SceneView.RepaintAll();
        }
        private void DrawSkyboxPreview()
        {
            if (Application.isPlaying) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Skybox Image Preview (Edit Mode)", EditorStyles.boldLabel);
            bool enabled = EditorGUILayout.Toggle("Live Preview", previewSkybox);
            if (!enabled) { if (previewSkybox) StopSkyboxPreview(); return; }
            previewSkybox = true;
            serializedObject.Update();
            var stages = serializedObject.FindProperty("stages");
            var options = new string[stages.arraySize + 1];
            options[0] = "Prologue";
            for (int i = 0; i < stages.arraySize; i++) options[i + 1] = "Stage " + (i + 1);
            previewStage = EditorGUILayout.Popup("Preview Stage", Mathf.Clamp(previewStage, 0, options.Length - 1), options);
            var stage = previewStage == 0 ? serializedObject.FindProperty("prologue") : stages.GetArrayElementAtIndex(previewStage - 1);
            EditorGUILayout.PropertyField(stage.FindPropertyRelative("skyboxImage"));
            EditorGUILayout.PropertyField(stage.FindPropertyRelative("skyboxRotation"));
            EditorGUILayout.PropertyField(stage.FindPropertyRelative("skyboxExposure"));
            serializedObject.ApplyModifiedProperties();
            var texture = stage.FindPropertyRelative("skyboxImage").objectReferenceValue as Texture2D;
            if (texture == null)
            {
                if (previewMaterial != null && RenderSettings.skybox == previewMaterial) RenderSettings.skybox = previousSkybox;
                EditorGUILayout.HelpBox("Assign Skybox Image to preview its rotation. Saving or leaving this Inspector ends the preview.", MessageType.Info);
                return;
            }
            if (previewMaterial == null)
            {
                var template = Resources.Load<Material>("StagePanoramaTemplate");
                if (template == null) return;
                previousSkybox = RenderSettings.skybox;
                previewMaterial = new Material(template) { hideFlags = HideFlags.HideAndDontSave };
            }
            previewMaterial.SetTexture("_MainTex", texture);
            previewMaterial.SetFloat("_Rotation", stage.FindPropertyRelative("skyboxRotation").floatValue);
            previewMaterial.SetFloat("_Exposure", stage.FindPropertyRelative("skyboxExposure").floatValue);
            RenderSettings.skybox = previewMaterial;
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            DrawSkyboxPreview();
            var manager = (GameManager)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Flow Preview (Play Mode)", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || !manager.isActiveAndEnabled))
            {
                EditorGUILayout.LabelField("Current", manager.Phase + (manager.Phase == GamePhase.Main ? " / Stage " + manager.CurrentStageNumber : ""));
                if (GUILayout.Button("Start Game")) manager.StartGame();
                if (GUILayout.Button("Show Prologue")) manager.ShowPrologue();
                if (GUILayout.Button("Reveal Current Stage")) manager.RevealCurrentStage();
                if (GUILayout.Button("Next Stage / Finish")) PreviewNextStage(manager);
                if (GUILayout.Button("Return To Start")) manager.ReturnToStart();
            }
            if (Application.isPlaying) Repaint();
        }

        // Live stages are driven by StoryController.ScenePresented, not ShowStage alone.
        // Match the Quest debug preview: seed temporary inventory and start the real story.
        // Keep this in the Editor; normal gameplay still uses InventoryContinueButton/sockets.
        public static void PreviewNextStage(GameManager manager)
        {
            if (!Application.isPlaying || manager.IsTransitioning) return;
            var presenter = Object.FindAnyObjectByType<StageViskoPresentation>();
            if (manager.Phase == GamePhase.Prologue && presenter != null && presenter.isActiveAndEnabled)
                RemoteDebugCommands.Execute(manager, "stage1");
            else manager.NextStage();
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
            var prologue = MakeContentRoot("Prologue");
            var main = MakeContentRoot("Main");
            var end = MakeScreen("End Screen", "THE END", "BACK TO START", false);
            string path = Folder + "/Game Manager.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                var go = new GameObject("Game Manager");
                try
                {
                    var manager = go.AddComponent<GameManager>();
                    var so = new SerializedObject(manager);
                    so.FindProperty("transitionShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Video/ScreenTransition.shader");
                    so.FindProperty("startScreenPrefab").objectReferenceValue = start;
                    so.FindProperty("prologuePrefab").objectReferenceValue = prologue;
                    so.FindProperty("prologue").FindPropertyRelative("screenPrefab").objectReferenceValue = prologue;
                    so.FindProperty("mainPrefab").objectReferenceValue = main;
                    so.FindProperty("stages").GetArrayElementAtIndex(0).FindPropertyRelative("screenPrefab").objectReferenceValue = main;
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

        private static GameObject MakeContentRoot(string name)
        {
            string path = Folder + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var root = new GameObject(name);
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
