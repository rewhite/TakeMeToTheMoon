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
        [Tooltip("Optional fallback when Skybox Image is empty.")]
        public Material skybox;
        [Tooltip("360-degree equirectangular panorama (typically 2:1). Takes priority over Skybox material.")]
        public Texture2D skyboxImage;
        [Range(0, 360)] public float skyboxRotation;
        [Min(0)] public float skyboxExposure = 1;
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
