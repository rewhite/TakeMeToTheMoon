using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TakeMeToTheMoon.Story.Editor
{
    public static class StoryInventoryGhostSetup
    {
        [MenuItem("Tools/Story/Configure Inventory Ghost Preview")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var tray = Object.FindAnyObjectByType<StoryInventoryTray>(FindObjectsInactive.Include);
            if (tray == null) { Debug.LogError("Configure the inventory tray first."); return; }
            if (tray.GetComponent<StoryInventoryGhost>() != null)
            { Selection.activeGameObject = tray.gameObject; Debug.Log("Inventory ghost preview already configured."); return; }
            var material = GetMaterial();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure inventory ghost previews");
            var holder = new GameObject("Ghost Previews");
            Undo.RegisterCreatedObjectUndo(holder, "Create preplaced ghost meshes");
            holder.transform.SetParent(tray.transform, false);
            var feedback = Undo.AddComponent<StoryInventoryGhost>(tray.gameObject);
            var settings = new SerializedObject(feedback);
            settings.FindProperty("tray").objectReferenceValue = tray;
            var sources = new SerializedObject(tray).FindProperty("allowedItems");
            var previews = settings.FindProperty("previews");
            previews.arraySize = sources.arraySize;
            for (int i = 0; i < sources.arraySize; i++)
            {
                var source = (SnapInteractor)sources.GetArrayElementAtIndex(i).objectReferenceValue;
                var ghost = CopyVisuals(source.transform, holder.transform, material);
                ghost.name = source.name + " Ghost";
                ghost.gameObject.SetActive(false);
                previews.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue = source;
                previews.GetArrayElementAtIndex(i).FindPropertyRelative("visual").objectReferenceValue = ghost;
            }
            settings.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(tray.gameObject.scene);
            Selection.activeGameObject = tray.gameObject;
            Debug.Log("Inventory ghost preview configured: five preplaced visual-only meshes, shared translucent material, exact snap-anchor alignment.");
        }
        private static Transform CopyVisuals(Transform source, Transform parent, Material material)
        {
            var go = new GameObject(source.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = source.localPosition;
            go.transform.localRotation = source.localRotation;
            go.transform.localScale = source.localScale;
            go.SetActive(source.gameObject.activeSelf);
            var filter = source.GetComponent<MeshFilter>();
            var renderer = source.GetComponent<MeshRenderer>();
            if (filter != null && renderer != null)
            {
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var ghost = go.AddComponent<MeshRenderer>();
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                ghost.sharedMaterials = materials;
                ghost.enabled = renderer.enabled;
                ghost.shadowCastingMode = ShadowCastingMode.Off;
                ghost.receiveShadows = false;
                ghost.lightProbeUsage = LightProbeUsage.Off;
                ghost.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            foreach (Transform child in source)
                if (child.GetComponentsInChildren<MeshRenderer>(true).Length > 0)
                    CopyVisuals(child, go.transform, material);
            return go.transform;
        }
        private static Material GetMaterial()
        {
            const string path = "Assets/Materials/Inventory Ghost.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.name = "Inventory Ghost";
            material.SetColor("_BaseColor", new Color(.25f, .95f, .85f, .28f));
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
