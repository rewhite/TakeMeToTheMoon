using System;
using System.Collections;
using System.Linq;
using TakeMeToTheMoon.Story;
using UnityEditor;
using UnityEngine;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class FuelThrowValidation
    {
        private static IEnumerator run;
        [MenuItem("Tools/Game Flow/Validate Stage 3 Fuel State")]
        public static void State()
        {
            var root = new GameObject("Fuel state validation"); root.SetActive(false);
            try
            {
                var story = root.AddComponent<StoryController>(); story.PrepareFuelChallengePreview();
                Require(story.BeginFuelChallenge(), "begin at checkpoint 3");
                Require(!story.CanChoose(0), "ordinary choices locked");
                Require(story.TryBeginFuelThrow(), "first throw");
                Require(!story.TryBeginFuelThrow(), "only one in flight");
                Require(story.ResolveFuelThrow(false, 5, 5) && story.Wellness == 95, "miss penalty");
                Require(!story.ResolveFuelThrow(false, 5, 5), "duplicate result rejected");
                Require(!story.CompleteFuelChallenge(), "early continue rejected");
                Require(story.TryBeginFuelThrow() && story.ResolveFuelThrow(true, 5, 5) && story.Wellness == 100, "hit reward");
                Require(story.TryBeginFuelThrow() && story.ResolveFuelThrow(true, 5, 5) && story.Wellness == 100, "wellness capped");
                Require(!story.TryBeginFuelThrow(), "fourth throw rejected");
                int requests = 0; story.NextSceneRequested.AddListener(_ => requests++);
                Require(story.CompleteFuelChallenge() && requests == 1, "one progression request");
                Require(story.FuelScoreChange == 0 && story.History.Last().wellness_change == 0, "summary uses actual net change, not capped rewards");
                Require(story.GetDirectorInput().checkpoint_number == 4, "next checkpoint is orbit");
                Require(!story.CompleteFuelChallenge() && requests == 1, "duplicate continue rejected");
                story.ResetStory(); Require(!story.FuelChallengeActive && story.Wellness == 100, "reset clears game");
                Debug.Log("FUEL STATE PASSED: three attempts, one in flight, hit/miss scores, clamp, duplicate rejection, checkpoint 4 progression, reset.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [MenuItem("Tools/Game Flow/Preview and Validate Stage 3 Fuel (Play Mode)")]
        public static void Preview()
        {
            if (!Application.isPlaying) throw new Exception("Enter Play Mode first");
            if (run != null) throw new Exception("Validation already running");
            run = Check(); EditorApplication.update += Tick;
        }
        private static void Tick()
        {
            try { if (Application.isPlaying && run.MoveNext()) return; }
            catch (Exception e) { Debug.LogException(e); }
            (run as IDisposable)?.Dispose(); run = null; EditorApplication.update -= Tick;
        }
        private static IEnumerator Check()
        {
            var manager = UnityEngine.Object.FindAnyObjectByType<GameManager>();
            var data = new SerializedObject(manager); data.FindProperty("transitionDuration").floatValue = 0; data.ApplyModifiedPropertiesWithoutUndo();
            var game = UnityEngine.Object.FindAnyObjectByType<FuelThrowGame>(FindObjectsInactive.Include);
            manager.ShowStage(3);
            Require(!game.gameObject.activeInHierarchy, "hidden during stage video");
            manager.SkipVideoForDebug();
            Require(game.gameObject.activeInHierarchy && game.story.FuelChallengeActive, "video end reveals game");
            yield return null; yield return null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var item = game.items.First(i => i.gameObject.activeInHierarchy);
                var flick = item.AssistedVelocity(Vector3.forward * .2f);
                Require(flick.z >= 3.8f && flick.y >= 1f, "short flick gets range and lift");
                Require(item.AssistedVelocity(Vector3.down).y < 0, "downward aim preserved");
                bool hit = attempt != 1;
                var target = game.targets[attempt == 0 ? 0 : 2];
                item.ReturnHome();
                if (hit)
                {
                    target.ResetTarget();
                    float settleUntil = Time.time + .15f;
                    while (Time.time < settleUntil) yield return null;
                }
                Vector3 start = hit ? target.home.position - game.transform.forward * .45f + Vector3.up * .15f
                    : game.transform.position + Vector3.right * 5;
                item.transform.position = start; item.body.position = start;
                Physics.SyncTransforms();
                item.TestLaunch(hit ? game.transform.forward * 4 : Vector3.down);
                Require(item.body.constraints == RigidbodyConstraints.FreezeRotation && item.body.angularVelocity == Vector3.zero,
                    "flight suppresses wrist spin");
                Require(!item.body.useGravity, "flight uses one explicit gravity force");
                foreach (var panelCollider in game.instructions.GetComponentsInChildren<Collider>(true))
                    foreach (var propCollider in item.GetComponentsInChildren<Collider>(true))
                        Require(panelCollider.isTrigger, "UI collider remains raycastable but cannot block projectiles");
                for (int supportIndex = 1; supportIndex <= game.targets.Length; supportIndex++)
                {
                    var support = game.transform.Find("Invisible Support " + supportIndex).GetComponent<Collider>();
                    Require(Physics.GetIgnoreCollision(item.GetComponent<SphereCollider>(), support), "projectile passes invisible target supports");
                }
                float end = Time.time + 7;
                while (game.story.FuelThrowPending && Time.time < end) yield return null;
                Require(game.resultFlash != null && game.resultFlash.IsFeedbackPlaying, "result color flash starts");
                Require(game.resultToast != null && game.resultToast.IsShowing, "result toast starts");
                Require(game.resultToast.image.texture == (hit ? game.resultToast.successImage : game.resultToast.failureImage), "matching result image");
                while ((game.story.FuelThrowPending || item.InFlight) && Time.time < end) yield return null;
                Require(!game.story.FuelThrowPending && !item.InFlight, "throw resolves");
                Require(!item.gameObject.activeSelf, "spent projectile disappears");
                Require(!game.BeginThrow(item), "spent projectile cannot be thrown twice");
                if (hit) Require(target.KnockedDown, "real collider collision topples cube: " + target.name + ", angle=" + Vector3.Angle(target.transform.up, target.home.up));
            }
            Require(game.story.FuelHits == 2 && game.story.FuelThrowsLeft == 0, "two physical hits and one miss; hits=" + game.story.FuelHits + ", left=" + game.story.FuelThrowsLeft);
            Require(game.continueButton.gameObject.activeSelf, "Continue after all throws");
            float toastEnd = Time.time + 3;
            while (game.resultToast.IsShowing && Time.time < toastEnd) yield return null;
            Require(!game.resultToast.IsShowing && game.resultToast.group.alpha == 0, "toast fades out");
            Debug.Log("FUEL PHYSICS PASSED: VideoEnded reveal, two actual physics knockdowns, miss timeout/fall, spent props disappear, three distinct throws, Continue.");
            // Restore a fresh preview without requesting Director content.
            game.gameObject.SetActive(false); game.story.PrepareFuelChallengePreview(); game.gameObject.SetActive(true);
        }
        private static void Require(bool ok, string label) { if (!ok) throw new Exception("Fuel validation failed: " + label); }
    }
}
