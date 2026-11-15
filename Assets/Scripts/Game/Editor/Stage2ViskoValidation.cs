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
        [MenuItem("Tools/Game Flow/Validate Stage 1 Visko Live (Play Mode)")]
        public static void ValidateLive() => ValidateLive(false);

        [MenuItem("Tools/Game Flow/Validate Stage 1 Visko with Mock Director (Play Mode)")]
        public static void ValidateLiveWithMockDirector() => ValidateLive(true);

        private static void ValidateLive(bool mockDirector)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first. This test uses the configured Visko and Director services.");
            var manager = UnityEngine.Object.FindAnyObjectByType<GameManager>();
            var presenter = UnityEngine.Object.FindAnyObjectByType<StageViskoPresentation>();
            var story = UnityEngine.Object.FindAnyObjectByType<StoryController>();
            var director = UnityEngine.Object.FindAnyObjectByType<DirectorClient>();
            var client = UnityEngine.Object.FindAnyObjectByType<Visko.ViskoStreamClient>();
            bool previousMock = director.MockMode;
            if (mockDirector) director.MockMode = true;
            string mode = mockDirector ? "actual Visko + mock Director" : "actual Visko + actual Director";
            var so = new SerializedObject(presenter);
            var cards = (GameObject)so.FindProperty("choiceRoot").objectReferenceValue;
            var button = (UnityEngine.UI.Button)so.FindProperty("choiceButtons").GetArrayElementAtIndex(0).objectReferenceValue;
            double start = EditorApplication.timeSinceStartup, pickedAt = -1;
            bool previousVisible = false;
            bool enteredStage1 = false;
            int connectionAttempts = -1;
            Texture preloadedTexture = null;
            EditorApplication.CallbackFunction poll = null;
            poll = () =>
            {
                if (!Application.isPlaying || manager == null)
                {
                    EditorApplication.update -= poll;
                    if (director != null) director.MockMode = previousMock;
                    return;
                }
                double elapsed = EditorApplication.timeSinceStartup - start;
                if (elapsed > 180)
                {
                    EditorApplication.update -= poll;
                    director.MockMode = previousMock;
                    RemoteDebugCommands.Execute(manager, "reset");
                    Debug.LogError("VISKO LIVE TEST timed out; session stopped. Inspect connection/Director status.");
                    return;
                }
                if (!enteredStage1 && manager.Phase == GamePhase.Prologue && !manager.IsTransitioning && presenter.Stage1VideoPrepared)
                {
                    if (story.Phase != StoryPhase.SelectingItems || cards.activeInHierarchy)
                    {
                        EditorApplication.update -= poll;
                        director.MockMode = previousMock;
                        RemoteDebugCommands.Execute(manager, "reset");
                        Debug.LogError("VISKO LIVE TEST FAILED: preload changed story state or revealed choices.");
                        return;
                    }
                    connectionAttempts = client.ConnectionAttemptCount;
                    preloadedTexture = client.VideoTexture;
                    enteredStage1 = true;
                    GameManagerEditor.PreviewNextStage(manager);
                    Debug.Log("[ViskoLiveTest] Entered Stage 1 through Inspector with preloaded video.");
                }
                bool visible = cards.activeInHierarchy && cards.GetComponent<CanvasGroup>().interactable;
                if (visible && !previousVisible) Debug.Log($"[ViskoLiveTest] Cards interactive at +{elapsed:0.00}s; story={story.Phase}");
                previousVisible = visible;
                if (pickedAt < 0 && visible && story.Phase == StoryPhase.AwaitingChoice && button.interactable)
                {
                    pickedAt = EditorApplication.timeSinceStartup;
                    button.onClick.Invoke();
                    Debug.Log($"[ViskoLiveTest] Selected visible first card at +{elapsed:0.00}s.");
                }
                if (pickedAt >= 0 && story.Checkpoint == 1 && story.CanContinueNextScene && visible)
                {
                    so.Update();
                    var next = (UnityEngine.UI.Button)so.FindProperty("continueButton").objectReferenceValue;
                    if (next != null && next.gameObject.activeInHierarchy && next.interactable) next.onClick.Invoke();
                }
                if (manager.CurrentStageNumber != 2 || manager.IsTransitioning) return;
                EditorApplication.update -= poll;
                director.MockMode = previousMock;
                bool passed = pickedAt >= 0 && EditorApplication.timeSinceStartup - pickedAt >= 10 && !cards.activeInHierarchy &&
                    presenter.ActiveClient != client && presenter.ActiveClient.SessionReady &&
                    presenter.ActiveClient.VideoTexture != null && presenter.ActiveClient.VideoTexture != preloadedTexture &&
                    client.ConnectionAttemptCount == connectionAttempts;
                if (passed) Debug.Log($"VISKO LIVE TEST PASSED ({mode}): prologue preload, prepared second stream adopted on Stage 2, delayed cards and 10-second choice delay at +{elapsed:0.00}s.");
                else Debug.LogError("VISKO LIVE TEST FAILED: early progression or visible cards on stage 2.");
                RemoteDebugCommands.Execute(manager, "reset"); // End this test's paid session.
            };
            RemoteDebugCommands.Execute(manager, "reset");
            manager.ShowPrologue();
            EditorApplication.update += poll;
            Debug.Log($"[ViskoLiveTest] Starting Prologue -> Inspector Next Stage ({mode}); will choose the first visible card, then verify Stage 2.");
        }

        [MenuItem("Tools/Game Flow/Validate Visko Outcome Gate")]
        public static void Validate()
        {
            ValidateStoryPreload();
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
                story.SetNextVideoPending(story.PendingRequestId, true);
                tick.Invoke(story, null);
                Require(story.Phase == StoryPhase.WaitingForNextScene, "elapsed choice delay still waits for prepared video");
                story.SetNextVideoPending(id, false);
                tick.Invoke(story, null);
                Require(story.Phase == StoryPhase.WaitingForNextScene, "stale request cannot release next video gate");
                story.SetNextVideoPending(story.PendingRequestId, false);
                story.RequireNextSceneContinue();
                tick.Invoke(story, null);
                Require(story.Phase == StoryPhase.WaitingForNextScene && story.CanContinueNextScene,
                    "elapsed delay and ready video cannot bypass Continue");
                Require(story.ContinueNextScene(), "Continue accepted when ready");
                Require(!story.ContinueNextScene(), "duplicate Continue rejected");
                tick.Invoke(story, null);
                Require(story.Phase == StoryPhase.Ended, "prepared scene presented after timer");
                story.ResetStory();
                Require(!story.OutcomeVideoStarted(id, 10), "stale completion after reset rejected");
                Debug.Log("VISKO OUTCOME GATE PASSED: accepted prompt/chunks/decoded frames, delayed cards, transition/stall guards, 10 seconds, retry, duplicate and reset checks.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void ValidateStoryPreload()
        {
            var root = new GameObject("Story preload validation");
            root.SetActive(false);
            try
            {
                var story = root.AddComponent<StoryController>();
                var data = new SerializedObject(story);
                data.FindProperty("openingScenes").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Story/OpeningScenes.json");
                data.ApplyModifiedPropertiesWithoutUndo();
                root.SetActive(true);
                story.ResetStory();
                int scenes = 0, checkpoints = 0;
                story.ScenePresented.AddListener(_ => scenes++);
                story.CheckpointEntered.AddListener(_ => checkpoints++);
                var preview = story.GetStage1PreviewScene();
                Require(preview != null && preview.choices.Length == 2, "authored roommate preview available");
                Require(story.Phase == StoryPhase.SelectingItems && story.Checkpoint == 0 &&
                    story.Inventory.Count == 0 && scenes == 0 && checkpoints == 0, "preview does not start the story");
                Require(story.TryStartStory(true), "normal start accepts preview data");
                Require(story.Phase == StoryPhase.AwaitingChoice && story.CurrentScene.video_prompt == preview.video_prompt &&
                    scenes == 1 && checkpoints == 1, "first scene is exactly the preloaded dorm prompt");
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
                // A ready preload must stay invisible and must not consume the choice delay.
                Set(presenter, "prewarming", true);
                Set(presenter, "prepared", true);
                Property(manager, "Phase", GamePhase.Prologue);
                Call(presenter, "Update");
                Require(!cards.activeSelf && !shared.ShowingExternal && presenter.Stage1VideoPrepared,
                    "preload cannot replace prologue video or reveal choices");
                Require((float)typeof(StageViskoPresentation).GetField("choicesElapsed", flags).GetValue(presenter) == 0,
                    "preload does not consume visible choice delay");
                // Adopt the identical in-flight/prepared prompt without calling Play or resetting its gate.
                var scene = story.CurrentScene;
                scene.video_prompt = "Roommate preview";
                scene.audio_prompt = "Roommate audio";
                Set(presenter, "prompt", scene.video_prompt);
                Set(presenter, "audioPrompt", scene.audio_prompt);
                Set(presenter, "playbackVersion", 123);
                typeof(StageViskoPresentation).GetMethod("Scene", flags).Invoke(presenter, new object[] { scene });
                Require((int)typeof(StageViskoPresentation).GetField("playbackVersion", flags).GetValue(presenter) == 123 &&
                    (bool)typeof(StageViskoPresentation).GetField("prepared", flags).GetValue(presenter),
                    "stage entry reuses preload without resending prompt");
                Property(manager, "Phase", GamePhase.Main);
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
                Property(manager, "CurrentStageNumber", 2);
                Property(story, "Checkpoint", 2);
                Set(presenter, "playbackStage", 2);
                Call(presenter, "GameState");
                Require(client.SessionReady && (bool)typeof(StageViskoPresentation).GetField("wanted", flags).GetValue(presenter),
                    "Stage 2 keeps generation active on the same connection");
                Set(presenter, "outcomePlayback", false);
                Set(presenter, "choicesElapsed", 101f);
                Call(presenter, "Update");
                Require(cards.activeSelf && group.interactable, "Stage 2 reveals choices after visible playback");
                Property(manager, "CurrentStageNumber", 6);
                Call(presenter, "GameState");
                Require(client.SessionReady && !(bool)typeof(StageViskoPresentation).GetField("wanted", flags).GetValue(presenter),
                    "unsupported presentation parks without disconnecting");
                UnityEngine.Object.DestroyImmediate(cards);
                Call(presenter, "OnDisable"); // Independent UI can be destroyed before its presenter.
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void Require(bool value, string label)
        { if (!value) throw new Exception(label); }
    }
}
