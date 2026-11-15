using System;
using System.Reflection;
using TakeMeToTheMoon.Video;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class PersistentVideoValidation
    {
        [MenuItem("Tools/Game Flow/Validate Persistent Video (Play Mode)")]
        public static void Validate()
        {
            if (!Application.isPlaying) throw new Exception("Enter Play Mode first.");
            var source = UnityEngine.Object.FindAnyObjectByType<CurvedVideoScreen>();
            if (source == null) throw new Exception("Scene video screen required.");
            var root = new GameObject("Persistent Video Validation");
            root.SetActive(false);
            var material = new Material(source.screenMaterial);
            try
            {
                var screen = root.AddComponent<CurvedVideoScreen>();
                screen.screenMaterial = material;
                var player = root.AddComponent<VideoPlayer>();
                var shared = root.AddComponent<SharedVideoPlayer>();
                var settings = new SerializedObject(shared);
                settings.FindProperty("screen").objectReferenceValue = screen;
                settings.FindProperty("videoPlayer").objectReferenceValue = player;
                settings.ApplyModifiedPropertiesWithoutUndo();
                root.SetActive(true);
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var opacity = typeof(SharedVideoPlayer).GetField("opacity", flags);
                typeof(SharedVideoPlayer).GetMethod("SetOpacity", flags).Invoke(shared, new object[] { 1f });
                int started = 0, ended = 0;
                for (int stage = 1; stage <= 5; stage++)
                {
                    shared.PauseForTransition();
                    if ((float)opacity.GetValue(shared) != 1) throw new Exception("Transition hid video panel.");
                    shared.Play(new StagePresentation(), () => started++, () => ended++);
                    var block = new MaterialPropertyBlock();
                    screen.ScreenRenderer.GetPropertyBlock(block);
                    if (!Mathf.Approximately(block.GetColor("_BaseColor").a, material.GetColor("_BaseColor").a))
                        throw new Exception("Empty stage hid video surface.");
                }
                if (started != 5 || ended != 5) throw new Exception("Empty stage reveal callbacks incorrect.");
                shared.Cancel();
                if ((float)opacity.GetValue(shared) != 0) throw new Exception("Start/end cleanup failed.");
                Debug.Log("PERSISTENT VIDEO VALIDATION PASSED: five transitions retain panel opacity, empty stages reveal once, exit cleanup hides panel.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
