using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class StageFlowValidation
    {
        [MenuItem("Tools/Game Flow/Validate Five Stage Flow (Play Mode)")]
        public static void Validate()
        {
            if (!Application.isPlaying) { Debug.LogError("Run this validation in Play Mode."); return; }
            var root = new GameObject("Stage Flow Validation");
            var target = new GameObject("Reveal Test Target");
            root.SetActive(false);
            try
            {
                var manager = root.AddComponent<GameManager>();
                var so = new SerializedObject(manager);
                so.FindProperty("transitionDuration").floatValue = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
                root.SetActive(true);
                var field = typeof(GameManager).GetField("stages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var stages = (StagePresentation[])field.GetValue(manager);
                int reveals = 0;
                stages[0].revealObjects = new[] { target };
                stages[0].revealAfterSeconds = 0;
                stages[0].Revealed.AddListener(() => reveals++);
                manager.StartGame();
                Check(manager.Phase == GamePhase.Prologue, "START must enter prologue");
                manager.NextStage();
                Check(manager.CurrentStageNumber == 1 && !target.activeSelf, "Stage 1 must hide delayed target");
                manager.SendMessage("Update");
                Check(target.activeSelf && reveals == 1 && manager.CurrentStageNumber == 1, "Timed reveal must not advance stage");
                manager.RevealCurrentStage();
                Check(reveals == 1, "Reveal must fire only once per entry");
                manager.ReturnToStart();
                Check(!target.activeSelf, "Return must hide delayed target");
                manager.ShowStage(1);
                stages[0].revealWhen = RevealCondition.VideoEnded;
                manager.SendMessage("Update");
                Check(!target.activeSelf, "Video-ended condition must ignore timer");
                manager.SendMessage("OnStageVideoEnded");
                Check(target.activeSelf && reveals == 2, "Video-ended reveal and replay reset");
                for (int n = 2; n <= 5; n++)
                {
                    manager.NextStage();
                    Check(manager.CurrentStageNumber == n && manager.Phase == GamePhase.Main, "Ordered stage " + n);
                }
                manager.NextStage();
                Check(manager.Phase == GamePhase.EndScreen, "Stage 5 must lead to ending");
                Debug.Log("PASS: prologue, stages 1–5, ending, timed/video reveal, one-shot events and replay cleanup.");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(target); }
        }
        private static void Check(bool result, string message)
        {
            if (!result) throw new Exception("Stage validation failed: " + message);
        }
    }
}
