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

        /// <summary>Story checkpoints in order. 1 = dorm (fixed scenes); 2-5 are Director beats; 6 = ending.</summary>
        public static readonly string[] Checkpoints =
        {
            "Stanford dorm room",
            "Stanford campus at Memorial Church",
            "Rocket launch pad",
            "Low Earth orbit",
            "Approaching the Moon",
            "Moon landing"
        };
        public const int EndingCheckpoint = 6;
        private const int DirectorBeats = 4; // Checkpoints 2-5, one decision each.

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
        [Tooltip("Checkpoint number (1-6) when a scene at a new checkpoint is presented. Connect to GameManager.ShowStage; 6 is the ending.")]
        public UnityEvent<int> CheckpointEntered = new UnityEvent<int>();
        [Tooltip("New wellness and the change, after every choice and every fuel throw. For pop-ups and the bar.")]
        public UnityEvent<int, int> WellnessChanged = new UnityEvent<int, int>();
        [Tooltip("The final verdict, when the ending is presented. Drives the Mission Complete / Access Denied screen.")]
        public UnityEvent<StoryVerdict> VerdictReady = new UnityEvent<StoryVerdict>();
        /// <summary>Final wellness at or above this lets the player in (Moon Citizen).</summary>
        public const int PassWellness = 85;

        private readonly List<StoryItemDefinition> inventory = new List<StoryItemDefinition>();
        private readonly List<StoryHistoryEntry> history = new List<StoryHistoryEntry>();
        private StoryOpeningScenes fixedScenes;
        private StoryScene pendingScene;
        public event Action<int, StoryScene> NextScenePrepared;
        private bool waitingForNextVideo;
        private bool requireNextContinue, nextContinuePressed;
        public bool NextContinueVisible => requireNextContinue && Phase == StoryPhase.WaitingForNextScene && remainingSeconds <= 0;
        public bool CanContinueNextScene => NextContinueVisible && !nextContinuePressed && pendingScene != null &&
            !waitingForNextVideo && !waitingForOutcomeVideo && !videoPlaybackPaused;
        public void RequireNextSceneContinue()
        {
            if (Phase != StoryPhase.WaitingForNextScene) return;
            requireNextContinue = true;
            nextContinuePressed = false;
        }
        public bool ContinueNextScene()
        {
            if (!CanContinueNextScene) return false;
            nextContinuePressed = true;
            return true;
        }
        public void SetNextVideoPending(int id, bool pending)
        {
            if (id == requestId && Phase == StoryPhase.WaitingForNextScene)
                waitingForNextVideo = pending;
        }
        private float remainingSeconds;
        private bool videoPlaybackPaused;
        public void SetVideoPlaybackPaused(bool paused) => videoPlaybackPaused = paused;
        private bool waitingForOpeningVideo;
        public void HoldOpeningForVideo() { if (Phase == StoryPhase.Opening) waitingForOpeningVideo = true; }
        public void OpeningVideoStarted()
        {
            if (!waitingForOpeningVideo || Phase != StoryPhase.Opening) return;
            waitingForOpeningVideo = false;
            remainingSeconds = Mathf.Max(0, openingSeconds);
        }
        private bool waitingForOutcomeVideo;
        private int outcomeVideoRequestId;

        public bool HoldOutcomeForVideo(int id)
        {
            if (Phase != StoryPhase.WaitingForNextScene || id != requestId) return false;
            waitingForOutcomeVideo = true;
            outcomeVideoRequestId = id;
            return true;
        }

        public bool OutcomeVideoStarted(int id, float seconds)
        {
            if (!waitingForOutcomeVideo || Phase != StoryPhase.WaitingForNextScene || id != outcomeVideoRequestId) return false;
            remainingSeconds = Mathf.Max(0, seconds);
            waitingForOutcomeVideo = false;
            return true;
        }
        private int requestId;
        public bool FuelChallengeActive { get; private set; }
        public bool FuelThrowPending { get; private set; }
        public int FuelThrowsLeft { get; private set; }
        public int FuelHits { get; private set; }
        public int FuelScoreChange { get; private set; }
        private bool fuelCompleted;
        private bool fuelResultPresented;
        public event Action<string> FuelResultVideoRequested;
        public bool PresentFuelResultVideo(string prompt)
        {
            if (!FuelChallengeActive || FuelThrowPending || FuelThrowsLeft != 0 || fuelResultPresented) return false;
            fuelResultPresented = true;
            if (!string.IsNullOrWhiteSpace(prompt)) FuelResultVideoRequested?.Invoke(prompt);
            return CompleteFuelChallenge(true);
        }

        private int fuelAttempts;
        public int ThrowsUsed => fuelAttempts - FuelThrowsLeft;

        public bool BeginFuelChallenge(int availableThrows = 3)
        {
            if (Checkpoint != 3 || Phase != StoryPhase.AwaitingChoice || fuelCompleted) return false;
            if (FuelChallengeActive) return true;
            FuelChallengeActive = true;
            FuelThrowsLeft = fuelAttempts = Mathf.Clamp(availableThrows, 0, 3);
            FuelHits = FuelScoreChange = 0;
            FuelThrowPending = false;
            StateChanged.Invoke();
            return true;
        }
        public bool TryBeginFuelThrow()
        {
            if (!FuelChallengeActive || FuelThrowPending || FuelThrowsLeft <= 0) return false;
            FuelThrowsLeft--;
            FuelThrowPending = true;
            StateChanged.Invoke();
            return true;
        }
        public bool ResolveFuelThrow(bool hit, int hitPoints, int missPoints)
        {
            if (!FuelChallengeActive || !FuelThrowPending) return false;
            FuelThrowPending = false;
            if (hit) FuelHits++;
            int delta = hit ? Mathf.Clamp(hitPoints, 0, 100) : -Mathf.Clamp(missPoints, 0, 100);
            int before = Wellness;
            Wellness = Mathf.Clamp(Wellness + delta, 0, 100);
            FuelScoreChange += Wellness - before; // Record the actual change after the 0–100 cap.
            StateChanged.Invoke();
            if (Wellness != before) WellnessChanged.Invoke(Wellness, Wellness - before);
            return true;
        }
        public bool CompleteFuelChallenge(bool waitForContinue = false)
        {
            if (!FuelChallengeActive || FuelThrowPending || FuelThrowsLeft != 0) return false;
            FuelChallengeActive = false;
            fuelCompleted = true;
            Decisions++; // Launchpad replaces the checkpoint's ordinary Director choice.
            Phase = StoryPhase.WaitingForNextScene;
            // The launch into low orbit plays live on Visko while the Director writes checkpoint 4.
            int full = Mathf.Max(1, fuelAttempts);
            string launchLine = FuelHits >= full ? "Full tanks. The rocket tears free of the burning sky."
                : FuelHits > 0 ? "Half-fueled, the rocket groans and shudders up into orbit."
                : "Running on fumes, you barely clear the atmosphere.";
            string launchShot = FuelHits >= full
                ? "First-person view from a rocket cockpit as it blasts off the burning launch pad, punching cleanly through smoke and ash clouds until the sky turns black and the curve of a glowing, burning Earth appears below."
                : FuelHits > 0
                ? "First-person view from a shaking rocket cockpit as it lifts off the burning launch pad, warning lights flashing, rattling through smoke and ash until the sky darkens into low orbit above a burning Earth."
                : "First-person view from a violently shaking rocket cockpit, red alarms flashing, the engines sputtering as it claws up through thick smoke and barely breaks into the black of low orbit above a burning Earth.";
            var outcome = new StoryChoice {
                label = $"Fuel the rocket: {FuelHits}/{full} hits",
                outcome_narration = launchLine,
                outcome_video_prompt = launchShot,
                wellness_change = FuelScoreChange
            };
            history.Add(new StoryHistoryEntry { location = CurrentScene.location, situation = CurrentScene.narration,
                picked = outcome.label, outcome = outcome.outcome_narration,
                wellness_change = FuelScoreChange, wellness_after = Wellness, checkpoint = 3, kind = "neutral" });
            LastChoice = outcome;
            pendingScene = null;
            remainingSeconds = waitForContinue ? 0 : Mathf.Max(0, outcomeSeconds);
            requireNextContinue = waitForContinue;
            nextContinuePressed = false;
            int id = ++requestId;
            StateChanged.Invoke();
            OutcomePresented.Invoke(outcome);
            if (Phase == StoryPhase.WaitingForNextScene && requestId == id) NextSceneRequested.Invoke(id);
            return true;
        }
