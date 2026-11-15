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
                tick.Invoke(story, null);
                Require(story.Phase == StoryPhase.Ended, "prepared scene presented after timer");
                story.ResetStory();
                Require(!story.OutcomeVideoStarted(id, 10), "stale completion after reset rejected");
                Debug.Log("VISKO OUTCOME GATE PASSED: waits for video, 10 seconds, retry, duplicate and reset checks.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void Require(bool value, string label)
        { if (!value) throw new Exception(label); }
    }
}
