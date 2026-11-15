using System.IO;
using System.Linq;
using TakeMeToTheMoon.Game;
using TakeMeToTheMoon.UI;
using TakeMeToTheMoon.Video;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Visko;

namespace TakeMeToTheMoon.Story.Editor
{
    /// <summary>
    /// Adds and wires the AI story flow in the open scene (normally 1114): DirectorClient,
    /// ViskoStoryBridge, a Visko Stream, choice cards, narration and StoryFlowConnector.
    /// Re-running keeps existing objects and their transforms; it only fills missing pieces.
    /// </summary>
    public static class StoryFlowSetup
    {
        const string ScenePath = "Assets/Scenes/1114.unity";
        const string ViskoModel = "reactor/visko-orbis-stable";

        [MenuItem("Tools/Story/Configure AI Story Flow")]
        public static void Configure()
        {
            var story = Find<StoryController>();
            var game = Find<GameManager>();
            var shared = Find<SharedVideoPlayer>();
            if (story == null || game == null || shared == null)
            {
                Debug.LogError("Open the 1114 scene: StoryController, GameManager and SharedVideoPlayer are required.");
                return;
            }
            GameObject host = story.gameObject;

            // Director.
            var director = GetOrAdd<DirectorClient>(host);
            Set(director, "controller", story);
            Set(director, "systemPrompt", AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Story/DirectorSystemPrompt.txt"));

            // Visko stream and bridge.
            var visko = Find<ViskoStreamClient>();
            if (visko == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Visko Stream.prefab");
                visko = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, story.gameObject.scene)).GetComponent<ViskoStreamClient>();
                Undo.RegisterCreatedObjectUndo(visko.gameObject, "Add Visko Stream");
            }
            Undo.RecordObject(visko, "Configure Visko");
            visko.model = ViskoModel;
            visko.connectOnStart = false;
            visko.autoStart = false;
            visko.externalScreen = true;
            visko.showWorldStatus = false;
            visko.showDebugGui = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(visko);

            var bridge = GetOrAdd<ViskoStoryBridge>(host);
            Set(bridge, "controller", story);
            Set(bridge, "visko", visko);
            Set(bridge, "sharedVideo", shared);
            SetBool(bridge, "connectOnStart", false);
            var bridgeSo = new SerializedObject(bridge);
            bridgeSo.FindProperty("styleLine").stringValue =
                "First-person view, handheld survival footage, photorealistic, dim smoky blood-orange light, ash in the air, harsh shadows, one continuous take.";
            bridgeSo.ApplyModifiedProperties();
            Set(director, "systemPrompt", AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Story/DirectorSystemPrompt.txt"));

            // UI placed where the item-selection panel was: it is hidden once the story starts.
            Transform anchor = FindByName("Item Selection Panel")?.transform;
            var choices = FindByName("Choice Cards") ?? Spawn("Assets/Prefabs/Choice Cards.prefab", story, anchor, 0f);
            NarrationPanel existingNarration = Find<NarrationPanel>();
            var narrationObject = existingNarration != null ? existingNarration.gameObject
                : Spawn("Assets/Prefabs/Narration Canvas.prefab", story, anchor, .45f);
            Button[] buttons = choices.GetComponentsInChildren<Button>(true)
                .OrderBy(b => b.transform.position.x).ToArray();
            TMP_Text[] labels = buttons.Select(b => b.GetComponentInChildren<TMP_Text>(true)).ToArray();

            // The item grab and the dorm choice use the scene's own UI (InventoryContinueButton,
            // RoommateChoicePanel); the connector handles the AI checkpoints only.
            CountdownTimer timer = null;

            var connector = GetOrAdd<StoryFlowConnector>(host);
            Set(connector, "gameManager", game);
            Set(connector, "story", story);
            Set(connector, "selection", Find<StorySocketSelection>());
            Set(connector, "visko", bridge);
            Set(connector, "itemSelectionRoot", FindByName("Item Selection"));
            Set(connector, "grabTimer", timer);
            Set(connector, "choiceRoot", choices);
            SetArray(connector, "choiceButtons", buttons);
            SetArray(connector, "choiceLabels", labels);
            Set(connector, "narration", narrationObject.GetComponent<NarrationPanel>());
            Set(connector, "director", director);
            Set(connector, "viskoClient", visko);
            // The dorm uses the same story-driven cards as every checkpoint (OpeningScenes.json),
            // so Potato's static Stage 1 roommate panel is switched off.
            var so = new SerializedObject(connector);
            so.FindProperty("firstAiCheckpoint").intValue = 1;
            so.FindProperty("showStatusInHeadset").boolValue = false;
            so.ApplyModifiedProperties();
            foreach (var panel in Object.FindObjectsByType<RoommateChoicePanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.RecordObject(panel.gameObject, "Retire roommate panel");
                panel.gameObject.SetActive(false);
                Debug.Log("Disabled RoommateChoicePanel on " + panel.name + " (dorm choice now uses the story cards).");
            }

            EditorSceneManager.MarkSceneDirty(story.gameObject.scene);
            Debug.Log($"AI story flow configured: {buttons.Length} choice buttons, timer {(timer != null ? "found" : "missing")}, " +
                      $"item selection {(FindByName("Item Selection") != null ? "found" : "missing")}. Save the scene.");
        }