#if UNITY_EDITOR
        /// <summary>Inspector testing only: resolve a synthetic result and keep the real Director/video path.</summary>
        public bool AdvanceForEditorPreview()
        {
            if (Checkpoint < 1 || Phase == StoryPhase.Ended) return false;
            if (Phase == StoryPhase.Opening) AdvanceOpening();
            if (Phase == StoryPhase.AwaitingChoice)
            {
                if (Checkpoint == 3)
                {
                    if (!FuelChallengeActive && !BeginFuelChallenge(Mathf.Min(3, inventory.Count))) return false;
                    if (FuelThrowPending) ResolveFuelThrow(true, 5, 5);
                    while (FuelThrowsLeft > 0)
                    {
                        if (!TryBeginFuelThrow()) return false;
                        ResolveFuelThrow(true, 5, 5);
                    }
                    if (!PresentFuelResultVideo(null)) return false;
                }
                else
                {
                    bool chosen = false;
                    for (int i = 0; i < CurrentScene.choices.Length; i++)
                        if (CanChoose(i)) { chosen = TryChoose(i); break; }
                    if (!chosen) return false;
                }
            }
            if (Phase != StoryPhase.WaitingForNextScene) return false;
            if (LastError != null) RetryNextScene();
            // Treat the Inspector click as Continue, but never bypass next-video readiness.
            remainingSeconds = 0;
            waitingForOutcomeVideo = false;
            nextContinuePressed = true;
            return true;
        }
