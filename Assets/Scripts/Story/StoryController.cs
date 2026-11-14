using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events;

namespace TakeMeToTheMoon.Story
{
    public enum StoryPhase { SelectingItems, Opening, AwaitingChoice, WaitingForNextScene, Ended }

    /// <summary>Owns selection and story state. Presentation and AI consume its events.</summary>
    [DisallowMultipleComponent]
    public sealed class StoryController : MonoBehaviour
    {
        public const int StartDistance = 238900;

        [SerializeField] private StoryItemDefinition[] availableItems = Array.Empty<StoryItemDefinition>();
        [SerializeField, Min(1)] private int requiredItemCount = 3;
        [SerializeField, Range(1, 15)] private int maxDecisions = 6;
        [SerializeField, Min(0)] private float openingSeconds = 10;
        [SerializeField, Min(0)] private float outcomeSeconds = 6;
        [SerializeField] private TextAsset openingScenes;

        public UnityEvent StateChanged = new UnityEvent();
        public UnityEvent<StoryScene> ScenePresented = new UnityEvent<StoryScene>();
        public UnityEvent<StoryChoice> OutcomePresented = new UnityEvent<StoryChoice>();
        [Tooltip("Request ID. GetDirectorInput() supplies JSON data; return the result through SubmitNextScene(id, scene).")]
        public UnityEvent<int> NextSceneRequested = new UnityEvent<int>();
        public UnityEvent<string> ErrorRaised = new UnityEvent<string>();

        private readonly List<StoryItemDefinition> inventory = new List<StoryItemDefinition>();
        private readonly List<StoryHistoryEntry> history = new List<StoryHistoryEntry>();
        private StoryOpeningScenes fixedScenes;
        private StoryScene pendingScene;
        private float remainingSeconds;
        private int requestId;

        public StoryPhase Phase { get; private set; } = StoryPhase.SelectingItems;
        public IReadOnlyList<StoryItemDefinition> AvailableItems => Array.AsReadOnly(availableItems);
        public IReadOnlyList<StoryItemDefinition> Inventory => inventory.AsReadOnly();
        public StoryScene CurrentScene { get; private set; }
        public StoryChoice LastChoice { get; private set; }
        public int DistanceRemaining { get; private set; } = StartDistance;
        public int Wellness { get; private set; } = 100;
        public int Decisions { get; private set; }
        public string Roommate { get; private set; }
        public int RequiredItemCount => requiredItemCount;
        public int PendingRequestId => requestId;
        public bool CanStart => Phase == StoryPhase.SelectingItems && inventory.Count == requiredItemCount;
        public string LastError { get; private set; }

        private void OnEnable() => ResetStory();
        private void OnDisable() => ResetStory();

        private void Update()
        {
            if (Phase != StoryPhase.Opening && Phase != StoryPhase.WaitingForNextScene) return;
            remainingSeconds -= Time.unscaledDeltaTime;
            if (remainingSeconds > 0) return;
            if (Phase == StoryPhase.Opening) AdvanceOpening();
            else if (pendingScene != null)
            {
                StoryScene next = pendingScene;
                pendingScene = null;
                Present(next);
            }
        }

        // Void wrappers can be connected to Unity UI or Meta UnityEvent wrappers.
        public void SelectItem(StoryItemDefinition item) => TrySelectItem(item);
        public void DeselectItem(StoryItemDefinition item)
        {
            if (Phase != StoryPhase.SelectingItems || item == null) return;
            int index = FindItem(item.DirectorName);
            if (index < 0) return;
            inventory.RemoveAt(index);
            StateChanged.Invoke();
        }

        public bool TrySelectItem(StoryItemDefinition item)
        {
            if (Phase != StoryPhase.SelectingItems || item == null ||
                !Array.Exists(availableItems, candidate => candidate == item) ||
                string.IsNullOrWhiteSpace(item.DirectorName) || FindItem(item.DirectorName) >= 0 ||
                inventory.Count >= requiredItemCount) return false;
            inventory.Add(item);
            StateChanged.Invoke();
            return true;
        }

        public bool HasItem(StoryItemDefinition item) => item != null && FindItem(item.DirectorName) >= 0;

        private int FindItem(string name) => inventory.FindIndex(item => item != null &&
            string.Equals(item.DirectorName, name, StringComparison.OrdinalIgnoreCase));

        public void StartStory() => TryStartStory();

        public bool TryStartStory()
        {
            if (!isActiveAndEnabled || !CanStart) return false;
            try
            {
                fixedScenes = openingScenes == null ? null :
                    JsonConvert.DeserializeObject<StoryOpeningScenes>(openingScenes.text);
                if (fixedScenes == null || !IsValidScene(fixedScenes.opening, true) ||
                    fixedScenes.opening.is_ending || !IsValidScene(fixedScenes.roommate, false) ||
                    fixedScenes.roommate.is_ending)
                    return Fail("Assign valid OpeningScenes.json before starting the story.");
            }
            catch (JsonException e) { return Fail("Invalid opening scene JSON: " + e.Message); }

            LastError = null;
            Phase = StoryPhase.Opening;
            remainingSeconds = Mathf.Max(0, openingSeconds);
            CurrentScene = fixedScenes.opening;
            CurrentScene.transition = "cut";
            StateChanged.Invoke();
            ScenePresented.Invoke(CurrentScene);
            return true;
        }

        public void AdvanceOpening()
        {
            if (Phase == StoryPhase.Opening) Present(fixedScenes.roommate);
        }

