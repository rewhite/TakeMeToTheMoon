using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class StepPanelMenu
    {
        private const string Path = "Assets/Prefabs/Step Panel.prefab";
        [MenuItem("Tools/Game Flow/Create Step Panel Prefab")]
        public static void CreatePrefab()
        {
            PanelBaseMenu.CreatePrefab();
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (existing != null) { Selection.activeObject = existing; return; }
            var root = new GameObject("Step Panel", typeof(RectTransform), typeof(Canvas), typeof(StepPanel));
            try
            {
                var rect = (RectTransform)root.transform;
                rect.sizeDelta = new Vector2(1100, 150);
                rect.localPosition = new Vector3(0, 0, 2);
                rect.localScale = Vector3.one * .0015f;
                root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var panel = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Panel Base.prefab"), root.transform);
                Stretch((RectTransform)panel.transform, 0);
                var content = panel.transform.Find("Content");
                var badge = new GameObject("Step Badge", typeof(RectTransform), typeof(Image));
                badge.transform.SetParent(content, false);
                var image = badge.GetComponent<Image>();
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Images/Canvas Panel.png");
                image.type = Image.Type.Sliced;
                image.color = new Color(.8f, .81f, .57f, 1);
                image.raycastTarget = false;
                var step = Text(badge.transform, "Label", 16, true);
                Stretch(step.rectTransform, 6);
                step.rectTransform.offsetMin = new Vector2(12, 6);
                step.rectTransform.offsetMax = new Vector2(-12, -6);
                var progress = Text(content, "Progress", 16, false);
                var instruction = Text(content, "Instruction", 34, true);
                var so = new SerializedObject(root.GetComponent<StepPanel>());
                so.FindProperty("badge").objectReferenceValue = badge.transform;
                so.FindProperty("stepText").objectReferenceValue = step;
                so.FindProperty("progressText").objectReferenceValue = progress;
                so.FindProperty("instructionText").objectReferenceValue = instruction;
                so.ApplyModifiedPropertiesWithoutUndo();
                root.GetComponent<StepPanel>().RefreshLayout();
                Selection.activeObject = PrefabUtility.SaveAsPrefabAsset(root, Path);
                Debug.Log("Step Panel created: editable step, progress and instruction, automatic height, shared background.");
            }
            finally { Object.DestroyImmediate(root); }
        }
        [MenuItem("Tools/Game Flow/Validate Step Panel Layout")]
        public static void ValidateLayout()
        {
            var root = PrefabUtility.LoadPrefabContents(Path);
            try
            {
                var panel = root.GetComponent<StepPanel>();
                var rect = (RectTransform)root.transform;
                panel.SetInstruction("Short instruction");
                float shortHeight = rect.rect.height;
                panel.SetInstruction(string.Concat(System.Linq.Enumerable.Repeat("Pick something to take before you leave. ", 20)));
                if (rect.rect.height <= shortHeight) throw new System.Exception("Long instruction did not expand the panel.");
                panel.SetInstruction("Short instruction");
                if (Mathf.Abs(rect.rect.height - shortHeight) > 1) throw new System.Exception("Panel did not shrink.");
                panel.SetStepLabel(""); panel.SetProgressLabel("");
                if (root.transform.Find("Panel Base/Content/Step Badge").gameObject.activeSelf) throw new System.Exception("Empty badge remains visible.");
                Debug.Log("PASS: Step Panel grows, shrinks and hides empty badge. Prefab unchanged.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static TMP_Text Text(Transform parent, string name, float size, bool display)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = display ? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/RubikDirt-Regular SDF.asset") : TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = display ? new Color(.91f, .9f, .76f) : new Color(.66f, .65f, .54f);
            text.raycastTarget = false;
            return text;
        }
        private static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * padding; rect.offsetMax = -Vector2.one * padding;
        }
    }
}
