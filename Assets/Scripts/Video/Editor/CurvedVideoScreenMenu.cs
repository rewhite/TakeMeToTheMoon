using UnityEditor;
using UnityEngine;

namespace TakeMeToTheMoon.Video.Editor
{
    public static class CurvedVideoScreenMenu
    {
        [MenuItem("GameObject/Video/Curved Video Screen", false, 10)]
        static void Create(MenuCommand command)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Curved Video Screen.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(go, "Create Curved Video Screen");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            if (go.transform.parent == null && Camera.main != null)
            {
                var camera = Camera.main.transform;
                go.transform.SetPositionAndRotation(camera.position + camera.forward * 2, camera.rotation);
            }
            Selection.activeGameObject = go;
        }
    }
}
