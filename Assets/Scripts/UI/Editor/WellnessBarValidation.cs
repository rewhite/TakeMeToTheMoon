using System;
using System.Collections;
using TakeMeToTheMoon.Story;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class WellnessBarValidation
    {
        private static IEnumerator run;
        [MenuItem("Tools/Game Flow/Validate Wellness Heartbeat (Play Mode)")]
        public static void Validate()
        {
            if (!EditorApplication.isPlaying || run != null) return;
            run = Check();
            EditorApplication.update += Tick;
        }
        private static void Tick()
        {
            try { if (EditorApplication.isPlaying && run.MoveNext()) return; }
            catch (Exception e) { Debug.LogException(e); }
            (run as IDisposable)?.Dispose(); run = null;
            EditorApplication.update -= Tick;
        }
        private static IEnumerator Check()
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Wellness Bar.prefab"));
            go.name = "Wellness validation (temporary)";
            try
            {
                var bar = go.GetComponent<WellnessBar>();
                var story = Object.FindFirstObjectByType<StoryController>(FindObjectsInactive.Include);
                if (story != null)
                {
                    story.StateChanged.Invoke();
                    Require(bar.Value == story.Wellness, "Story value is authoritative");
                }
                go.SetActive(false);
                var data = new SerializedObject(bar);
                data.FindProperty("followStory").boolValue = false;
                data.ApplyModifiedPropertiesWithoutUndo();
                go.SetActive(true);
                var heart = go.transform.Find("Heart");
                var label = go.GetComponentInChildren<TMP_Text>();
                bar.SetValue(100);
                float slow = bar.BeatsPerMinute;
                float min = float.PositiveInfinity, max = 0, until = Time.unscaledTime + 1.4f;
                while (Time.unscaledTime < until)
                { min = Mathf.Min(min, heart.localScale.x); max = Mathf.Max(max, heart.localScale.x); yield return null; }
                Require(max - min > .05f, "Heart visibly pulses at high wellness");
                bar.SetValue(10);
                Require(bar.BeatsPerMinute > slow && label.text == "10", "Low wellness changes number and speeds heartbeat");
                min = float.PositiveInfinity; max = 0; until = Time.unscaledTime + 1;
                while (Time.unscaledTime < until)
                { min = Mathf.Min(min, heart.localScale.x); max = Mathf.Max(max, heart.localScale.x); yield return null; }
                Require(max - min > .05f, "Heart visibly pulses at low wellness");
                bar.SetValue(-10); Require(bar.Value == 0, "Clamp minimum");
                bar.SetValue(110); Require(bar.Value == 100, "Clamp maximum");
                go.SetActive(false); Require(Mathf.Abs(heart.localScale.x - 1) < .001f, "Disable restores resting scale");
                Debug.Log("WELLNESS HEARTBEAT VALIDATION PASSED: story binding, live high/low pulses, faster low-wellness rate, number updates, clamping and disable reset.");
            }
            finally { Object.Destroy(go); }
        }
        private static void Require(bool value, string message) { if (!value) throw new Exception("Wellness validation failed: " + message); }
    }
}
