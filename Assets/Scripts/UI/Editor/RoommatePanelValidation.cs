using System;
using System.Linq;
using TakeMeToTheMoon.Game;
using TakeMeToTheMoon.Story;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class RoommatePanelValidation
    {
        [MenuItem("Tools/Game Flow/Validate Stage 1 Choices (Play Mode)")]
        public static void Validate()
        {
            if (!Application.isPlaying) throw new Exception("Enter Play Mode first.");
            var source = UnityEngine.Object.FindAnyObjectByType<RoommateChoicePanel>(FindObjectsInactive.Include);
            var live = UnityEngine.Object.FindAnyObjectByType<StoryController>();
            var root = new GameObject("Roommate choice test");
            root.SetActive(false);
            try
            {
                var controller = root.AddComponent<StoryController>();
                var data = new SerializedObject(controller);
                var items = data.FindProperty("availableItems");
                items.arraySize = live.AvailableItems.Count;
                for (int i = 0; i < items.arraySize; i++) items.GetArrayElementAtIndex(i).objectReferenceValue = live.AvailableItems[i];
                data.FindProperty("openingScenes").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Story/OpeningScenes.json");
                data.ApplyModifiedPropertiesWithoutUndo();
                var clone = UnityEngine.Object.Instantiate(source.gameObject, root.transform);
                clone.SetActive(false);
                var panel = clone.GetComponent<RoommateChoicePanel>();
                var binding = new SerializedObject(panel);
                binding.FindProperty("controller").objectReferenceValue = controller;
                binding.ApplyModifiedPropertiesWithoutUndo();
                root.SetActive(true);
                for (int choice = 0; choice < 2; choice++)
                {
                    controller.ResetStory();
                    foreach (var item in controller.AvailableItems.Take(3)) controller.TrySelectItem(item);
                    if (!controller.TryStartStory()) throw new Exception("Test story start failed.");
                    clone.SetActive(true);
                    if (controller.Phase != StoryPhase.AwaitingChoice) throw new Exception("Reveal did not expose roommate choices.");
                    var button = (Button)new SerializedObject(panel).FindProperty(choice == 0 ? "wakeButton" : "leaveButton").objectReferenceValue;
                    if (!button.interactable) throw new Exception("Choice button disabled.");
                    button.onClick.Invoke();
                    if (clone.activeSelf || controller.Phase != StoryPhase.WaitingForNextScene ||
                        controller.Roommate != (choice == 0 ? "came along" : "left behind, still asleep") ||
                        controller.Wellness != (choice == 0 ? 98 : 100)) throw new Exception("Wrong roommate result.");
                    button.onClick.Invoke();
                    if (controller.Wellness != (choice == 0 ? 98 : 100)) throw new Exception("Duplicate choice applied.");
                }
                Debug.Log("STAGE 1 CHOICES PASSED: reveal advances opening, both buttons update StoryController, correct wellness/roommate, panel hides, duplicate click ignored.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            // Preview real authored layout with normal video ownership; runtime edits vanish on exit.
            var manager = UnityEngine.Object.FindAnyObjectByType<GameManager>();
            var managerData = new SerializedObject(manager);
            managerData.FindProperty("transitionDuration").floatValue = 0;
            managerData.ApplyModifiedPropertiesWithoutUndo();
            manager.ShowStage(1);
            if (source.gameObject.activeInHierarchy) throw new Exception("Panel revealed before video-ended/manual preview.");
            manager.RevealCurrentStage();
            if (!source.gameObject.activeInHierarchy) throw new Exception("Stage reveal did not show panel.");
            Debug.Log("STAGE 1 REVEAL PASSED: panel hidden on entry, visible on reveal; layout ready for preview. Exit Play Mode to restore.");
        }
    }
}
