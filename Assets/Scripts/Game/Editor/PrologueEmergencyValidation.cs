using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class PrologueEmergencyValidation
    {
        [MenuItem("Tools/Game Flow/Validate Prologue Emergency Pulse (Play Mode)")]
        public static void Validate()
        {
            if (!Application.isPlaying) throw new Exception("Enter Play Mode first.");
            var source = UnityEngine.Object.FindAnyObjectByType<PrologueEmergencyPulse>(FindObjectsInactive.Include);
            var root = new GameObject("Emergency pulse validation"); root.SetActive(false);
            try
            {
                var manager = root.AddComponent<GameManager>();
                var settings = new SerializedObject(manager);
                settings.FindProperty("transitionDuration").floatValue = 0;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var copy = UnityEngine.Object.Instantiate(source.gameObject, root.transform);
                var pulse = copy.GetComponent<PrologueEmergencyPulse>();
                var data = new SerializedObject(pulse);
                data.FindProperty("manager").objectReferenceValue = manager;
                data.ApplyModifiedPropertiesWithoutUndo();
                root.SetActive(true);
                if (pulse.IsPulsing || copy.GetComponent<MeshRenderer>().enabled) throw new Exception("Pulse visible on start screen.");
                manager.ShowPrologue();
                if (!pulse.IsPulsing) throw new Exception("Prologue did not enable pulse.");
                typeof(PrologueEmergencyPulse).GetField("elapsed", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pulse, 2f);
                pulse.SendMessage("LateUpdate");
                if (pulse.CurrentOpacity < .14f || pulse.CurrentOpacity > .17f || !copy.GetComponent<MeshRenderer>().enabled)
                    throw new Exception("Pulse did not reach gentle red peak.");
                manager.ShowStage(1);
                if (pulse.IsPulsing || copy.GetComponent<MeshRenderer>().enabled) throw new Exception("Pulse leaked into stage 1.");
                if (copy.GetComponent<Collider>() != null) throw new Exception("Overlay must not obstruct interactions.");
                Debug.Log("EMERGENCY PULSE VALIDATION PASSED: start hidden, prologue enabled, gentle red peak, stage 1 hidden, no collider.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
