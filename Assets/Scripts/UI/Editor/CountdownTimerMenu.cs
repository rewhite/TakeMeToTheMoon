using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class CountdownTimerMenu
    {
        private const string Path = "Assets/Prefabs/Countdown Timer.prefab";
        [MenuItem("Tools/Game Flow/Create Countdown Timer Prefab")]
        public static void CreatePrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Path) != null) return;
            var root = new GameObject("Countdown Timer", typeof(RectTransform), typeof(Canvas), typeof(CountdownTimer));
            try
            {
                var rect = (RectTransform)root.transform;
                rect.sizeDelta = new Vector2(104, 104);
                rect.localPosition = new Vector3(0, 1.6f, 2);
                rect.localScale = Vector3.one * .002f;
                root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                // A thick ring forms the opaque disc, keeping the entire badge code-native.
                AddRing(root.transform, "Disc", 104, new Color(.09f, .04f, .055f), 52);
                AddRing(root.transform, "Track", 90, new Color(.24f, .075f, .09f), 6);
                var ring = AddRing(root.transform, "Progress", 90, new Color(1f, .32f, .35f), 6);
                var ringSettings = new SerializedObject(ring);
                ringSettings.FindProperty("gradientEnabled").boolValue = true;
                ringSettings.ApplyModifiedPropertiesWithoutUndo();
                var number = AddText(root.transform, "Seconds", "15", 9, 34);
                AddText(root.transform, "Unit", "SEC", -20, 10);
                var so = new SerializedObject(root.GetComponent<CountdownTimer>());
                so.FindProperty("secondsText").objectReferenceValue = number;
                so.FindProperty("ring").objectReferenceValue = ring;
                so.ApplyModifiedPropertiesWithoutUndo();
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, Path);
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                Debug.Log("Countdown Timer prefab created: 15 seconds, Completed event, screen stays visible.");
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static TimerRing AddRing(Transform parent, string name, float size, Color color, float thickness)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TimerRing));
            go.transform.SetParent(parent, false);
            var ring = go.GetComponent<TimerRing>();
            ring.rectTransform.sizeDelta = Vector2.one * size;
            ring.color = color;
            ring.raycastTarget = false;
            var so = new SerializedObject(ring);
            so.FindProperty("thickness").floatValue = thickness;
            so.ApplyModifiedPropertiesWithoutUndo();
            return ring;
        }
        private static TMP_Text AddText(Transform parent, string name, string text, float y, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/EastSeaDokdo-Regular SDF.asset");
            label.text = text;
            label.fontSize = size;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(90, 45);
            label.rectTransform.anchoredPosition = new Vector2(0, y);
            return label;
        }
    }
}
