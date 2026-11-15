using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace TakeMeToTheMoon.Story.Editor
{
    [CustomEditor(typeof(StoryController))]
    public sealed class StoryControllerEditor : UnityEditor.Editor
    {
        private TextAsset nextSceneJson;

        [MenuItem("GameObject/Story/Story Controller", false, 10)]
        private static void Create(MenuCommand command)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Story Controller.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(go, "Create Story Controller");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Selection.activeGameObject = go;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Story Preview (Play Mode)", EditorStyles.boldLabel);
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to select items and preview the fixed opening. AI and video are connected through the events above.", MessageType.Info);
                return;
            }

            var controller = (StoryController)target;
            EditorGUILayout.LabelField("Phase", controller.Phase.ToString());
            EditorGUILayout.LabelField("Distance / Wellness", $"{controller.DistanceRemaining:N0} mi / {controller.Wellness}%");
            EditorGUILayout.LabelField("Decisions", controller.Decisions.ToString());
            EditorGUILayout.LabelField("Inventory", $"{controller.Inventory.Count} / {controller.RequiredItemCount}");
            if (!string.IsNullOrEmpty(controller.LastError))
                EditorGUILayout.HelpBox(controller.LastError, MessageType.Error);

            using (new EditorGUI.DisabledScope(controller.Phase != StoryPhase.SelectingItems))
            {
                foreach (StoryItemDefinition item in controller.AvailableItems)
                {
                    if (item == null) continue;
                    bool selected = controller.HasItem(item);
                    bool next = EditorGUILayout.ToggleLeft(item.DisplayName, selected);
                    if (next != selected)
                    {
                        if (next) controller.SelectItem(item);
                        else controller.DeselectItem(item);
                    }
                }
            }
            using (new EditorGUI.DisabledScope(!controller.CanStart))
                if (GUILayout.Button("Start Story"))
                {
                    var sockets = controller.GetComponent<StorySocketSelection>();
                    if (sockets != null && sockets.isActiveAndEnabled) sockets.StartStory();
                    else controller.StartStory();
                }

            if (controller.CurrentScene != null)
                EditorGUILayout.HelpBox(controller.CurrentScene.narration, MessageType.None);
            if (controller.Phase == StoryPhase.Opening && GUILayout.Button("Skip Opening Wait"))
                controller.AdvanceOpening();
            if (controller.Phase == StoryPhase.AwaitingChoice)
            {
                for (int i = 0; i < controller.CurrentScene.choices.Length; i++)
                    using (new EditorGUI.DisabledScope(!controller.CanChoose(i)))
                        if (GUILayout.Button(controller.CurrentScene.choices[i].label)) controller.Choose(i);
            }
            if (controller.Phase == StoryPhase.WaitingForNextScene)
            {
                EditorGUILayout.HelpBox(controller.LastChoice.outcome_narration, MessageType.None);
                EditorGUILayout.HelpBox("Waiting for a Director adapter. For a local preview, assign a JSON TextAsset containing one Scene and submit it below. No API calls are made by this controller.", MessageType.Info);
                nextSceneJson = (TextAsset)EditorGUILayout.ObjectField("Next Scene JSON", nextSceneJson, typeof(TextAsset), false);
                using (new EditorGUI.DisabledScope(nextSceneJson == null))
                    if (GUILayout.Button("Submit Preview Scene"))
                    {
                        try
                        {
                            controller.SubmitNextScene(controller.PendingRequestId,
                                JsonConvert.DeserializeObject<StoryScene>(nextSceneJson.text));
                        }
                        catch (JsonException e) { controller.ReportDirectorError(controller.PendingRequestId, e.Message); }
                    }
                if (GUILayout.Button("Retry Next Scene Request")) controller.RetryNextScene();
            }
            if (GUILayout.Button("Copy Director Input JSON"))
                EditorGUIUtility.systemCopyBuffer = JsonConvert.SerializeObject(controller.GetDirectorInput(), Formatting.Indented);
            if (GUILayout.Button("Reset Story")) controller.ResetStory();
            Repaint();
        }
    }
}
