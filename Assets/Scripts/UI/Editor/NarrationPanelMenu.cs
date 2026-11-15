using UnityEditor;
using UnityEngine;

namespace TakeMeToTheMoon.UI.Editor
{
    public static class NarrationPanelMenu
    {
        [MenuItem("GameObject/UI/Narration Canvas", false, 10)]
        static void Create(MenuCommand command)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Narration Canvas.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(go, "Create Narration Canvas");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            // Preserve the prefab's pixel-to-metre scale after parenting.
            go.transform.localScale = Vector3.one * 0.001f;
            if (go.transform.parent == null && Camera.main != null)
            {
                var camera = Camera.main.transform;
                go.transform.SetPositionAndRotation(camera.position + camera.forward * 2 - camera.up * 0.5f, camera.rotation);
            }
            Selection.activeGameObject = go;
        }
    }
}
