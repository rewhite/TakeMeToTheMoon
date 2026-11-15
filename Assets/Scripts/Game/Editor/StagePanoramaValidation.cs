using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class StagePanoramaValidation
    {
        [MenuItem("Tools/Game Flow/Validate Stage Panorama")]
        public static void Validate()
        {
            var template = Resources.Load<Material>("StagePanoramaTemplate");
            if (template == null || template.shader.name != "Skybox/Panoramic")
                throw new Exception("Stage panorama needs the bundled Skybox/Panoramic template.");
            var root = new GameObject("Panorama validation");
            root.SetActive(false);
            var texture = new Texture2D(2, 1);
            Material generated = null;
            var originalSkybox = RenderSettings.skybox;
            try
            {
                var manager = root.AddComponent<GameManager>();
                var resolve = typeof(GameManager).GetMethod("ResolveSkybox", BindingFlags.Instance | BindingFlags.NonPublic);
                var stage = new StagePresentation { skyboxImage = texture, skyboxRotation = 75, skyboxExposure = .7f };
                generated = (Material)resolve.Invoke(manager, new object[] { stage, template });
                if (generated == template || generated.GetTexture("_MainTex") != texture ||
                    !Mathf.Approximately(generated.GetFloat("_Rotation"), 75) ||
                    !Mathf.Approximately(generated.GetFloat("_Exposure"), .7f)) throw new Exception("Panorama image or settings not applied.");
                typeof(GameManager).GetField("currentStage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, stage);
                stage.skyboxRotation = 123;
                stage.skyboxExposure = 1.2f;
                typeof(GameManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
                if (RenderSettings.skybox != generated || !Mathf.Approximately(generated.GetFloat("_Rotation"), 123) ||
                    !Mathf.Approximately(generated.GetFloat("_Exposure"), 1.2f)) throw new Exception("Live skybox adjustment failed after reveal.");
                stage.skyboxImage = null;
                if ((Material)resolve.Invoke(manager, new object[] { stage, template }) != template)
                    throw new Exception("Material fallback failed.");
                if (template.GetTexture("_MainTex") != null) throw new Exception("Template was modified.");
                Debug.Log("STAGE PANORAMA VALIDATION PASSED: bundled panoramic shader, image priority, rotation/exposure, live rotation/exposure after reveal, material fallback, template unchanged.");
            }
            finally
            {
                RenderSettings.skybox = originalSkybox;
                if (generated != null) UnityEngine.Object.DestroyImmediate(generated);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
