using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class FuelInstructionPanelSetup
    {
        private const string Path = "Assets/Prefabs/Fuel Instruction Panel.prefab";
        [MenuItem("Tools/Game Flow/Create Fuel Instruction Panel Prefab")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (prefab == null)
            {
                var root = new GameObject("Fuel Instruction Panel", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var rect = (RectTransform)root.transform;
                    rect.sizeDelta = new Vector2(1180, 480);
                    rect.localScale = Vector3.one * .0012f;
                    rect.localPosition = new Vector3(0, 0, 1.6f);
                    root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                    var panel = Background(root.transform, "Instruction Background", new Vector2(1110, 348), new Vector2(0, 30));
                    Text(root.transform, "Title", "Fill the fuel!!", "RubikDirt-Regular", 52, new Vector2(1000, 80), new Vector2(0, 115), false);
                    Text(root.transform, "Instructions", "In order to fill the fuel, you need to grab one of the\nobjects and hit the target. You have 3 chances.", "EastSeaDokdo-Regular", 60, new Vector2(1000, 155), new Vector2(0, -4), true);
                    var counter = Background(root.transform, "Throws Counter", new Vector2(346, 90), new Vector2(20, -152));
                    var outline = counter.gameObject.AddComponent<Outline>();
                    outline.effectColor = new Color(.65f, .37f, .1f, .85f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);
                    Text(counter.transform, "Counter Heading", "THROWS LEFT", "RubikDirt-Regular", 15, new Vector2(140, 22), new Vector2(-80, 22), true);
                    var count = Text(counter.transform, "Count", "3 / 3", "RubikDirt-Regular", 32, new Vector2(140, 40), new Vector2(-80, -10), true);
                    var images = new Image[3];
                    var marks = new GameObject[3];
                    for (int i = 0; i < 3; i++)
                    {
                        var go = new GameObject("Fuel Icon " + (i + 1), typeof(RectTransform), typeof(Image));
                        go.transform.SetParent(counter.transform, false);
                        var ir = (RectTransform)go.transform;
                        ir.sizeDelta = new Vector2(26, 36);
                        ir.anchoredPosition = new Vector2(17 + i * 55, -1);
                        images[i] = go.GetComponent<Image>();
                        images[i].raycastTarget = false;
                        var mark = Text(go.transform, "Placeholder Mark", "×", "RubikDirt-Regular", 25, new Vector2(26, 36), Vector2.zero, false);
                        mark.color = new Color(.08f, .08f, .05f);
                        marks[i] = mark.gameObject;
                    }
                    var view = root.AddComponent<FuelInstructionPanel>();
                    var settings = new SerializedObject(view);
                    settings.FindProperty("counterText").objectReferenceValue = count;
                    settings.FindProperty("panelBackground").objectReferenceValue = panel;
                    settings.FindProperty("counterBackground").objectReferenceValue = counter;
                    var icons = settings.FindProperty("fuelIcons"); icons.arraySize = 3;
                    var labels = settings.FindProperty("placeholderMarks"); labels.arraySize = 3;
                    for (int i = 0; i < 3; i++)
                    {
                        icons.GetArrayElementAtIndex(i).objectReferenceValue = images[i];
                        labels.GetArrayElementAtIndex(i).objectReferenceValue = marks[i];
                    }
                    settings.ApplyModifiedPropertiesWithoutUndo();
                    view.SetThrowsLeft(3);
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, Path);
                }
                finally { Object.DestroyImmediate(root); }
            }
            ApplyArtwork(prefab);
            PreviewAndValidate(prefab);
            Selection.activeObject = prefab;
            Debug.Log("FUEL PANEL READY: prefab only, editable title/instructions, 0–3 display, replaceable panel/counter/fuel artwork. Preview: /tmp/fuel-panel-preview.png");
        }
        private static void ApplyArtwork(GameObject prefab)
        {
            string[] paths = { "Assets/Images/Throws Panel.png", "Assets/Images/Can (ready).png", "Assets/Images/Can (used).png" };
            if (paths.Any(path => !System.IO.File.Exists(path))) return;
            foreach (var path in paths)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                var textureSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(textureSettings);
                textureSettings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(textureSettings);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            var root = PrefabUtility.LoadPrefabContents(Path);
            try
            {
                var view = root.GetComponent<FuelInstructionPanel>();
                var settings = new SerializedObject(view);
                settings.FindProperty("counterArtwork").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(paths[0]);
                settings.FindProperty("fuelArtwork").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(paths[1]);
                settings.FindProperty("usedFuelArtwork").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(paths[2]);
                settings.ApplyModifiedPropertiesWithoutUndo();
                var counter = (Image)settings.FindProperty("counterBackground").objectReferenceValue;
                counter.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(paths[0]);
                counter.type = Image.Type.Simple;
                counter.rectTransform.sizeDelta = new Vector2(346, 346f * 213 / 804);
                var outline = counter.GetComponent<Outline>();
                if (outline != null) outline.enabled = false; // Border is included in supplied artwork.
                var icons = settings.FindProperty("fuelIcons");
                for (int index = 0; index < icons.arraySize; index++)
                    ((Image)icons.GetArrayElementAtIndex(index).objectReferenceValue).rectTransform.sizeDelta = new Vector2(44, 52);
                view.SetThrowsLeft(view.ThrowsLeft);
                PrefabUtility.SaveAsPrefabAsset(root, Path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Image Background(Transform parent, string name, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.rectTransform.sizeDelta = size;
            image.rectTransform.anchoredPosition = position;
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Images/Canvas Panel.png");
            image.type = Image.Type.Tiled;
            image.pixelsPerUnitMultiplier = 3;
            image.raycastTarget = false;
            return image;
        }
        private static TextMeshProUGUI Text(Transform parent, string name, string copy, string font, float fontSize, Vector2 size, Vector2 position, bool left)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.rectTransform.sizeDelta = size;
            text.rectTransform.anchoredPosition = position;
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/" + font + " SDF.asset");
            text.text = copy;
            text.fontSize = fontSize;
            text.color = left ? new Color(.65f, .64f, .55f) : new Color(.91f, .9f, .8f);
            if (name == "Count") text.color = new Color(.91f, .9f, .8f);
            text.alignment = left ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }
        private static void PreviewAndValidate(GameObject prefab)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            RenderTexture rt = null; Texture2D image = null;
            var previous = RenderTexture.active;
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                root.transform.position = new Vector3(0, 0, 2);
                root.transform.localScale = Vector3.one * .001f;
                var display = root.GetComponent<FuelInstructionPanel>();
                var displayData = new SerializedObject(display);
                var ready = displayData.FindProperty("fuelArtwork").objectReferenceValue as Sprite;
                var used = displayData.FindProperty("usedFuelArtwork").objectReferenceValue as Sprite;
                var icons = displayData.FindProperty("fuelIcons");
                for (int count = 0; count <= 3; count++)
                {
                    display.SetThrowsLeft(count);
                    if (((TMP_Text)displayData.FindProperty("counterText").objectReferenceValue).text != $"{count} / 3")
                        throw new System.Exception("Fuel counter text mismatch.");
                    if (ready != null && used != null)
                        for (int i = 0; i < icons.arraySize; i++)
                            if (((Image)icons.GetArrayElementAtIndex(i).objectReferenceValue).sprite != (i < count ? ready : used))
                                throw new System.Exception("Ready/used artwork mismatch.");
                }
                display.SetThrowsLeft(-1);
                if (display.ThrowsLeft != 0) throw new System.Exception("Throw count lower clamp failed.");
                display.SetThrowsLeft(8);
                if (display.ThrowsLeft != 3) throw new System.Exception("Throw count upper clamp failed.");
                var go = new GameObject("Preview Camera", typeof(Camera));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                var camera = go.GetComponent<Camera>(); camera.scene = scene;
                camera.orthographic = true; camera.orthographicSize = .255f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.16f, .16f, .16f);
                camera.nearClipPlane = .01f; camera.farClipPlane = 10;
                Canvas.ForceUpdateCanvases();
                foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>())
                {
                    text.ForceMeshUpdate();
                    if (text.isTextOverflowing) throw new System.Exception("Fuel panel text overflow: " + text.name);
                }
                rt = new RenderTexture(1180, 510, 24); camera.targetTexture = rt; camera.Render();
                camera.targetTexture = null; RenderTexture.active = rt;
                image = new Texture2D(1180, 510, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1180, 510), 0, 0); image.Apply();
                System.IO.File.WriteAllBytes("/tmp/fuel-panel-preview.png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (image != null) Object.DestroyImmediate(image);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
