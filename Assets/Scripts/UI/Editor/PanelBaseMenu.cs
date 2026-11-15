using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class PanelBaseMenu
    {
        private const string PrefabPath = "Assets/Prefabs/Panel Base.prefab";
        private const string ImagePath = "Assets/Images/Canvas Panel.png";

        [MenuItem("Tools/Game Flow/Create Panel Base Prefab")]
        public static void CreatePrefab()
        {
            var importer = AssetImporter.GetAtPath(ImagePath) as TextureImporter;
            if (importer == null) throw new System.InvalidOperationException("Missing Canvas Panel.png");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(24, 24, 24, 24);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) { Selection.activeObject = existing; return; }
            var root = new GameObject("Panel Base", typeof(RectTransform));
            try
            {
                ((RectTransform)root.transform).sizeDelta = new Vector2(1000, 260);
                var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
                Stretch((RectTransform)background.transform, root.transform, 0);
                var image = background.GetComponent<Image>();
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ImagePath);
                image.type = Image.Type.Sliced;
                image.fillCenter = true;
                image.raycastTarget = false;
                var content = new GameObject("Content", typeof(RectTransform));
                Stretch((RectTransform)content.transform, root.transform, 24);
                Selection.activeObject = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("Panel Base created: sliced background, 24px borders and content padding.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("GameObject/UI/Panel Base", false, 10)]
        private static void Add(MenuCommand command)
        {
            var parent = command.context as GameObject;
            if (parent == null && Selection.activeGameObject != null) parent = Selection.activeGameObject;
            if (parent == null || parent.GetComponentInParent<Canvas>() == null)
            {
                Debug.LogWarning("Select an existing Canvas or a child of a Canvas before adding Panel Base.");
                return;
            }
            CreatePrefab();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), parent.transform);
            Undo.RegisterCreatedObjectUndo(instance, "Add Panel Base");
            instance.transform.localScale = Vector3.one;
            instance.transform.localPosition = Vector3.zero;
            Selection.activeGameObject = instance;
        }

        private static void Stretch(RectTransform rect, Transform parent, float padding)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * padding;
            rect.offsetMax = -Vector2.one * padding;
        }
    }
}
