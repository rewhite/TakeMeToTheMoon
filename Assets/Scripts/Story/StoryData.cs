using System;

namespace TakeMeToTheMoon.Story
{
    // Field names intentionally match WebTest/README.md and director.js JSON.
    [Serializable]
    public sealed class StoryScene
    {
        public string location;
        public string transition;
        public string narration;
        // Optional: the journey from the previous checkpoint into this one, played before video_prompt.
        public string arrival_video_prompt;
        // Optional subtitle shown while the arrival plays.
        public string arrival_narration;
        public string video_prompt;
        public string audio_prompt;
        public StoryChoice[] choices = Array.Empty<StoryChoice>();
        public bool is_ending;
        public string ending_title;
    }

    [Serializable]
    public sealed class StoryChoice
    {
        public string label;
        public string outcome_narration;
        public string outcome_video_prompt;
        public int miles_gained;
        public int wellness_change;
        public string uses_item;
        // Only the fixed roommate choices use this optional extension.
        public string roommate;
        // "good" (caring), "bad" (selfish) or "neutral": drives the traveller type in the verdict.
        public string kind;
    }

    [Serializable]
    public sealed class StoryHistoryEntry
    {
        public string location;
        public string situation;
        public string picked;
        public string outcome;
        // Wellness cost of this pick and the wellness left after it (drives the ending and report).
        public int wellness_change;
        public int wellness_after;
        public int checkpoint;
        public string kind;
    }

    /// <summary>Final result for the end screen: Moon Citizen (passed) or Moon Reject.</summary>
    [Serializable]
    public sealed class StoryVerdict
    {
        public bool passed;
        public string title;          // "Moon Citizen" or "Moon Reject"
        public int wellness;
        public string archetype;      // Only when passed, e.g. "The Guardian"
        public string archetype_line; // One sentence describing that traveller
    }

    [Serializable]
    public sealed class StoryDirectorInput
    {
        public int decision_number;
        public int decisions_left;
        public bool must_end;
        // Only meaningful when must_end: whether the player is let in (wellness >= pass line).
        public bool verdict_passed;
        public int checkpoint_number;
        public string checkpoint;
        public string next_checkpoint;
        // Two different approach numbers (1-10, see the Director prompt) for the two choices.
        public int[] choice_approaches;
        public string stage;
        public int distance_remaining_miles;
        public int wellness_percent;
        public string[] inventory;
        public string roommate;
        public string previous_location;
        public StoryHistoryEntry[] history;
    }

    [Serializable]
    public sealed class StoryOpeningScenes
    {
        public StoryScene opening;
        public StoryScene roommate;
    }
}
