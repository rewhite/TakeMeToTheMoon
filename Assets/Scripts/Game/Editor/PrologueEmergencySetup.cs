using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class PrologueEmergencySetup
    {
        [MenuItem("Tools/Game Flow/Configure Prologue Emergency Pulse")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var manager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            var eye = manager.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).First(c => c.name == "CenterEyeAnchor");
            var pulse = Object.FindAnyObjectByType<PrologueEmergencyPulse>(FindObjectsInactive.Include);
            if (pulse == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Prologue Emergency Pulse";
                Undo.RegisterCreatedObjectUndo(go, "Add prologue emergency pulse");
                Object.DestroyImmediate(go.GetComponent<Collider>());
                pulse = Undo.AddComponent<PrologueEmergencyPulse>(go);
            }
            const string path = "Assets/Materials/Prologue Emergency Pulse.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("TakeMeToTheMoon/Emergency Pulse"));
                AssetDatabase.CreateAsset(material, path);
            }
            var surface = pulse.GetComponent<MeshRenderer>();
            Undo.RecordObject(surface, "Configure emergency overlay");
            surface.sharedMaterial = material;
            surface.shadowCastingMode = ShadowCastingMode.Off;
            surface.receiveShadows = false;
            surface.sortingOrder = short.MaxValue - 1;
            surface.enabled = false;
            var data = new SerializedObject(pulse);
            data.FindProperty("manager").objectReferenceValue = manager;
            data.FindProperty("eye").objectReferenceValue = eye;
            data.FindProperty("surface").objectReferenceValue = surface;
            data.ApplyModifiedProperties();
            Undo.RecordObject(pulse.transform, "Place emergency overlay");
            pulse.transform.position = eye.transform.position;
            pulse.transform.localScale = Vector3.one * 2;
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Selection.activeGameObject = pulse.gameObject;
            Debug.Log("Prologue emergency pulse configured: preplaced collider-free stereo overlay, 4 second cycle, 16% peak red, disabled outside prologue.");
        }
    }
}
