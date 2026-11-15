using System;
using System.Linq;
using TakeMeToTheMoon.Story;
using TakeMeToTheMoon.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Visko;
using Object = UnityEngine.Object;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class StageViskoSetup
    {
        [MenuItem("Tools/Game Flow/Connect Stage 1 Visko")]
        public static void Configure()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/1114.unity") throw new InvalidOperationException("Open 1114 first.");
            T Find<T>() where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.gameObject.scene == scene);
            var story = Find<StoryController>();
            var manager = Find<GameManager>();
            var shared = Find<SharedVideoPlayer>();
            if (story == null || manager == null || shared == null) throw new InvalidOperationException("Existing story, manager and shared video are required.");
            var host = story.gameObject;
            var adapter = host.GetComponent<StageViskoPresentation>() ?? Undo.AddComponent<StageViskoPresentation>(host);
            var director = host.GetComponent<DirectorClient>() ?? Undo.AddComponent<DirectorClient>(host);
            Set(director, "controller", story);
            Set(director, "systemPrompt", AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Story/DirectorSystemPrompt.txt"));
            var visko = Find<ViskoStreamClient>();
            if (visko == null) visko = Spawn("Assets/Prefabs/Visko Stream.prefab").GetComponent<ViskoStreamClient>();
            Undo.RecordObject(visko, "Configure stage 1 stream");
            visko.connectOnStart = visko.autoStart = false;
            visko.externalScreen = true;
            visko.showWorldStatus = visko.showDebugGui = false;
            visko.model = "reactor/visko-orbis-stable";
            PrefabUtility.RecordPrefabInstancePropertyModifications(visko);
            // Keep all authoring visible and persistent; nothing is spawned by runtime setup.
            var data = new SerializedObject(adapter);
            var choices = data.FindProperty("choiceRoot").objectReferenceValue as GameObject;
            bool newChoices = choices == null;
            if (choices == null) choices = Spawn("Assets/Prefabs/Choice Cards.prefab");
            var narration = data.FindProperty("narration").objectReferenceValue as NarrationPanel;
            bool newNarration = narration == null;
            if (narration == null) narration = Spawn("Assets/Prefabs/Narration Canvas.prefab").GetComponent<NarrationPanel>();
            var anchor = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.gameObject.scene == scene && x.name == "Item Selection Panel");
            if (anchor != null)
            {
                if (newChoices) Align(choices, anchor, 0);
                if (newNarration) Align(narration.gameObject, anchor, .45f);
            }
            var buttons = choices.GetComponentsInChildren<Button>(true).OrderBy(x => x.transform.position.x).ToArray();
            Set(adapter, "gameManager", manager); Set(adapter, "story", story);
            Set(adapter, "viskoClient", visko); Set(adapter, "sharedVideo", shared);
            Set(adapter, "choiceRoot", choices); Set(adapter, "narration", narration);
            data.Update();
            var bp = data.FindProperty("choiceButtons"); var lp = data.FindProperty("choiceLabels");
            bp.arraySize = lp.arraySize = buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
            {
                bp.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
                lp.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i].GetComponentInChildren<TMP_Text>(true);
            }
            data.FindProperty("targetStage").intValue = 1;
            data.FindProperty("outcomeVideoSeconds").floatValue = 10;
            data.FindProperty("choicesDelaySeconds").floatValue = 2.5f;
            data.FindProperty("choicesFadeSeconds").floatValue = .4f;
            data.FindProperty("playbackBufferSeconds").floatValue = 1.8f;
            data.ApplyModifiedProperties();
            var gameData = new SerializedObject(manager);
            var stages = gameData.FindProperty("stages");
            var stage2 = stages.GetArrayElementAtIndex(1);
            stage2.FindPropertyRelative("screenPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Game Flow/Stage 2.prefab");
            stage2.FindPropertyRelative("revealWhen").enumValueIndex = (int)RevealCondition.AfterSeconds;
            var stage = stages.GetArrayElementAtIndex(0);
            var oldRoot = stage.FindPropertyRelative("screenPrefab").objectReferenceValue as GameObject;
            if (oldRoot != null && oldRoot.scene.IsValid())
            {
                Undo.RecordObject(oldRoot, "Hide replaced Stage 1 screen");
                oldRoot.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(oldRoot);
            }
            stage.FindPropertyRelative("videoClip").objectReferenceValue = null;
            stage.FindPropertyRelative("videoUrl").stringValue = "";
            stage.FindPropertyRelative("screenPrefab").objectReferenceValue = null;
            stage.FindPropertyRelative("revealWhen").enumValueIndex = (int)RevealCondition.Manual;
            // The live presenter alone reveals its cards. Remove the legacy VideoEnded panel
            // from the manager's reveal list so it cannot advance the opening on enable.
            var revealTargets = stage.FindPropertyRelative("revealObjects");
            for (int i = 0; i < revealTargets.arraySize; i++)
            {
                var oldPanel = revealTargets.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                if (oldPanel == null || !oldPanel.scene.IsValid()) continue;
                Undo.RecordObject(oldPanel, "Hide legacy Stage 1 reveal");
                oldPanel.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(oldPanel);
            }
            revealTargets.arraySize = 0;
            gameData.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
            foreach (var old in host.GetComponents<StoryFlowConnector>()) Undo.DestroyObjectImmediate(old);
            foreach (var old in host.GetComponents<ViskoStoryBridge>()) Undo.DestroyObjectImmediate(old);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
            Selection.activeGameObject = visko.gameObject;
            Debug.Log($"STAGE 1 VISKO CONNECTED: saved 1114; {buttons.Length} choices; outcome 10 seconds; client={visko.name}", adapter);
        }
        static GameObject Spawn(string path)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            Undo.RegisterCreatedObjectUndo(go, "Add Stage 1 presentation");
            return go;
        }
        static void Align(GameObject go, Transform anchor, float up)
        {
            Undo.RecordObject(go.transform, "Place Stage 1 UI");
            var canvas = go.GetComponentInChildren<Canvas>(true);
            Transform face = canvas != null ? canvas.transform : go.transform;
            go.transform.rotation = anchor.rotation * Quaternion.Inverse(Quaternion.Inverse(go.transform.rotation) * face.rotation);
            go.transform.position += anchor.position + anchor.up * up - face.position;
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
        }
        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }
}
