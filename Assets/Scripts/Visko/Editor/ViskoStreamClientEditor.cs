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
            DrawClientSettings();
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

        private void DrawClientSettings()
        {
            serializedObject.Update();
            var property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                bool external = serializedObject.FindProperty("externalScreen").boolValue;
                if (external && IsStandaloneScreenSetting(property.name)) continue;

                using (new EditorGUI.DisabledScope(property.name == "m_Script"))
                    EditorGUILayout.PropertyField(property, true);

                if (property.name == "externalScreen")
                {
                    EditorGUILayout.HelpBox(external
                        ? "Video output: VideoTexture. Another component displays it.\n" +
                          "In 1114: StageViskoPresentation → SharedVideoPlayer → CurvedVideoScreen.\n" +
                          "Edit the material on CurvedVideoScreen. This client does not create a screen or use its own Screen Material."
                        : "This client displays video on Target Renderer using a runtime copy of Screen Material. " +
                          "If no renderer is assigned, it creates a flat screen using the size and position below.",
                        MessageType.Info);
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        private static bool IsStandaloneScreenSetting(string name) =>
            name == "targetRenderer" || name == "screenMaterial" ||
            name == "screenDistance" || name == "screenWidth" ||
            name == "screenHeightOffset" || name == "showWorldStatus";
    }
}
