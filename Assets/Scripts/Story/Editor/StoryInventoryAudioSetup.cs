using Oculus.Interaction;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TakeMeToTheMoon.Story.Editor
{
    public static class StoryInventoryAudioSetup
    {
        [MenuItem("Tools/Story/Configure Inventory Audio")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var tray = Object.FindAnyObjectByType<StoryInventoryTray>(FindObjectsInactive.Include);
            if (tray == null) { Debug.LogError("Configure the inventory tray first."); return; }
            const string folder = "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Audio/Content/";
            var grabClip = AssetDatabase.LoadAssetAtPath<AudioClip>(folder + "Interaction_BasicGrab_Grab_01.wav");
            var dockClip = AssetDatabase.LoadAssetAtPath<AudioClip>(folder + "Interaction_BasicGrab_Release_01.wav");
            if (grabClip == null || dockClip == null) { Debug.LogError("Meta sample audio clips missing."); return; }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure inventory audio");
            var data = new SerializedObject(tray);
            var items = data.FindProperty("allowedItems");
            for (int i = 0; i < items.arraySize; i++)
            {
                var snap = items.GetArrayElementAtIndex(i).objectReferenceValue as SnapInteractor;
                if (snap == null || snap.GetComponent<StoryItemGrabAudio>() != null) continue;
                var audio = MakeAudio(snap.transform, "Grab Audio", grabClip, 0.45f);
                var feedback = Undo.AddComponent<StoryItemGrabAudio>(snap.gameObject);
                var settings = new SerializedObject(feedback);
                settings.FindProperty("grabbable").objectReferenceValue = snap.GetComponent<Grabbable>();
                settings.FindProperty("snapInteractor").objectReferenceValue = snap;
                settings.FindProperty("grabAudio").objectReferenceValue = audio;
                settings.ApplyModifiedProperties();
            }
            if (tray.transform.Find("Dock Audio") == null)
            {
                var audio = MakeAudio(tray.transform, "Dock Audio", dockClip, 0.65f);
                Undo.RecordObject(tray, "Connect accepted item sound");
                UnityEventTools.AddPersistentListener(tray.ItemDocked, audio.PlayAudio);
                EditorUtility.SetDirty(tray);
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(tray.gameObject.scene);
            Selection.activeGameObject = tray.gameObject;
            Debug.Log("Inventory audio configured: five grab sources and one accepted-dock source, all preplaced. Edit AudioTrigger clips/volume in Inspector.");
        }
        private static AudioTrigger MakeAudio(Transform parent, string name, AudioClip clip, float volume)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create scene audio source");
            go.transform.SetParent(parent, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1;
            source.minDistance = 0.3f;
            source.maxDistance = 5;
            source.dopplerLevel = 0;
            var trigger = go.AddComponent<AudioTrigger>();
            var data = new SerializedObject(trigger);
            data.FindProperty("_audioSource").objectReferenceValue = source;
            var clips = data.FindProperty("_audioClips");
            clips.arraySize = 1;
            clips.GetArrayElementAtIndex(0).objectReferenceValue = clip;
            data.FindProperty("_volume").floatValue = volume;
            // Unity 3D audio works without requiring a separately configured spatializer plugin.
            data.FindProperty("_spatialize").boolValue = false;
            data.ApplyModifiedProperties();
            return trigger;
        }
    }
}
