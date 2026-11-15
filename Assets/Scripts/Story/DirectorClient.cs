using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Visko;
using Debug = UnityEngine.Debug;

namespace TakeMeToTheMoon.Story
{
    /// <summary>
    /// Story Director over the OpenAI Responses API. Answers StoryController.NextSceneRequested
    /// with SubmitNextScene / ReportDirectorError. Port of WebTest/director.js; runs on Quest
    /// without a PC bridge. The key comes from a Git-ignored local resource
    /// (Tools > Director > Local OpenAI Key).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DirectorClient : MonoBehaviour
    {
        public const string ConfigResource = "OpenAILocalConfig";
        const string Endpoint = "https://api.openai.com/v1/responses";

        [SerializeField] private StoryController controller;
        [SerializeField] private string model = "gpt-5.5";
        [Tooltip("none, minimal, low, medium or high. Lower is faster.")]
        [SerializeField] private string reasoningEffort = "low";
        [Tooltip("System prompt for the Director. Edit this file to change the story rules.")]
        [SerializeField] private TextAsset systemPrompt;
        [Tooltip("Automatic retries after a failed request before the error is shown.")]
        [SerializeField, Range(0, 3)] private int autoRetries = 1;
        [Tooltip("Return canned scenes without calling OpenAI. For testing the flow with no key.")]
        [SerializeField] private bool mockMode;
        [Tooltip("While the player decides, write the next scene for every choice in parallel, so the picked " +
                 "one is ready immediately (one extra request per decision).")]
        [SerializeField] private bool prefetchChoices = true;
        [Tooltip("No branches are written ahead at or after this checkpoint (the demo stops there).")]
        [SerializeField, Range(1, 6)] private int noPrefetchFromCheckpoint = 6;

        private CancellationTokenSource cancel;
        private System.Threading.Tasks.Task<StoryScene>[] prefetched;
        private StoryScene prefetchedFor;
        private string apiKey;

        public bool MockMode { get => mockMode; set => mockMode = value; }
        public bool Busy { get; private set; }

        private void Reset() => controller = GetComponent<StoryController>();

        private void OnEnable()
        {
            if (controller == null) controller = GetComponent<StoryController>();
            cancel = new CancellationTokenSource();
            if (controller != null)
            {
                controller.NextSceneRequested.AddListener(OnNextSceneRequested);
                controller.ScenePresented.AddListener(OnScenePresented);
            }
            else Debug.LogError("[Director] Assign a StoryController.", this);
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.NextSceneRequested.RemoveListener(OnNextSceneRequested);
                controller.ScenePresented.RemoveListener(OnScenePresented);
            }
            prefetched = null;
            cancel?.Cancel();
            cancel?.Dispose();
            cancel = null;
            Busy = false;
        }

        // Start writing every branch as soon as a scene with choices is on screen.
        private void OnScenePresented(StoryScene scene)
        {
            prefetched = null;
            prefetchedFor = null;
            if (!prefetchChoices || scene == null || scene.is_ending || scene.choices == null ||
                scene.choices.Length == 0 || controller.Phase != StoryPhase.AwaitingChoice ||
                controller.Checkpoint >= noPrefetchFromCheckpoint) return;
            prefetchedFor = scene;
            prefetched = new System.Threading.Tasks.Task<StoryScene>[scene.choices.Length];
            for (int i = 0; i < scene.choices.Length; i++)
            {
                StoryDirectorInput input = controller.GetDirectorInputFor(i);
                if (input == null) continue;
                input.choice_approaches = PickApproaches();
                prefetched[i] = Prefetch(i, input, cancel.Token);
            }
            Debug.Log($"[Director] Writing {scene.choices.Length} branches ahead for '{scene.location}'.");
        }

