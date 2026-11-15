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
        public static void Configure()
        {
            var manager = Object.FindFirstObjectByType<GameManager>();
            var player = Object.FindFirstObjectByType<VideoPlayer>();
            var screen = Object.FindFirstObjectByType<CurvedVideoScreen>();
            if (manager == null || player == null || screen == null)
            {
                Debug.LogError("Scene requires GameManager, VideoPlayer and CurvedVideoScreen before configuring the prologue.");
                return;
            }
            // The video must remain active while GameManager hides Main.
            if (screen.transform.parent != null)
                Undo.SetTransformParent(screen.transform, null, "Keep Prologue Screen Persistent");
            var prologue = manager.GetComponent<PrologueManager>();
            if (prologue == null) prologue = Undo.AddComponent<PrologueManager>(manager.gameObject);
            var so = new SerializedObject(prologue);
            so.FindProperty("videoPlayer").objectReferenceValue = player;
            so.FindProperty("screen").objectReferenceValue = screen;
            so.ApplyModifiedProperties();
            so = new SerializedObject(manager);
            so.FindProperty("prologueManager").objectReferenceValue = prologue;
            so.ApplyModifiedProperties();
            Undo.RecordObject(player, "Configure Prologue Video");
            player.playOnAwake = false;
            player.isLooping = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Selection.activeGameObject = manager.gameObject;
            Debug.Log("Prologue configured: START -> fade in video -> Main. Set timing and optional next scene on PrologueManager.");
        }
    }
}
