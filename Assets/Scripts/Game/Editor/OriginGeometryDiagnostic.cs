using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class OriginGeometryDiagnostic
    {
        [MenuItem("Tools/Game Flow/Disable Wall Penetration Gray Mask")]
        public static void DisableGrayMask()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var wall in Object.FindObjectsByType<Oculus.Interaction.Locomotion.WallPenetrationTunneling>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!wall.gameObject.scene.IsValid()) continue;
                var data = new SerializedObject(wall);
                var effect = data.FindProperty("_tunneling").objectReferenceValue as Oculus.Interaction.TunnelingEffect;
                Undo.RecordObject(wall, "Disable wall penetration gray mask");
                wall.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(wall);
                if (effect != null)
                {
                    Undo.RecordObject(effect, "Disable wall penetration overlay");
                    effect.enabled = false;
                    effect.UserFOV = 360;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(effect);
                    foreach (var renderer in effect.GetComponentsInChildren<Renderer>(true))
                    {
                        Undo.RecordObject(renderer, "Hide wall penetration mesh");
                        renderer.enabled = false;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    }
                }
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(wall.gameObject.scene);
                Selection.activeGameObject = wall.gameObject;
                Debug.Log("Disabled Meta wall-penetration gray overlay in scene. Gameplay colliders and other locomotion effects retained.", wall);
            }
        }

        [MenuItem("Tools/Game Flow/Inspect Geometry Near Origin")]
        public static void Inspect()
        {
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in cameras)
                Debug.Log($"ORIGIN CAMERA {AnimationUtility.CalculateTransformPath(c.transform, null)} active={c.isActiveAndEnabled} position={c.transform.position} near={c.nearClipPlane} clear={c.clearFlags} background={c.backgroundColor}", c);
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.scene.IsValid()) continue;
                bool near = r.bounds.SqrDistance(Vector3.zero) < 1 || cameras.Any(c => c.isActiveAndEnabled && r.bounds.SqrDistance(c.transform.position) < .25f);
                if (!near) continue;
                Debug.Log($"ORIGIN GEOMETRY {AnimationUtility.CalculateTransformPath(r.transform, null)} bounds={r.bounds} materials={string.Join(",", r.sharedMaterials.Select(m => m == null ? "NULL" : m.name + ":" + m.shader.name))}", r);
            }
        }
    }
}