        private async System.Threading.Tasks.Task<StoryScene> Prefetch(int index, StoryDirectorInput input, CancellationToken token)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                StoryScene scene = mockMode ? MockScene(input) : await RequestScene(input, token);
                Debug.Log($"[Director] Branch {index} ready in {timer.Elapsed.TotalSeconds:0.0}s: '{scene.location}'");
                return scene;
            }
            catch (Exception e)
            {
                // The live request after the pick retries anyway; never surface unobserved exceptions.
                if (!(e is OperationCanceledException)) Debug.LogWarning($"[Director] Branch {index} failed: {e.Message}");
                return null;
            }
        }

        private async void OnNextSceneRequested(int id)
        {
            StoryDirectorInput input = controller.GetDirectorInput();
            input.choice_approaches = PickApproaches();
            CancellationToken token = cancel.Token;
            Busy = true;

            // Use the branch written while the player was deciding, if it is (or becomes) ready.
            int picked = controller.LastChoiceIndex;
            var ready = prefetched != null && picked >= 0 && picked < prefetched.Length ? prefetched[picked] : null;
            prefetched = null;
            if (ready != null)
            {
                var timer = Stopwatch.StartNew();
                StoryScene scene = await ready;
                if (token.IsCancellationRequested || id != controller.PendingRequestId) return;
                if (scene != null)
                {
                    Debug.Log($"[Director] Request {id}: used prewritten branch {picked} '{scene.location}' " +
                              $"(waited {timer.Elapsed.TotalSeconds:0.0}s after the pick).");
                    controller.SubmitNextScene(id, scene);
                    if (id == controller.PendingRequestId) Busy = false;
                    return;
                }
            }
            Debug.Log($"[Director] Request {id}: checkpoint {input.checkpoint_number} ({input.checkpoint}), must_end={input.must_end}{(mockMode ? ", MOCK" : "")}");
            for (int attempt = 0; ; attempt++)
            {
                var timer = Stopwatch.StartNew();
                try
                {
                    StoryScene scene = mockMode ? MockScene(input) : await RequestScene(input, token);
                    if (token.IsCancellationRequested || id != controller.PendingRequestId) return;
                    Debug.Log($"[Director] Scene '{scene.location}' in {timer.Elapsed.TotalSeconds:0.0}s" +
                              $" ({scene.choices?.Length ?? 0} choices, ending={scene.is_ending})");
                    controller.SubmitNextScene(id, scene);
                    break;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e)
                {
                    if (token.IsCancellationRequested || id != controller.PendingRequestId) return;
                    Debug.LogWarning($"[Director] Attempt {attempt + 1} failed after {timer.Elapsed.TotalSeconds:0.0}s: {e.Message}");
                    if (attempt < autoRetries) continue;
                    controller.ReportDirectorError(id, "Director failed: " + e.Message);
                    break;
                }
            }
            if (id == controller.PendingRequestId) Busy = false;
        }

        private async System.Threading.Tasks.Task<StoryScene> RequestScene(StoryDirectorInput input, CancellationToken token)
        {
            string key = LoadKey();
            if (systemPrompt == null) throw new InvalidOperationException("Assign the Director system prompt asset.");

            var body = new JObject
            {
                ["model"] = model,
                ["instructions"] = systemPrompt.text,
                ["input"] = "Run state:\n" + JsonConvert.SerializeObject(input, Formatting.Indented) + "\n\nWrite the next beat.",
                ["max_output_tokens"] = 8000,
                ["text"] = new JObject
                {
                    ["format"] = new JObject
                    {
                        ["type"] = "json_schema", ["name"] = "scene", ["strict"] = true, ["schema"] = SceneSchema()
                    }
                }
            };
            if (!string.IsNullOrWhiteSpace(reasoningEffort))
                body["reasoning"] = new JObject { ["effort"] = reasoningEffort.Trim() };

            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + key,
                ["Content-Type"] = "application/json"
            };
            ReactorHttps.Reply reply = await ReactorHttps.Send("POST", Endpoint, headers,
                Encoding.UTF8.GetBytes(body.ToString(Formatting.None)), token);
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(reply.Error)) throw new InvalidOperationException(reply.Error);
            if (reply.Status < 200 || reply.Status >= 300)
                throw new InvalidOperationException($"OpenAI HTTP {reply.Status}: {ErrorMessage(reply.Body)}");
            return ParseScene(reply.Body);
        }

        // Two different random approaches per beat, so the Director does not default to the same choices.
        // Two different transports (1-10). Rarely, one is swapped for 11, the magic-portal wild card.
        private const float PortalChance = 1f / 12;
        private static int[] PickApproaches()
        {
            const int count = 10;
            int first = UnityEngine.Random.Range(1, count + 1);
            int second = UnityEngine.Random.Range(1, count);
            if (second >= first) second++;
            if (UnityEngine.Random.value < PortalChance) second = 11;
            return new[] { first, second };
        }

        private string LoadKey()
        {
            if (!string.IsNullOrWhiteSpace(apiKey)) return apiKey;
            var config = Resources.Load<TextAsset>(ConfigResource);
            string key = config == null ? null : (string)JObject.Parse(config.text)["apiKey"];
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("Set the key in Tools > Director > Local OpenAI Key.");
            return apiKey = key.Trim();
        }

        private static StoryScene ParseScene(string json)
        {
            JObject response = JObject.Parse(json);
            string status = (string)response["status"];
            foreach (JToken item in response["output"] ?? new JArray())
            {
                if ((string)item["type"] != "message") continue;
                foreach (JToken part in item["content"] ?? new JArray())
                {
                    string type = (string)part["type"];
                    if (type == "refusal") throw new InvalidOperationException("Director refused: " + (string)part["refusal"]);
                    if (type == "output_text")
                    {
                        var scene = JsonConvert.DeserializeObject<StoryScene>((string)part["text"]);
                        if (scene == null) break;
                        scene.choices ??= Array.Empty<StoryChoice>();
                        // Story choices are travel decisions; items are only used physically (launch pad, orbit).
                        foreach (StoryChoice choice in scene.choices) if (choice != null) choice.uses_item = null;
                        return scene;
                    }
                }
            }
            string reason = (string)response["incomplete_details"]?["reason"];
            throw new InvalidOperationException($"Director returned no scene (status: {status}{(reason != null ? ", " + reason : "")}).");
        }

        private static string ErrorMessage(string body)
        {
            try { return (string)JObject.Parse(body)["error"]?["message"] ?? body; }
            catch (JsonException) { return body; }
        }

        // Mirrors the Scene/Choice zod schema in WebTest/director.js. Strict mode: every field required.
        private static JObject SceneSchema()
        {
            JObject Str(string d) => new JObject { ["type"] = "string", ["description"] = d };
            JObject NullableStr(string d) => new JObject { ["type"] = new JArray("string", "null"), ["description"] = d };
            JObject Int(string d) => new JObject { ["type"] = "integer", ["description"] = d };
            JObject Obj(JObject props) => new JObject
            {
                ["type"] = "object", ["properties"] = props, ["additionalProperties"] = false,
                ["required"] = new JArray(props.Properties().Select(p => p.Name))
            };

            JObject choice = Obj(new JObject
            {
                ["label"] = Str("What the player picks. Max 8 words, imperative."),
                ["outcome_narration"] = Str("One short sentence (max 15 words) shown as a subtitle after the pick."),
                ["outcome_video_prompt"] = Str("Visko prompt for what the camera sees right after the pick. Same location as the scene."),
                ["miles_gained"] = Int("Progress toward the Moon from this pick, in miles. Always > 0."),
                ["wellness_change"] = Int("Change to wellness percent, usually 0 to -15. Rarely positive."),
                ["uses_item"] = NullableStr("Always null: choices never use inventory items."),
                ["roommate"] = NullableStr("Null unless this pick changes where the roommate is, e.g. 'took the escape pod in low orbit'."),
                ["kind"] = new JObject
                {
                    ["type"] = "string", ["enum"] = new JArray("good", "bad", "neutral"),
                    ["description"] = "good = caring/generous toward others, bad = selfish/ruthless, neutral = neither."
                }
            });
            return Obj(new JObject
            {
                ["narration"] = Str("One short sentence (max 18 words) shown as a subtitle, setting up the decision."),
                ["location"] = Str("Short name of where this happens, e.g. 'Memorial Church'."),
                ["transition"] = new JObject
                {
                    ["type"] = "string", ["enum"] = new JArray("morph", "cut"),
                    ["description"] = "'morph' if the location is the same as the previous beat, 'cut' if it changes."
                },
                ["arrival_narration"] = NullableStr("One short subtitle sentence (max 15 words) saying what the arrival escape is. Null only if arrival_video_prompt is null."),
                ["arrival_video_prompt"] = NullableStr("Visko prompt for the journey from the previous location into this checkpoint, continuing the last outcome. Null only if the location did not change."),
                ["video_prompt"] = Str("Visko prompt for the checkpoint while the player decides."),
                ["audio_prompt"] = Str("One sentence of what the scene SOUNDS like: ambience, instruments, materials. Not a picture description."),
                ["choices"] = new JObject
                {
                    ["type"] = "array", ["items"] = choice,
                    ["description"] = "Exactly 2 choices, each a way to leave this checkpoint for next_checkpoint. Empty only when is_ending is true."
                },
                ["is_ending"] = new JObject { ["type"] = "boolean" },
                ["ending_title"] = NullableStr("Only for endings, e.g. 'Ending: Sovereign Pelican Moon Queen'.")
            });
        }

        private static StoryScene MockScene(StoryDirectorInput input)
        {
            int step = Math.Max(1, input.distance_remaining_miles / Math.Max(1, input.decisions_left));
            if (input.must_end)
                return new StoryScene
                {
                    location = "Moon surface", transition = "cut", is_ending = true,
                    arrival_video_prompt = "A shaky descent onto grey lunar dust, thrusters kicking up a cloud.",
                    ending_title = "Ending: Mock Moon Landing",
                    narration = "You land on the Moon. It is, as promised, room temperature.",
                    video_prompt = "Grey lunar dust under boots, the blue Earth hanging over the horizon.",
                    audio_prompt = "Soft wind-like hum, slow breathing inside a helmet."
                };
            StoryChoice Choice(string label, int miles) => new StoryChoice
            {
                label = label, miles_gained = miles, wellness_change = -3,
                outcome_narration = "Mock outcome for: " + label + ".",
                outcome_video_prompt = "The camera rushes away from " + input.checkpoint + " toward " + input.next_checkpoint + "."
            };
            return new StoryScene
            {
                location = input.checkpoint, transition = "cut",
                narration = "Mock scene at " + input.checkpoint + ". Get to " + input.next_checkpoint + ".",
                arrival_video_prompt = "Rushing through the heat haze toward " + input.checkpoint + ".",
                arrival_narration = "Mock: you fight your way to " + input.checkpoint + ".",
                video_prompt = "A first-person view of " + input.checkpoint + ", orange heat haze.",
                audio_prompt = "Distant sirens and wind.",
                choices = new[] { Choice("Do the chaotic thing", step), Choice("Do the sneaky thing", step) }
            };
        }
    }
}
