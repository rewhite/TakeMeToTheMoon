using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Visko.Editor
{
    public class ViskoDevelopment : EditorWindow
    {
        const string ConfigPath = "Assets/Resources/ReactorLocalConfig.json";
        string _key = "";
        [MenuItem("Tools/Visko/Local API Key")]
        static void Open() => GetWindow<ViskoDevelopment>(true, "Visko Development Key");
        void OnEnable()
        {
            if (File.Exists(ConfigPath)) _key = (string)JObject.Parse(File.ReadAllText(ConfigPath))["apiKey"] ?? "";
        }
        void OnGUI()
        {
            EditorGUILayout.LabelField("Reactor API key (local development only)", EditorStyles.boldLabel);
            _key = EditorGUILayout.PasswordField("API key", _key);
            EditorGUILayout.HelpBox("Saved in a Git-ignored local resource and included in your local APK. Do not share that APK. No Python bridge or authentication server is needed.", MessageType.Info);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_key)))
                if (GUILayout.Button("Save local key")) {
                    Directory.CreateDirectory("Assets/Resources");
                    File.WriteAllText(ConfigPath, new JObject { ["apiKey"] = _key.Trim() }.ToString());
                    AssetDatabase.ImportAsset(ConfigPath);
                    Close();
                }
        }

        [MenuItem("Tools/Visko/Build Quest Test APK")]
        public static void BuildQuestTest()
        {
            if (!File.Exists(ConfigPath)) { Open(); return; }
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new System.InvalidOperationException("Install Android Build Support through Unity Hub first.");
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.optimizedFramePacing = false;
            Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/Scenes/ViskoTest.unity" },
                target = BuildTarget.Android,
                locationPathName = "Builds/ViskoQuestTest.apk",
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.InvalidOperationException("Quest test build failed. Check the Unity Console.");
            Debug.Log("Visko Quest test APK built in Builds/ViskoQuestTest.apk (contains local development credentials).");
        }
    }
}
