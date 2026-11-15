using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;
using TakeMeToTheMoon.Video;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class PrologueSetup
    {
        [MenuItem("Tools/Game Flow/Configure Prologue")]
        [MenuItem("Tools/Game Flow/Configure Five Stages")]
        public static void Configure()
        {
            var manager = Object.FindFirstObjectByType<GameManager>();
            if (manager == null) { Debug.LogError("Scene needs GameManager."); return; }
            var settings = new SerializedObject(manager);
            var existing = settings.FindProperty("sharedVideo").objectReferenceValue;
            if (existing != null)
            {
                var retired = manager.GetComponent<PrologueManager>();
                if (retired != null) Undo.DestroyObjectImmediate(retired);
                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                Selection.activeObject = manager;
                return;
            }
            var legacy = manager.GetComponent<PrologueManager>();
            var old = legacy != null ? new SerializedObject(legacy) : null;
            var player = old?.FindProperty("videoPlayer").objectReferenceValue as VideoPlayer;
            var screen = old?.FindProperty("screen").objectReferenceValue as CurvedVideoScreen;
            if (player == null) player = Object.FindFirstObjectByType<VideoPlayer>(FindObjectsInactive.Include);
            if (screen == null) screen = Object.FindFirstObjectByType<CurvedVideoScreen>(FindObjectsInactive.Include);
            if (player == null || screen == null) { Debug.LogError("Scene needs a VideoPlayer and CurvedVideoScreen."); return; }
            var shared = manager.GetComponent<SharedVideoPlayer>();
            if (shared == null) shared = Undo.AddComponent<SharedVideoPlayer>(manager.gameObject);
            var video = new SerializedObject(shared);
            video.FindProperty("videoPlayer").objectReferenceValue = player;
            video.FindProperty("screen").objectReferenceValue = screen;
            video.ApplyModifiedProperties();
            settings.FindProperty("sharedVideo").objectReferenceValue = shared;
            var intro = settings.FindProperty("prologue");
            intro.FindPropertyRelative("screenPrefab").objectReferenceValue = settings.FindProperty("prologuePrefab").objectReferenceValue;
            intro.FindPropertyRelative("skybox").objectReferenceValue = settings.FindProperty("prologueSkybox").objectReferenceValue ?? settings.FindProperty("mainSkybox").objectReferenceValue;
            intro.FindPropertyRelative("videoClip").objectReferenceValue = player.source == VideoSource.VideoClip ? player.clip : null;
            intro.FindPropertyRelative("videoUrl").stringValue = player.source == VideoSource.Url ? player.url : "";
            if (old != null)
            {
                intro.FindPropertyRelative("videoFadeSeconds").floatValue = old.FindProperty("fadeInSeconds").floatValue;
                intro.FindPropertyRelative("revealWhen").enumValueIndex = old.FindProperty("advanceWhenVideoEnds").boolValue ? 1 : 0;
                intro.FindPropertyRelative("revealAfterSeconds").floatValue = old.FindProperty("durationSeconds").floatValue;
                Undo.RecordObject(legacy, "Retire legacy prologue playback");
                legacy.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(legacy);
            }
            var stages = settings.FindProperty("stages");
            stages.arraySize = 5;
            for (int i = 0; i < 5; i++)
            {
                var stage = stages.GetArrayElementAtIndex(i);
                stage.FindPropertyRelative("title").stringValue = "Stage " + (i + 1);
                if (i == 0)
                {
                    stage.FindPropertyRelative("screenPrefab").objectReferenceValue = settings.FindProperty("mainPrefab").objectReferenceValue;
                    stage.FindPropertyRelative("skybox").objectReferenceValue = settings.FindProperty("mainSkybox").objectReferenceValue;
                }
                else stage.FindPropertyRelative("screenPrefab").objectReferenceValue = MakeStage(i + 1);
            }
            settings.ApplyModifiedProperties();
            if (legacy != null) Undo.DestroyObjectImmediate(legacy);
            Undo.RecordObject(player, "Configure shared video");
            player.playOnAwake = false;
            player.isLooping = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Selection.activeObject = manager;
            Debug.Log("Five stages configured. Revealed shows UI only; connect a continue/interaction event to GameManager.NextStage.");
        }

        private static GameObject MakeStage(int number)
        {
            string path = "Assets/Prefabs/Game Flow/Stage " + number + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var root = new GameObject("Stage " + number);
            root.AddComponent<GameStageView>();
            try { return PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