#endif

        // Explicit developer command only: seed a temporary launchpad run.
        public void PrepareFuelChallengePreview()
        {
            ResetStory();
            foreach (var item in availableItems) { if (CanStart) break; TrySelectItem(item); }
            Roommate = "came along";
            Decisions = 1;
            Present(new StoryScene { location = Checkpoints[2], narration = "Fill the fuel!", transition = "cut",
                video_prompt = "First-person wide view of an emergency rocket launch pad, towering evacuation rockets surrounded by smoke and pulsing warning lights. Crews hurry between fueling equipment as ash drifts across the scorched ground beneath a blood-orange sky.", choices = Array.Empty<StoryChoice>() });
        }

        public StoryPhase Phase { get; private set; } = StoryPhase.SelectingItems;
        public IReadOnlyList<StoryItemDefinition> AvailableItems => Array.AsReadOnly(availableItems);
        public IReadOnlyList<StoryItemDefinition> Inventory => inventory.AsReadOnly();
        /// <summary>Every pick so far, with its wellness cost. Read-only view for reports and UI.</summary>
        public IReadOnlyList<StoryHistoryEntry> History => history.AsReadOnly();
        public StoryScene CurrentScene { get; private set; }
        public StoryChoice LastChoice { get; private set; }
        public int LastChoiceIndex { get; private set; } = -1;
        public int DistanceRemaining { get; private set; } = StartDistance;
        public int Wellness { get; private set; } = 100;
        public int Decisions { get; private set; }
        public string Roommate { get; private set; }
        /// <summary>Current checkpoint, 1-6, or 0 before the story starts.</summary>
        public int Checkpoint { get; private set; }
        public int RequiredItemCount => requiredItemCount;
        public int PendingRequestId => requestId;
        public bool CanStart => Phase == StoryPhase.SelectingItems && inventory.Count == requiredItemCount;
        public string LastError { get; private set; }

        private void OnEnable() => ResetStory();
        private void OnDisable() => ResetStory();

        private void Update()
        {
            if (Phase != StoryPhase.Opening && Phase != StoryPhase.WaitingForNextScene) return;
            if (waitingForOutcomeVideo || waitingForOpeningVideo || videoPlaybackPaused) return;
            remainingSeconds -= Time.unscaledDeltaTime;
            if (remainingSeconds > 0) return;
            if (Phase == StoryPhase.Opening) AdvanceOpening();
            else if (pendingScene != null && !waitingForNextVideo && (!requireNextContinue || nextContinuePressed))
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

        /// <summary>Read authored Stage 1 media for preloading without selecting items or advancing story state.</summary>
        public StoryScene GetStage1PreviewScene()
        {
            try
            {
                var scenes = openingScenes == null ? null :
                    JsonConvert.DeserializeObject<StoryOpeningScenes>(openingScenes.text);
                return scenes != null && IsValidScene(scenes.roommate, false) && !scenes.roommate.is_ending
                    ? scenes.roommate : null;
            }
            catch (JsonException) { return null; }
        }

        public bool TryStartStory() => TryStartStory(false);

        /// <summary>allowFewerItems starts with whatever is selected (e.g. when the grab timer runs out).</summary>
        public bool TryStartStory(bool allowFewerItems)
        {
            if (!isActiveAndEnabled || Phase != StoryPhase.SelectingItems ||
                (!allowFewerItems && inventory.Count != requiredItemCount)) return false;
            try
            {
                fixedScenes = openingScenes == null ? null :
                    JsonConvert.DeserializeObject<StoryOpeningScenes>(openingScenes.text);
                if (fixedScenes == null ||
                    !IsValidScene(fixedScenes.roommate, false) ||
                    fixedScenes.roommate.is_ending)
                    return Fail("Assign valid OpeningScenes.json before starting the story.");
            }
            catch (JsonException e) { return Fail("Invalid opening scene JSON: " + e.Message); }

            LastError = null;
            Phase = StoryPhase.AwaitingChoice;
            remainingSeconds = 0;
            CurrentScene = fixedScenes.roommate;
            CurrentScene.transition = "cut";
            Checkpoint = 1;
            StateChanged.Invoke();
            CheckpointEntered.Invoke(Checkpoint);
            ScenePresented.Invoke(CurrentScene);
            return true;
        }

        public void AdvanceOpening()
        {
            if (Phase == StoryPhase.Opening) Present(fixedScenes.roommate);
        }

        public bool CanChoose(int index)
        {
            if (Checkpoint == 3 || Phase != StoryPhase.AwaitingChoice || CurrentScene == null ||
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
            int wellnessBefore = Wellness;
            Wellness = (int)Math.Max(0L, Math.Min(100L, (long)Wellness + choice.wellness_change));
            if (!string.IsNullOrWhiteSpace(choice.uses_item)) inventory.RemoveAt(FindItem(choice.uses_item));
            bool dormPick = Checkpoint < 2;
            if (!string.IsNullOrWhiteSpace(choice.roommate)) Roommate = choice.roommate;
            if (!dormPick) Decisions++;
            history.Add(new StoryHistoryEntry
            {

                location = CurrentScene.location, situation = CurrentScene.narration,
                picked = choice.label, outcome = choice.outcome_narration,
                wellness_change = Wellness - wellnessBefore, wellness_after = Wellness,
                checkpoint = Checkpoint, kind = NormalizeKind(choice.kind)
            });
            if (Wellness != wellnessBefore) WellnessChanged.Invoke(Wellness, Wellness - wellnessBefore);
            LastChoice = choice;
            LastChoiceIndex = index;
            LastError = null;
            pendingScene = null;
            remainingSeconds = Mathf.Max(0, outcomeSeconds);
            int id = ++requestId;
            StateChanged.Invoke();
            OutcomePresented.Invoke(choice);
            if (Phase == StoryPhase.WaitingForNextScene && requestId == id) NextSceneRequested.Invoke(id);
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
            NextScenePrepared?.Invoke(id, pendingScene);
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

        public StoryDirectorInput GetDirectorInput() =>
            BuildDirectorInput(Decisions, DistanceRemaining, Wellness, Roommate,
                inventory.ConvertAll(item => item.DirectorName), history, CurrentScene?.location);

        /// <summary>
        /// The Director input as it would be after picking choice `index` of the current scene, without
        /// applying it. Lets the Director write both branches while the player is still deciding.
        /// </summary>
        public StoryDirectorInput GetDirectorInputFor(int index)
        {
            if (Phase != StoryPhase.AwaitingChoice || CurrentScene == null ||
                index < 0 || index >= CurrentScene.choices.Length) return null;
            StoryChoice choice = CurrentScene.choices[index];
            var items = inventory.ConvertAll(item => item.DirectorName);
            if (!string.IsNullOrWhiteSpace(choice.uses_item))
                items.RemoveAll(name => string.Equals(name, choice.uses_item, StringComparison.OrdinalIgnoreCase));
            var nextHistory = new List<StoryHistoryEntry>(history)
            {
                new StoryHistoryEntry
                {
                    location = CurrentScene.location, situation = CurrentScene.narration,
                    picked = choice.label, outcome = choice.outcome_narration,
                    checkpoint = Checkpoint, kind = NormalizeKind(choice.kind)
                }
            };
            bool dormPick = Checkpoint < 2;
            bool roommateChange = !string.IsNullOrWhiteSpace(choice.roommate);
            int wellnessAfter = (int)Math.Max(0L, Math.Min(100L, (long)Wellness + choice.wellness_change));
            nextHistory[nextHistory.Count - 1].wellness_change = wellnessAfter - Wellness;
            nextHistory[nextHistory.Count - 1].wellness_after = wellnessAfter;
            return BuildDirectorInput(dormPick ? Decisions : Decisions + 1,
                Math.Max(0, DistanceRemaining - Math.Max(0, choice.miles_gained)),
                wellnessAfter,
                roommateChange ? choice.roommate : Roommate, items, nextHistory, CurrentScene.location);
        }

        private StoryDirectorInput BuildDirectorInput(int decisions, int distance, int wellness, string roommate,
            List<string> items, List<StoryHistoryEntry> pastChoices, string previousLocation)
        {
            // Each Director decision moves one checkpoint: the next beat is at checkpoint decisions + 2.
            int number = Mathf.Clamp(decisions + 2, 2, EndingCheckpoint);
            int left = Mathf.Max(0, Mathf.Min(maxDecisions, DirectorBeats) - decisions);
            bool mustEnd = number == EndingCheckpoint || left == 0;
            if (mustEnd) number = EndingCheckpoint;
            string here = Checkpoints[number - 1];
            string next = number < EndingCheckpoint ? Checkpoints[number] : null;
            return new StoryDirectorInput
            {
                decision_number = decisions + 1, decisions_left = left,
                must_end = mustEnd, verdict_passed = mustEnd && wellness >= PassWellness,
                checkpoint_number = number, checkpoint = here, next_checkpoint = next,
                stage = next == null ? "land on the Moon" : "arrive at " + here + ", then leave it for " + next,
                distance_remaining_miles = distance, wellness_percent = wellness,
                inventory = items.ToArray(),
                roommate = roommate, previous_location = previousLocation,
                history = pastChoices.ConvertAll(entry => new StoryHistoryEntry
                {
                    location = entry.location, situation = entry.situation,
                    picked = entry.picked, outcome = entry.outcome,
                    wellness_change = entry.wellness_change, wellness_after = entry.wellness_after,
                    checkpoint = entry.checkpoint, kind = entry.kind
                }).ToArray()
            };
        }

        public void ResetStory()
        {
            waitingForOpeningVideo = false;
            waitingForNextVideo = false;
            requireNextContinue = nextContinuePressed = false;
            waitingForOutcomeVideo = false;
            videoPlaybackPaused = false;
            ++requestId;
            FuelChallengeActive = FuelThrowPending = fuelCompleted = fuelResultPresented = false;
            FuelThrowsLeft = FuelHits = FuelScoreChange = fuelAttempts = 0;
            pendingScene = null;
            fixedScenes = null;
            inventory.Clear();
            history.Clear();
            CurrentScene = null;
            LastChoice = null;
            LastChoiceIndex = -1;
            LastError = null;
            DistanceRemaining = StartDistance;
            Wellness = 100;
            Decisions = 0;
            Roommate = null;
            Checkpoint = 0;
            Phase = StoryPhase.SelectingItems;
            StateChanged.Invoke();
        }

        private void Present(StoryScene scene)
        {
            requireNextContinue = nextContinuePressed = false;
            CurrentScene = scene;
            Phase = scene.is_ending ? StoryPhase.Ended : StoryPhase.AwaitingChoice;
            // Fixed dorm scenes (before the roommate pick) are checkpoint 1; Director beats match GetDirectorInput.
            int checkpoint = scene.is_ending ? EndingCheckpoint :
                Roommate == null ? 1 : Mathf.Clamp(Decisions + 2, 2, EndingCheckpoint - 1);
            bool entered = checkpoint != Checkpoint;
            Checkpoint = checkpoint;
            StateChanged.Invoke();
            if (entered) CheckpointEntered.Invoke(checkpoint);
            ScenePresented.Invoke(scene);
            if (scene.is_ending) VerdictReady.Invoke(GetVerdict());
        }

        private static string NormalizeKind(string kind)
        {
            string k = kind?.Trim().ToLowerInvariant();
            return k == "good" || k == "bad" ? k : "neutral";
        }

        /// <summary>
        /// Moon Citizen (wellness >= PassWellness) with a traveller type, or Moon Reject. Types come from
        /// the caring ("good") / selfish ("bad") picks; first match wins.
        /// </summary>
        public StoryVerdict GetVerdict()
        {
            var verdict = new StoryVerdict { passed = Wellness >= PassWellness, wellness = Wellness };
            verdict.title = verdict.passed ? "Moon Citizen" : "Moon Reject";
            if (!verdict.passed) return verdict;

            StoryHistoryEntry At(int checkpoint) => history.FindLast(entry => entry.checkpoint == checkpoint);
            bool Good(int checkpoint) => At(checkpoint)?.kind == "good";
            bool Bad(int checkpoint) => At(checkpoint)?.kind == "bad";
            bool anyBad = history.Exists(entry => entry.kind == "bad");

            if (!anyBad)
                (verdict.archetype, verdict.archetype_line) = ("The Guardian",
                    "You never put yourself first. Everyone you met made it because of you.");
            else if (Bad(1) && (Good(4) || Good(5)))
                (verdict.archetype, verdict.archetype_line) = ("The Redeemed",
                    "You left someone behind in the first minute and spent the rest of the journey making it right.");
            else if (Good(1) && Good(5) && Bad(4))
                (verdict.archetype, verdict.archetype_line) = ("The Loyal One",
                    "You would do anything for your own people. Strangers had to earn it.");
            else if (fuelCompleted && FuelHits >= 3)
                (verdict.archetype, verdict.archetype_line) = ("The Ace Pilot",
                    "Steady hands, full tanks. Nothing on the way caught you off guard.");
            else
                (verdict.archetype, verdict.archetype_line) = ("The Survivor",
                    "You weren't perfect, and you knew it. You did enough good to be let in.");
            return verdict;
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
