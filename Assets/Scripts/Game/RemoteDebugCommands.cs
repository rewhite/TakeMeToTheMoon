using TakeMeToTheMoon.Story;
using UnityEngine;

namespace TakeMeToTheMoon.Game
{
    // Android receiver queues only strings; all Unity work runs here on the main thread.
    public sealed class RemoteDebugCommands
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaClass bridge;
        private float nextPoll;
        private bool failed;
#endif
        public void Tick(GameManager manager, bool enabled)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!enabled) { Disable(); return; }
            if (failed || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + .2f;
            try
            {
                if (bridge == null)
                {
                    bridge = new AndroidJavaClass("com.takemetothemoon.debug.MoonDebugReceiver");
                    bridge.CallStatic("setReady", true);
                    Debug.Log("[MoonDebug] Ready: reset, prologue, stage1, stage3, ending-success, realign, skip, status");
                }
                // Hold commands during fades so skip/restart never races stage setup.
                if (manager.IsTransitioning) return;
                string command = bridge.CallStatic<string>("poll");
                if (!string.IsNullOrEmpty(command))
                {
                    try { Execute(manager, command); }
                    catch (System.Exception e) { Debug.LogError("[MoonDebug] Command failed: " + command + ": " + e.Message); }
                }
            }
            catch (System.Exception e)
            {
                failed = true;
                Debug.LogError("[MoonDebug] " + e.Message);
            }
#endif
        }
        public void Disable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (bridge != null)
            {
                bridge.CallStatic("setReady", false);
                bridge.Dispose(); bridge = null;
            }
            failed = false;
#endif
        }
        public static void Execute(GameManager manager, string command)
        {
            var story = Object.FindAnyObjectByType<StoryController>(FindObjectsInactive.Include);
            if (command == "status")
            {
                Debug.Log($"[MoonDebug] phase={manager.Phase}, stage={manager.CurrentStageNumber}, story={story?.Phase}, wellness={story?.Wellness}, items={story?.Inventory.Count}");
                return;
            }
            if (command == "realign")
            {
                manager.RealignForDebug();
                return;
            }
            if (command == "skip")
            {
                manager.SkipVideoForDebug();
                Debug.Log("[MoonDebug] skip completed");
                return;
            }
            if (command != "reset" && command != "prologue" && command != "stage1" && command != "stage3" && command != "ending-success")
                throw new System.ArgumentException("Unknown remote command");
            if (story == null || !story.isActiveAndEnabled)
                throw new System.InvalidOperationException("Active StoryController required for reset");
            // Preserve the Visko session already preloaded during the prologue.
            if (command == "stage1" && manager.Phase == GamePhase.Prologue && story.Phase == StoryPhase.SelectingItems)
            {
                StartStage1Preview(manager, story);
                return;
            }
            manager.ResetPresentationForDebug();
            foreach (var item in Object.FindObjectsByType<StoryItemDisplayMotion>(FindObjectsInactive.Include))
                item.ResetToHome();
            foreach (var tray in Object.FindObjectsByType<StoryInventoryTray>(FindObjectsInactive.Include))
                if (tray.Slots != null) for (int i = 0; i < tray.Slots.Length; i++) tray.ClearSlot(i);
            story.ResetStory(); // Invalidates outstanding Director responses as well.
            if (command == "ending-success")
            {
                var presenter = Object.FindAnyObjectByType<StageViskoPresentation>();
                if (presenter == null) throw new System.InvalidOperationException("Active StageViskoPresentation required");
                story.PrepareSuccessfulEndingPreview();
                presenter.PlayEndingVideoPreview();
                Debug.Log($"[MoonDebug] Success ending preview: wellness={story.Wellness}, throws={story.FuelHits}/3, type={story.GetVerdict().archetype}");
                return;
            }
            if (command == "prologue") manager.ShowPrologue();
            if (command == "stage1")
            {
                StartStage1Preview(manager, story);
                return;
            }
            if (command == "stage3")
            {
                story.PrepareFuelChallengePreview();
                manager.ShowStage(3);
            }
            Debug.Log("[MoonDebug] " + command + " applied" + ((command == "stage1" || command == "stage3") ? " with test inventory" : ""));
        }

        private static void StartStage1Preview(GameManager manager, StoryController story)
        {
            foreach (var item in story.AvailableItems)
            {
                if (story.CanStart) break;
                story.TrySelectItem(item);
            }
            if (!story.TryStartStory()) throw new System.InvalidOperationException("Could not prepare stage 1 test inventory");
            manager.ShowStage(1);
            Debug.Log("[MoonDebug] stage1 applied with test inventory");
        }
    }
}
