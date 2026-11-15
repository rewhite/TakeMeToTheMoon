using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class WellnessBarMenu
    {
        private const string Path = "Assets/Prefabs/Wellness Bar.prefab";
        [MenuItem("Tools/Game Flow/Create Wellness Bar Prefab")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (prefab == null)
            {
                Import("Assets/Images/heart.png");
                Import("Assets/Images/Wellness Bar_BG.png");
                var root = new GameObject("Wellness Bar", typeof(RectTransform), typeof(Canvas), typeof(WellnessBar));
                try
                {
                    var rect = (RectTransform)root.transform;
                    rect.sizeDelta = new Vector2(480, 186);
                    rect.localPosition = new Vector3(0, 1.6f, 2);
                    rect.localScale = Vector3.one * .0015f;
                    root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                    var bg = Image(root.transform, "Background", "Wellness Bar_BG", new Vector2(480, 186), Vector2.zero);
                    var shadow = bg.gameObject.AddComponent<Shadow>();
                    shadow.effectColor = new Color(0, 0, 0, .22f);
                    shadow.effectDistance = new Vector2(0, -7);
                    var heart = Image(root.transform, "Heart", "heart", new Vector2(90, 90), new Vector2(-132, 0));
                    var label = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI));
                    label.transform.SetParent(root.transform, false);
                    var text = label.GetComponent<TextMeshProUGUI>();
                    text.rectTransform.sizeDelta = new Vector2(240, 146);
                    text.rectTransform.anchoredPosition = new Vector2(58, 0);
                    text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/RubikDirt-Regular SDF.asset");
                    text.fontSize = 108;
                    text.text = "100";
                    text.color = new Color(.98f, .98f, .92f);
                    text.alignment = TextAlignmentOptions.Center;
                    text.raycastTarget = false;
                    var settings = new SerializedObject(root.GetComponent<WellnessBar>());
                    settings.FindProperty("valueText").objectReferenceValue = text;
                    settings.FindProperty("heart").objectReferenceValue = heart.rectTransform;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, Path);
                }
                finally { Object.DestroyImmediate(root); }
            }
            Preview(prefab);
            Selection.activeObject = prefab;
            Debug.Log("Wellness Bar prefab created and rendered: StoryController wellness, double heartbeat from 55 BPM at 100 to 150 BPM at 0. Preview: /tmp/wellness-bar-preview.png");
        }
        private static void Import(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
        private static Image Image(Transform parent, string name, string asset, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Images/" + asset + ".png");
            image.rectTransform.sizeDelta = size;
            image.rectTransform.anchoredPosition = position;
            image.raycastTarget = false;
            return image;
        }
        private static void Preview(GameObject prefab)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            Texture2D result = null;
            var old = RenderTexture.active;
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                root.transform.position = new Vector3(0, 0, 2);
                root.transform.localScale = Vector3.one * .001f;
                var cameraGo = new GameObject("Preview Camera", typeof(Camera));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo, scene);
                var camera = cameraGo.GetComponent<Camera>();
                camera.scene = scene; camera.orthographic = true; camera.orthographicSize = .146f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .12f, .12f);
                camera.nearClipPlane = .01f; camera.farClipPlane = 10;
                foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();
                target = new RenderTexture(582, 292, 24);
                camera.targetTexture = target; camera.Render(); camera.targetTexture = null;
                RenderTexture.active = target;
                result = new Texture2D(582, 292, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, 582, 292), 0, 0); result.Apply();
                System.IO.File.WriteAllBytes("/tmp/wellness-bar-preview.png", result.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = old;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (result != null) Object.DestroyImmediate(result);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