        /// <summary>Batch mode: -executeMethod TakeMeToTheMoon.Story.Editor.StoryFlowSetup.ConfigureAndSave</summary>
        public static void ConfigureAndSave()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Configure();
            LayoutAroundScreen();
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// Keeps the video clean: narration becomes a subtitle strip on the screen's bottom edge and
        /// the choice cards sit below the screen, both facing the viewer. Moves existing objects.
        /// </summary>
        [MenuItem("Tools/Story/Layout AI UI Around Screen")]
        public static void LayoutAroundScreen()
        {
            var screen = Find<CurvedVideoScreen>();
            var connector = Find<StoryFlowConnector>();
            if (screen == null || connector == null) { Debug.LogError("Need the CurvedVideoScreen and StoryFlowConnector."); return; }
            var so = new SerializedObject(connector);
            var choices = so.FindProperty("choiceRoot").objectReferenceValue as GameObject;
            var narration = so.FindProperty("narration").objectReferenceValue as NarrationPanel;

            Transform t = screen.transform;
            float aspect = Mathf.Max(.1f, screen.aspectRatio);
            float halfHeight = screen.width / aspect * .5f * t.lossyScale.y;
            Camera eye = Camera.main;
            Vector3 viewer = eye != null ? eye.transform.position : t.position - t.forward * 2;
            Vector3 toViewer = Vector3.Dot(viewer - t.position, t.forward) >= 0 ? t.forward : -t.forward;
            // World-space canvases read correctly when their forward points away from the viewer.
            Quaternion facing = Quaternion.LookRotation(-toViewer, t.up);

            if (narration != null)
            {
                // Sanity check against the previous (readable) placement.
                Debug.Log($"Layout: narration facing agreement {Vector3.Dot(narration.transform.forward, facing * Vector3.forward):0.00}, " +
                          $"screen half height {halfHeight:0.00} m, viewer {viewer}");
                ApplySubtitleStyle(narration);
                Place(narration.transform, t.position - t.up * (halfHeight - .14f) + toViewer * .04f, facing);
            }
            if (choices != null)
                Place(CanvasOf(choices), t.position - t.up * (halfHeight + .3f) + toViewer * .35f, facing, choices.transform);
            EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
            Debug.Log("AI UI laid out around the screen: subtitles on its bottom edge, choices below it.");
        }

        static Transform CanvasOf(GameObject root)
        {
            Canvas canvas = root.GetComponentInChildren<Canvas>(true);
            return canvas != null ? canvas.transform : root.transform;
        }

        // Moves `root` so that `target` (itself or a child) ends at the given pose.
        static void Place(Transform target, Vector3 position, Quaternion rotation, Transform root = null)
        {
            root = root != null ? root : target;
            Undo.RecordObject(root, "Layout AI UI");
            root.rotation = rotation * Quaternion.Inverse(Quaternion.Inverse(root.rotation) * target.rotation);
            root.position += position - target.position;
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);
        }

        static void ApplySubtitleStyle(NarrationPanel narration)
        {
            var so = new SerializedObject(narration);
            var text = so.FindProperty("narrationText").objectReferenceValue as TMP_Text;
            var panel = so.FindProperty("panel").objectReferenceValue as GameObject;
            var rect = narration.transform as RectTransform;
            if (rect != null)
            {
                Undo.RecordObject(rect, "Subtitle width");
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 1700);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
            }
            if (text != null)
            {
                Undo.RecordObject(text, "Subtitle text");
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
                if (font != null) text.font = font;
                text.fontSize = 46;
                PrefabUtility.RecordPrefabInstancePropertyModifications(text);
            }
            var image = panel != null ? panel.GetComponent<Image>() : null;
            if (image != null)
            {
                Undo.RecordObject(image, "Subtitle background");
                var color = image.color; color.a = .55f; image.color = color;
                PrefabUtility.RecordPrefabInstancePropertyModifications(image);
            }
        }

        [MenuItem("Tools/Story/Build Quest Story APK (1114)")]
        public static void BuildQuest()
        {
            foreach (string key in new[] { "ReactorLocalConfig", DirectorClient.ConfigResource })
                if (!File.Exists("Assets/Resources/" + key + ".json"))
                    throw new System.InvalidOperationException($"Missing Assets/Resources/{key}.json. Set it through Tools > Visko / Tools > Director.");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new System.InvalidOperationException("Install Android Build Support through Unity Hub first.");
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = true;
            Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = BuildTarget.Android,
                locationPathName = "Builds/TakeMeToTheMoon.apk",
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.InvalidOperationException("Quest build failed. Check the Unity Console.");
            Debug.Log("Built Builds/TakeMeToTheMoon.apk (contains local development keys; do not share).");
        }

        static T Find<T>() where T : Object =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();

        static GameObject FindByName(string name) =>
            Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.name == name)?.gameObject;

        // Explicit Unity null checks: GetComponent can return a "fake null" that ?? would keep.
        static T GetOrAdd<T>(GameObject host) where T : Component
        {
            T existing = host.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(host);
        }

        static GameObject Spawn(string path, StoryController story, Transform anchor, float up)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), story.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(go, "Add " + go.name);
            if (anchor == null) return go;
            // Put the prefab's canvas where the anchor canvas is, raised by `up` metres.
            Canvas child = go.GetComponentInChildren<Canvas>(true);
            Transform canvas = child != null ? child.transform : go.transform;
            go.transform.rotation = anchor.rotation * Quaternion.Inverse(Quaternion.Inverse(go.transform.rotation) * canvas.rotation);
            go.transform.position += anchor.position + anchor.up * up - canvas.position;
            return go;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        static void Set(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).floatValue = value;
            so.ApplyModifiedProperties();
        }

        static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).boolValue = value;
            so.ApplyModifiedProperties();
        }

        static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedProperties();
        }
    }
}
