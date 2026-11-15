using System;
using Oculus.Interaction;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class ChoiceCardButtonMenu
    {
        private const string Folder = "Assets/Prefabs/";

        [MenuItem("Tools/Game Flow/Create Choice Card Buttons")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var green = MakeButton("Green", "I'll wake my roommate\nup.");
            var blue = MakeButton("Blue", "Option title");
            const string path = Folder + "Choice Cards.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                var root = new GameObject("Choice Cards", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                try
                {
                    var rect = (RectTransform)root.transform;
                    rect.sizeDelta = new Vector2(920, 360);
                    rect.localPosition = new Vector3(0, 0, 2);
                    rect.localScale = Vector3.one * .0015f;
                    var canvas = root.GetComponent<Canvas>();
                    canvas.renderMode = RenderMode.WorldSpace;
                    AddButton(root.transform, green, -240);
                    AddButton(root.transform, blue, 240);
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("8369d93f7b6b99742bbea0649a41b7b1"));
                    if (source == null) throw new InvalidOperationException("Missing Meta ray Canvas interaction prefab.");
                    var interaction = Object.Instantiate(source, root.transform, false);
                    interaction.name = "ISDK_RayCanvasInteraction";
                    var ir = (RectTransform)interaction.transform;
                    ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
                    ir.offsetMin = ir.offsetMax = Vector2.zero;
                    interaction.GetComponent<PointableCanvas>().InjectCanvas(canvas);
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { Object.DestroyImmediate(root); }
            }
            Preview(prefab);
            Selection.activeObject = prefab;
            Debug.Log("Choice cards created: 360x232 hit area, 440x316 artwork including shadows, editable Rubik Dirt labels, Button On Click, and Meta ray Canvas. Preview: /tmp/choice-cards-preview.png");
        }

        private static GameObject MakeButton(string color, string copy)
        {
            string imagePath = "Assets/Images/" + color + "Card.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(imagePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 300;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            string path = Folder + color + " Card Button.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var root = new GameObject(color + " Card Button", typeof(RectTransform), typeof(Image), typeof(Button));
            try
            {
                ((RectTransform)root.transform).sizeDelta = new Vector2(360, 232);
                // Only this rectangle receives clicks; artwork and shadow do not enlarge the hit area.
                var hit = root.GetComponent<Image>();
                hit.color = Color.clear;
                hit.raycastTarget = true;
                var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
                bg.transform.SetParent(root.transform, false);
                var br = (RectTransform)bg.transform;
                br.sizeDelta = new Vector2(440, 316);
                // Source body: x=120..1200, y=84..780 from the top (3x export).
                br.anchoredPosition = new Vector2(0, -14);
                var image = bg.GetComponent<Image>();
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(imagePath);
                image.type = Image.Type.Simple;
                image.raycastTarget = false;
                var button = root.GetComponent<Button>();
                button.targetGraphic = image;
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1);
                colors.selectedColor = Color.white;
                colors.pressedColor = new Color(.72f, .72f, .72f, 1);
                colors.disabledColor = new Color(.5f, .5f, .5f, .55f);
                colors.fadeDuration = .1f;
                button.colors = colors;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                label.transform.SetParent(root.transform, false);
                var lr = (RectTransform)label.transform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = new Vector2(24, 24); lr.offsetMax = new Vector2(-24, -24);
                var text = label.GetComponent<TextMeshProUGUI>();
                text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/RubikDirt-Regular SDF.asset");
                text.text = copy;
                text.fontSize = 27;
                text.enableAutoSizing = true;
                text.fontSizeMin = 18; text.fontSizeMax = 27;
                text.color = new Color(.91f, .9f, .80f);
                text.alignment = TextAlignmentOptions.Center;
                text.raycastTarget = false;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void AddButton(Transform parent, GameObject prefab, float x)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            ((RectTransform)instance.transform).anchoredPosition = new Vector2(x, 14);
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
                camera.scene = scene; camera.orthographic = true; camera.orthographicSize = .18f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.12f, .12f, .12f);
                camera.nearClipPlane = .01f; camera.farClipPlane = 10;
                foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();
                target = new RenderTexture(960, 360, 24);
                camera.targetTexture = target; camera.Render(); camera.targetTexture = null;
                RenderTexture.active = target;
                result = new Texture2D(960, 360, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, 960, 360), 0, 0); result.Apply();
                System.IO.File.WriteAllBytes("/tmp/choice-cards-preview.png", result.EncodeToPNG());
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
