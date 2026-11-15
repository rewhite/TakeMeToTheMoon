using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Oculus.Interaction;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class IntroPanelMenu
    {
        [MenuItem("Tools/Game Flow/Create Intro Panel Prefabs")]
        public static void CreatePrefabs()
        {
            PanelBaseMenu.CreatePrefab();
            var button = MakeButton();
            const string path = "Assets/Prefabs/Intro Panel.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
            var root = new GameObject("Intro Panel", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(IntroPanel));
            try
            {
                var rect = (RectTransform)root.transform;
                rect.sizeDelta = new Vector2(1000, 490);
                rect.localPosition = new Vector3(0, 0, 2);
                rect.localScale = Vector3.one * .0015f;
                root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var panel = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Panel Base.prefab"), root.transform);
                var pr = (RectTransform)panel.transform;
                pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one;
                pr.offsetMin = pr.offsetMax = Vector2.zero;
                var content = panel.transform.Find("Content");
                var badge = new GameObject("Prologue Badge", typeof(RectTransform), typeof(Image), typeof(Outline));
                badge.transform.SetParent(content, false);
                Place((RectTransform)badge.transform, 32, -28, 125, 34);
                badge.GetComponent<Image>().color = new Color(.19f, .2f, .12f, .8f);
                badge.GetComponent<Image>().raycastTarget = false;
                badge.GetComponent<Outline>().effectColor = new Color(.45f, .44f, .27f);
                var badgeText = Text(badge.transform, "Label", "PROLOGUE", 0, 0, 125, 34, 16, true);
                badgeText.alignment = TextAlignmentOptions.Center;
                Text(content, "Title", "Breaking News", 32, -82, 880, 64, 48, true);
                Text(content, "Body", "Earth has officially hit 451 degrees. Scientists say we had 'plenty of warnings' and 'one job.' The oceans are a hot tub. Lake Lag has boiled, which is impressive, because it was already empty. The Moon is now the only place with decent weather. You have thirty seconds to grab what you can. Good luck, and stay hydrated.", 32, -167, 880, 155, 23, false);
                Text(content, "Hint", "Pinch or pull the trigger to continue", 32, -368, 470, 38, 16, false);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(button, content);
                Place((RectTransform)instance.transform, 642, -340, 278, 72);
                var so = new SerializedObject(root.GetComponent<IntroPanel>());
                so.FindProperty("continueButton").objectReferenceValue = instance.GetComponent<Button>();
                so.ApplyModifiedPropertiesWithoutUndo();
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("8369d93f7b6b99742bbea0649a41b7b1"));
                if (source != null)
                {
                    var interaction = Object.Instantiate(source, root.transform, false);
                    interaction.name = "ISDK_RayCanvasInteraction";
                    var ir = (RectTransform)interaction.transform;
                    ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
                    ir.offsetMin = ir.offsetMax = Vector2.zero;
                    interaction.GetComponent<PointableCanvas>().InjectCanvas(root.GetComponent<Canvas>());
                }
                Selection.activeObject = PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("Intro Panel created with shared Panel Base, Primary Button, and Meta ray Canvas interaction.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("Tools/Game Flow/Validate Intro Panel Layout")]
        public static void ValidateLayout()
        {
            var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Intro Panel.prefab");
            try
            {
                var panel = root.GetComponent<IntroPanel>();
                var rect = (RectTransform)root.transform;
                panel.SetTitle("Short title");
                panel.SetBody("Short body.");
                float shortHeight = rect.rect.height;
                string paragraph = "A longer passage wraps naturally while preserving the chosen font size. ";
                panel.SetTitle("A longer title that can wrap across multiple lines of this panel");
                panel.SetBody(string.Concat(System.Linq.Enumerable.Repeat(paragraph, 20)));
                float longHeight = rect.rect.height;
                if (longHeight <= shortHeight) throw new System.Exception("Long copy did not expand the Canvas.");
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 500);
                panel.RefreshLayout();
                if (rect.rect.height <= longHeight) throw new System.Exception("Narrow width did not expand the Canvas.");
                var content = root.transform.Find("Panel Base/Content");
                var body = (RectTransform)content.Find("Body");
                var button = (RectTransform)content.Find("Primary Button");
                if (-button.anchoredPosition.y < -body.anchoredPosition.y + body.rect.height)
                    throw new System.Exception("Footer overlaps body.");
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 1000);
                panel.SetTitle("Short title");
                panel.SetBody("Short body.");
                if (Mathf.Abs(rect.rect.height - shortHeight) > 1)
                    throw new System.Exception("Canvas did not shrink back.");
                Debug.Log("PASS: Intro Panel grows for long copy, wraps at narrow widths, keeps footer below body, and shrinks back. Prefab unchanged.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static GameObject MakeButton()
        {
            const string path = "Assets/Prefabs/Primary Button.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var go = new GameObject("Primary Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            try
            {
                ((RectTransform)go.transform).sizeDelta = new Vector2(278, 72);
                var image = go.GetComponent<Image>();
                image.color = new Color(.92f, .43f, .12f);
                go.GetComponent<Outline>().effectColor = new Color(1f, .75f, .43f);
                go.GetComponent<Outline>().effectDistance = Vector2.one;
                var button = go.GetComponent<Button>();
                button.targetGraphic = image;
                var colors = button.colors;
                colors.highlightedColor = new Color(1f, .88f, .7f);
                colors.pressedColor = new Color(.7f, .65f, .55f);
                button.colors = colors;
                var label = Text(go.transform, "Label", "CONTINUE", 0, 0, 278, 72, 27, true);
                label.alignment = TextAlignmentOptions.Center;
                label.color = new Color(1f, .96f, .83f);
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static TMP_Text Text(Transform parent, string name, string value, float x, float y, float w, float h, float size, bool display)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = display ? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/RubikDirt-Regular SDF.asset") : TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.text = value;
            text.color = display ? new Color(.91f, .9f, .76f) : new Color(.66f, .65f, .54f);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.raycastTarget = false;
            Place(text.rectTransform, x, y, w, h);
            return text;
        }
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
        }
    }
}
