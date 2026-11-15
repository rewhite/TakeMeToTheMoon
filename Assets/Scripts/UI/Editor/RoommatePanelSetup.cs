using System.Linq;
using TMPro;
using TakeMeToTheMoon.Game;
using TakeMeToTheMoon.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class RoommatePanelSetup
    {
        [MenuItem("Tools/Game Flow/Configure Stage 1 Choice Panel")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var manager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            var controller = Object.FindAnyObjectByType<StoryController>(FindObjectsInactive.Include);
            var settings = new SerializedObject(manager);
            var stage = settings.FindProperty("stages").GetArrayElementAtIndex(0);
            var stageRoot = stage.FindPropertyRelative("screenPrefab").objectReferenceValue as GameObject;
            if (stageRoot == null || !stageRoot.scene.IsValid()) throw new System.InvalidOperationException("Assign a preplaced Stage 1 screen root.");
            var existing = stageRoot.GetComponentInChildren<RoommateChoicePanel>(true);
            GameObject root;
            if (existing != null) root = existing.gameObject;
            else
            {
                root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Choice Cards.prefab"), stageRoot.transform);
                Undo.RegisterCreatedObjectUndo(root, "Add Stage 1 choice panel");
                root.name = "Stage 1 Choice Panel";
                var eye = manager.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).First(c => c.name == "CenterEyeAnchor");
                var rect = (RectTransform)root.transform;
                rect.SetPositionAndRotation(eye.transform.position + eye.transform.forward * 1.6f - Vector3.up * .05f, Quaternion.Euler(0, eye.transform.eulerAngles.y, 0));
                rect.localScale = Vector3.one * .0015f;
                rect.sizeDelta = new Vector2(920, 620);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
                var canvas = root.GetComponent<Canvas>();
                canvas.worldCamera = eye;
                PrefabUtility.RecordPrefabInstancePropertyModifications(canvas);
                var buttons = root.GetComponentsInChildren<Button>(true).OrderBy(b => b.transform.localPosition.x).ToArray();
                for (int i = 0; i < 2; i++)
                {
                    var br = (RectTransform)buttons[i].transform;
                    br.anchoredPosition = new Vector2(i == 0 ? -240 : 240, -110);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(br);
                    var label = buttons[i].GetComponentInChildren<TMP_Text>();
                    label.text = i == 0 ? "I'll wake my roommate\nup." : "Leave";
                    PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                }
                var header = new GameObject("Situation", typeof(RectTransform), typeof(Image));
                header.transform.SetParent(root.transform, false);
                var hr = (RectTransform)header.transform;
                hr.sizeDelta = new Vector2(840, 176);
                hr.anchoredPosition = new Vector2(0, 205);
                var background = header.GetComponent<Image>();
                background.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Images/Canvas Panel.png");
                background.type = Image.Type.Tiled;
                background.pixelsPerUnitMultiplier = 3;
                background.raycastTarget = false;
                var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                textObject.transform.SetParent(header.transform, false);
                var text = textObject.GetComponent<TextMeshProUGUI>();
                text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = new Vector2(35, 20); text.rectTransform.offsetMax = new Vector2(-35, -20);
                text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/RubikDirt-Regular SDF.asset");
                text.text = "We have to escape from the dorm\nand head to the Moon right away!";
                text.fontSize = 34;
                text.color = new Color(.91f, .9f, .8f);
                text.alignment = TextAlignmentOptions.Center;
                text.raycastTarget = false;
                existing = Undo.AddComponent<RoommateChoicePanel>(root);
                var binding = new SerializedObject(existing);
                binding.FindProperty("controller").objectReferenceValue = controller;
                binding.FindProperty("wakeButton").objectReferenceValue = buttons[0];
                binding.FindProperty("leaveButton").objectReferenceValue = buttons[1];
                binding.ApplyModifiedProperties();
            }
            // Only this UI is delayed; the persistent video remains outside Stage 1.
            stage.FindPropertyRelative("revealWhen").enumValueIndex = (int)RevealCondition.VideoEnded;
            var targets = stage.FindPropertyRelative("revealObjects");
            if (!Enumerable.Range(0, targets.arraySize).Any(i => targets.GetArrayElementAtIndex(i).objectReferenceValue == root))
            {
                int i = targets.arraySize++;
                targets.GetArrayElementAtIndex(i).objectReferenceValue = root;
            }
            settings.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(stageRoot.scene);
            Selection.activeGameObject = root;
            Debug.Log("Stage 1 choice panel configured: preplaced header + Green/Blue cards, Meta ray input, VideoEnded reveal, StoryController roommate choices.");
        }
    }
}
