using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace TakeMeToTheMoon.Story.Editor
{
    /// <summary>Stores the OpenAI development key in a Git-ignored Resources file used by DirectorClient.</summary>
    public sealed class DirectorKeyWindow : EditorWindow
    {
        const string ConfigPath = "Assets/Resources/" + DirectorClient.ConfigResource + ".json";
        string key = "";

        [MenuItem("Tools/Director/Local OpenAI Key")]
        static void Open() => GetWindow<DirectorKeyWindow>(true, "Director OpenAI Key");

        void OnEnable()
        {
            if (File.Exists(ConfigPath)) key = (string)JObject.Parse(File.ReadAllText(ConfigPath))["apiKey"] ?? "";
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("OpenAI API key (local development only)", EditorStyles.boldLabel);
            key = EditorGUILayout.PasswordField("API key", key);
            EditorGUILayout.HelpBox("Saved in a Git-ignored local resource and included in your local APK. Do not share that APK.", MessageType.Info);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(key)))
                if (GUILayout.Button("Save local key"))
                {
                    Directory.CreateDirectory("Assets/Resources");
                    File.WriteAllText(ConfigPath, new JObject { ["apiKey"] = key.Trim() }.ToString());
                    AssetDatabase.ImportAsset(ConfigPath);
                    Close();
                }
        }
    }
}
