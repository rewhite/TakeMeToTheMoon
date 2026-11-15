using System;
using System.Reflection;
using Newtonsoft.Json;
using TakeMeToTheMoon.Story;
using UnityEditor;
using UnityEngine;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class Stage2ViskoValidation
    {
        [MenuItem("Tools/Game Flow/Validate Visko Outcome Gate")]
        public static void Validate()
        {
            ValidateStreamBoundary();
            ValidateChoicePresentation();
            var root = new GameObject("Outcome gate validation");
            root.SetActive(false);
            try
            {
                var story = root.AddComponent<StoryController>();
                story.PrepareFuelChallengePreview();
                story.BeginFuelChallenge(0);
                Require(story.CompleteFuelChallenge(), "outcome started");
                int id = story.PendingRequestId;
                Require(story.HoldOutcomeForVideo(id), "hold accepted");
                // Director retries must not invalidate the video's independent completion signal.
                story.RetryNextScene();
                var ending = JsonConvert.DeserializeObject<StoryScene>(
                    AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Story/PreviewEnding.json").text);
                Require(story.SubmitNextScene(story.PendingRequestId, ending), "next scene prepared");
                var field = typeof(StoryController).GetField("remainingSeconds", BindingFlags.Instance | BindingFlags.NonPublic);
                var tick = typeof(StoryController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                field.SetValue(story, -1f);
                tick.Invoke(story, null);
                Require(story.Phase == StoryPhase.WaitingForNextScene, "prepared scene cannot bypass video wait");
                Require(story.OutcomeVideoStarted(id, 10), "video arrival starts timer after retry");
                Require((float)field.GetValue(story) == 10, "ten second duration");
                Require(!story.OutcomeVideoStarted(id, 10), "duplicate frame cannot restart timer");
                field.SetValue(story, 0f);
                story.SetVideoPlaybackPaused(true);
                tick.Invoke(story, null);
                Require(story.Phase == StoryPhase.WaitingForNextScene, "stalled or suspended video cannot advance");
                story.SetVideoPlaybackPaused(false);
                tick.Invoke(story, null);
                Require(story.Phase == StoryPhase.Ended, "prepared scene presented after timer");
                story.ResetStory();
                Require(!story.OutcomeVideoStarted(id, 10), "stale completion after reset rejected");
                Debug.Log("VISKO OUTCOME GATE PASSED: accepted prompt/chunks/decoded frames, delayed cards, transition/stall guards, 10 seconds, retry, duplicate and reset checks.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void ValidateStreamBoundary()
        {
            var gate = new ViskoPlaybackGate();
            gate.Begin(true, 1.8f);
            gate.Message("chunk_complete", 0, 100);
            Require(!gate.TryOpen(100, 200, true), "old stream cannot open before prompt acceptance");
            gate.Message("prompt_accepted", 1, 200);
            gate.Message("chunk_complete", 2, 220);
            Require(!gate.TryOpen(100, 300, true), "in-flight old chunk cannot open morph gate");
            gate.Message("chunk_complete", 3, 240);
            Require(!gate.TryOpen(4, 250, true), "playback buffer must elapse");
            Require(!gate.TryOpen(5, 240, true), "allocated or frozen texture cannot open gate");
            Require(!gate.TryOpen(5, 250, false), "stage transition cannot open gate");
            Require(gate.TryOpen(5, 250, true), "fresh decoding after morph boundary opens gate");
            Require(!gate.TryOpen(6, 260, true), "gate only opens once");
            gate.Begin(false, 0);
            gate.Message("prompt_accepted", 6, 0);
            gate.Message("chunk_complete", 7, 0);
            Require(gate.TryOpen(7, 1, true), "initial stream needs one completed chunk and decoded frame");
            gate.Begin(true, 0);
            gate.Cancel();
            gate.Message("prompt_accepted", 8, 1);
            gate.Message("chunk_complete", 9, 2);
            gate.Message("chunk_complete", 10, 3);
            Require(!gate.TryOpen(11, 4, true), "late messages after reset cannot reopen gate");
        }
        private static void ValidateChoicePresentation()
        {
            // Inactive fixture: exercise the real presenter without starting a paid connection.
            var root = new GameObject("Visko UI validation");
            root.SetActive(false);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            void Set(object obj, string field, object value) => obj.GetType().GetField(field, flags).SetValue(obj, value);
            void Property(object obj, string name, object value) => Set(obj, "<" + name + ">k__BackingField", value);
            void Call(object obj, string name) => obj.GetType().GetMethod(name, flags).Invoke(obj, null);
            try
            {
                var story = root.AddComponent<StoryController>();
                Property(story, "Phase", StoryPhase.AwaitingChoice);
                Property(story, "Checkpoint", 1);
                Property(story, "CurrentScene", new StoryScene { choices = new[] {
                    new StoryChoice { label = "Leave" }, new StoryChoice { label = "Bring roommate" }
                }});
                var manager = root.AddComponent<GameManager>();
                Property(manager, "Phase", GamePhase.Main);
                Property(manager, "CurrentStageNumber", 1);
                var client = root.AddComponent<Visko.ViskoStreamClient>();
                Property(client, "SessionReady", true);
                Property(client, "VideoTexture", Texture2D.blackTexture);
                Property(client, "LastVideoFrameTime", Time.realtimeSinceStartup);
                var shared = root.AddComponent<SharedVideoPlayer>();
                var cards = new GameObject("Cards");
                cards.transform.SetParent(root.transform);
                var group = cards.AddComponent<CanvasGroup>();
                var buttons = new UnityEngine.UI.Button[2];
                var labels = new TMPro.TMP_Text[2];
                for (int i = 0; i < 2; i++)
                {
                    var card = new GameObject("Card", typeof(RectTransform));
                    card.transform.SetParent(cards.transform);
                    buttons[i] = card.AddComponent<UnityEngine.UI.Button>();
                    labels[i] = card.AddComponent<TMPro.TextMeshProUGUI>();
                }
                var presenter = root.AddComponent<StageViskoPresentation>();
                Set(presenter, "story", story); Set(presenter, "gameManager", manager);
                Set(presenter, "viskoClient", client); Set(presenter, "sharedVideo", shared);
                Set(presenter, "choiceRoot", cards); Set(presenter, "choiceGroup", group);
                Set(presenter, "choiceButtons", buttons); Set(presenter, "choiceLabels", labels);
                Set(presenter, "wanted", true);
                Call(presenter, "HideChoices");
                Call(presenter, "Update");
                Require(!cards.activeSelf, "stage entry and an allocated texture cannot reveal cards");
                Set(presenter, "shotVisible", true);
                Set(presenter, "choicesDelaySeconds", 100f);
                Call(presenter, "Update");
                Require(!cards.activeSelf, "visible video still waits for card delay");
                Set(presenter, "choicesElapsed", 101f);
                Set(presenter, "choicesFadeSeconds", 0f);
                Call(presenter, "Update");
                Require(cards.activeSelf && group.interactable && labels[0].text == "Leave", "cards reveal after delay with current labels");
                Property(client, "LastVideoFrameTime", float.NegativeInfinity);
                Call(presenter, "Update");
                Require(!cards.activeSelf && !group.blocksRaycasts, "stalled video hides and disables input");
                Property(client, "LastVideoFrameTime", Time.realtimeSinceStartup);
                Set(manager, "transitioning", true);
                Call(presenter, "Update");
                Require(!cards.activeSelf, "transition keeps cards hidden");
                Set(manager, "transitioning", false);
                Set(presenter, "outcomePlayback", true);
                Call(presenter, "Update");
                Require(!cards.activeSelf, "outcome never reopens decision cards");
                UnityEngine.Object.DestroyImmediate(cards);
                Call(presenter, "OnDisable"); // Independent UI can be destroyed before its presenter.
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void Require(bool value, string label)
        { if (!value) throw new Exception(label); }
    }
}