        public bool CanChoose(int index)
        {
            if (Phase != StoryPhase.AwaitingChoice || CurrentScene == null ||
                index < 0 || index >= CurrentScene.choices.Length) return false;
            string item = CurrentScene.choices[index].uses_item;
            return string.IsNullOrWhiteSpace(item) || FindItem(item) >= 0;
        }

        public void Choose(int index) => TryChoose(index);

        public bool TryChoose(int index)
        {
            if (!CanChoose(index)) return false;
            StoryChoice choice = CurrentScene.choices[index];
            // Lock before events: repeated socket/button input cannot apply an outcome twice.
            Phase = StoryPhase.WaitingForNextScene;
            DistanceRemaining = Math.Max(0, DistanceRemaining - Math.Max(0, choice.miles_gained));
            Wellness = (int)Math.Max(0L, Math.Min(100L, (long)Wellness + choice.wellness_change));
            if (!string.IsNullOrWhiteSpace(choice.uses_item)) inventory.RemoveAt(FindItem(choice.uses_item));
            if (!string.IsNullOrWhiteSpace(choice.roommate)) Roommate = choice.roommate;
            else Decisions++;
            history.Add(new StoryHistoryEntry
            {
                location = CurrentScene.location, situation = CurrentScene.narration,
                picked = choice.label, outcome = choice.outcome_narration
            });
            LastChoice = choice;
            LastError = null;
            pendingScene = null;
            remainingSeconds = Mathf.Max(0, outcomeSeconds);
            int id = ++requestId;
            StateChanged.Invoke();
            OutcomePresented.Invoke(choice);
            if (Phase == StoryPhase.WaitingForNextScene && requestId == id)
                NextSceneRequested.Invoke(id);
            return true;
        }

        /// <summary>Call on Unity's main thread. Late results from reset/retry are ignored.</summary>
        public bool SubmitNextScene(int id, StoryScene scene)
        {
            if (Phase != StoryPhase.WaitingForNextScene || id != requestId || pendingScene != null) return false;
            if (!IsValidScene(scene, false)) return Fail("Director returned an invalid story scene.");
            if (GetDirectorInput().must_end && !scene.is_ending)
                return Fail("The decision or distance limit is reached; the next scene must be an ending.");
            pendingScene = JsonConvert.DeserializeObject<StoryScene>(JsonConvert.SerializeObject(scene));
            LastError = null;
            StateChanged.Invoke();
            return true;
        }

        public void ReportDirectorError(int id, string message)
        {
            if (Phase == StoryPhase.WaitingForNextScene && id == requestId && pendingScene == null)
                Fail(message);
        }

        public void RetryNextScene()
        {
            if (Phase != StoryPhase.WaitingForNextScene || pendingScene != null) return;
            LastError = null;
            int id = ++requestId;
            StateChanged.Invoke();
            NextSceneRequested.Invoke(id); // Does not replay item consumption or stat changes.
        }

        public StoryDirectorInput GetDirectorInput()
        {
            int left = Mathf.Max(0, maxDecisions - Decisions);
            float fraction = (float)DistanceRemaining / StartDistance;
            return new StoryDirectorInput
            {
                decision_number = Decisions + 1, decisions_left = left,
                must_end = DistanceRemaining <= 0 || left == 0,
                stage = fraction > .85f ? "escape campus and leave the ground" :
                    fraction > .5f ? "get airborne, up through the sky" :
                    fraction > .15f ? "out in space, travelling to the Moon" : "approaching the Moon",
                distance_remaining_miles = DistanceRemaining, wellness_percent = Wellness,
                inventory = inventory.ConvertAll(item => item.DirectorName).ToArray(),
                roommate = Roommate, previous_location = CurrentScene?.location,
                history = history.ConvertAll(entry => new StoryHistoryEntry
                {
                    location = entry.location, situation = entry.situation,
                    picked = entry.picked, outcome = entry.outcome
                }).ToArray()
            };
        }

        public void ResetStory()
        {
            ++requestId;
            pendingScene = null;
            fixedScenes = null;
            inventory.Clear();
            history.Clear();
            CurrentScene = null;
            LastChoice = null;
            LastError = null;
            DistanceRemaining = StartDistance;
            Wellness = 100;
            Decisions = 0;
            Roommate = null;
            Phase = StoryPhase.SelectingItems;
            StateChanged.Invoke();
        }

        private void Present(StoryScene scene)
        {
            CurrentScene = scene;
            Phase = scene.is_ending ? StoryPhase.Ended : StoryPhase.AwaitingChoice;
            StateChanged.Invoke();
            ScenePresented.Invoke(scene);
        }

        private bool Fail(string message)
        {
            LastError = message;
            StateChanged.Invoke();
            ErrorRaised.Invoke(message);
            return false;
        }

        private static bool IsValidScene(StoryScene scene, bool allowNoChoices)
        {
            if (scene == null || string.IsNullOrWhiteSpace(scene.location) ||
                string.IsNullOrWhiteSpace(scene.narration) || string.IsNullOrWhiteSpace(scene.video_prompt) ||
                (scene.transition != "morph" && scene.transition != "cut") || scene.choices == null) return false;
            if (!allowNoChoices && !scene.is_ending && scene.choices.Length == 0) return false;
            foreach (StoryChoice choice in scene.choices)
                if (choice == null || string.IsNullOrWhiteSpace(choice.label) ||
                    string.IsNullOrWhiteSpace(choice.outcome_narration) ||
                    string.IsNullOrWhiteSpace(choice.outcome_video_prompt)) return false;
            return true;
        }
    }
}
