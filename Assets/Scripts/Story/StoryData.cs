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
    }

    [Serializable]
    public sealed class StoryHistoryEntry
    {
        public string location;
        public string situation;
        public string picked;
        public string outcome;
    }

    [Serializable]
    public sealed class StoryDirectorInput
    {
        public int decision_number;
        public int decisions_left;
        public bool must_end;
        public int checkpoint_number;
        public string checkpoint;
        public string next_checkpoint;
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
