using System.IO;
using System.Linq;
using TakeMeToTheMoon.Game;
using TakeMeToTheMoon.UI;
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

            EditorSceneManager.MarkSceneDirty(story.gameObject.scene);
            Debug.Log($"AI story flow configured: {buttons.Length} choice buttons, timer {(timer != null ? "found" : "missing")}, " +
                      $"item selection {(FindByName("Item Selection") != null ? "found" : "missing")}. Save the scene.");
        }

        /// <summary>Batch mode: -executeMethod TakeMeToTheMoon.Story.Editor.StoryFlowSetup.ConfigureAndSave</summary>
        public static void ConfigureAndSave()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Configure();
            EditorSceneManager.SaveScene(scene);
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
