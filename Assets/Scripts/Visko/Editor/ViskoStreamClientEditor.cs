using UnityEditor;
using UnityEngine;

namespace Visko.Editor
{
    [CustomEditor(typeof(ViskoStreamClient))]
    public class ViskoStreamClientEditor : UnityEditor.Editor
    {
        string _prompt;
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!Application.isPlaying) return;

            var client = (ViskoStreamClient)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live Visko Controls", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Status", client.Status);
            _prompt = EditorGUILayout.TextArea(_prompt ?? client.startPrompt, GUILayout.MinHeight(90));
            using (new EditorGUI.DisabledScope(!client.isActiveAndEnabled || !client.SessionReady))
            {
                if (GUILayout.Button("Send Prompt")) client.SetPrompt(_prompt);
                if (GUILayout.Button("Start Generation"))
                {
                    client.SetPrompt(_prompt);
                    client.StartGeneration();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Pause")) client.PauseGeneration();
                    if (GUILayout.Button("Resume")) client.ResumeGeneration();
                }
            }
        }
    }
}
