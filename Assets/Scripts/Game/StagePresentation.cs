using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Video;

namespace TakeMeToTheMoon.Game
{
    public enum RevealCondition { AfterSeconds, VideoEnded, Manual }

    [Serializable]
    public sealed class StagePresentation
    {
        public string title;
        [Tooltip("Preplaced scene root or prefab asset. Keep the shared video outside this root.")]
        public GameObject screenPrefab;
        public Material skybox;
        public VideoClip videoClip;
        [Tooltip("Used when Video Clip is empty. Leave both empty for a stage without video.")]
        public string videoUrl;
        [Min(0)] public float videoFadeSeconds = 2;
        public RevealCondition revealWhen = RevealCondition.AfterSeconds;
        [Min(0)] public float revealAfterSeconds = 5;
        [Tooltip("Optional scene objects. For prefab-local objects use GameStageView on the prefab root.")]
        public GameObject[] revealObjects = Array.Empty<GameObject>();
        public UnityEvent Revealed = new UnityEvent();
    }
}
