using TMPro;
using UnityEditor;
using UnityEngine;

namespace TakeMeToTheMoon.UI.Editor
{
    [CustomEditor(typeof(IntroPanel))]
    public sealed class IntroPanelEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var panel = (IntroPanel)target;
            EditorGUILayout.LabelField("Panel Text", EditorStyles.boldLabel);
            EditText("Title", panel.TitleText, 45);
            EditText("Body", panel.BodyText, 120);
            EditorGUILayout.Space();
            DrawDefaultInspector();
            panel.RefreshLayout();
        }
        private static void EditText(string label, TMP_Text text, float height)
        {
            if (text == null) return;
            var so = new SerializedObject(text);
            so.Update();
            var property = so.FindProperty("m_text");
            EditorGUILayout.LabelField(label);
            EditorGUI.BeginChangeCheck();
            string next = EditorGUILayout.TextArea(property.stringValue, GUILayout.MinHeight(height));
            if (EditorGUI.EndChangeCheck())
            {
                property.stringValue = next;
                so.ApplyModifiedProperties();
            }
        }
    }
}
