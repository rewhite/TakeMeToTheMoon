#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UIElements;

namespace Visko
{
    // Editor Play-mode controls shared by ordinary Game view and XR simulation.
    public sealed class ViskoEditorOverlay : MonoBehaviour
    {
        ViskoStreamClient _client;
        GameObject _ui;
        PanelSettings _settings;
        VisualElement _panel, _commands;
        Label _status;
        TextField _prompt;

        public void Initialize(ViskoStreamClient client)
        {
            _client = client;
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/Scripts/Visko/Editor/ViskoDebugTheme.tss");
            _settings.sortingOrder = 1000;
            _ui = new GameObject("Visko Editor Debug UI");
            _ui.transform.SetParent(transform, false);
            var document = _ui.AddComponent<UIDocument>();
            document.panelSettings = _settings;
            var root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            _panel = new VisualElement();
            _panel.style.position = Position.Absolute;
            _panel.style.left = 10; _panel.style.top = 10;
            _panel.style.width = 440; _panel.style.maxWidth = Length.Percent(95);
            _panel.style.paddingLeft = 10; _panel.style.paddingRight = 10;
            _panel.style.paddingTop = 10; _panel.style.paddingBottom = 10;
            _panel.style.backgroundColor = new Color(.12f, .12f, .12f, .96f);
            _panel.style.color = Color.white;
            root.Add(_panel);
            _status = new Label(); _panel.Add(_status);
            _prompt = new TextField { multiline = true, value = client.startPrompt };
            _prompt.style.height = 100; _panel.Add(_prompt);
            _commands = new VisualElement(); _panel.Add(_commands);
            AddButton(_commands, "Send Prompt", () => client.SetPrompt(_prompt.value));
            AddButton(_commands, "Start", () => { client.SetPrompt(_prompt.value); client.StartGeneration(); });
            AddButton(_commands, "Pause", client.PauseGeneration);
            AddButton(_commands, "Resume", client.ResumeGeneration);
            AddButton(_panel, "Connect", () => { if (!client.SessionReady) _ = client.Connect(); });
            AddButton(_panel, "Disconnect", client.Disconnect);
        }

        static void AddButton(VisualElement parent, string text, System.Action action)
            => parent.Add(new Button(action) { text = text });

        void Update()
        {
            if (_client == null || _panel == null) return;
            _panel.style.display = _client.isActiveAndEnabled && _client.showDebugGui
                ? DisplayStyle.Flex : DisplayStyle.None;
            _status.text = "Visko: " + _client.Status;
            _commands.SetEnabled(_client.SessionReady);
        }

        void OnDestroy()
        {
            if (_ui != null) Destroy(_ui);
            if (_settings != null) Destroy(_settings);
        }
    }
}
#endif
