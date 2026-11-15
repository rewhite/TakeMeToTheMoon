using System.Collections.Generic;
using System.Linq;

namespace TakeMeToTheMoon.Story
{
    public static class TravelerArchetypes
    {
        public static readonly string[] Names = { "The Guardian", "The Redeemed", "The Loyal One", "The Prepared", "The Survivor" };
        public static readonly string[] Blurbs = {
            "You never put yourself first. Everyone you met made it because of you.",
            "You made a mistake in the first minute and spent the rest of the journey making it right.",
            "You would do anything for your own people. Strangers had to earn it.",
            "You packed for the worst and it came. Nothing on the way caught you off guard.",
            "You weren't perfect, and you knew it. You did enough good to be let in."
        };
        public static string Classify(int wellness, bool wokeKyle, bool finalGenerous,
            int rightItems, IEnumerable<StoryHistoryEntry> history)
        {
            if (wellness < 85) return null;
            var bad = history.Where(h => h.verdict == "bad").ToArray();
            if (bad.Length == 0) return Names[0];
            if (!wokeKyle && finalGenerous) return Names[1];
            if (wokeKyle && finalGenerous && bad.Any(h => h.checkpoint == 2 || h.checkpoint == 4 || h.checkpoint == 5)) return Names[2];
            if (rightItems == 3) return Names[3];
            return Names[4];
        }
        public static string Blurb(string name)
        {
            int index = System.Array.IndexOf(Names, name);
            return index < 0 ? "" : Blurbs[index];
        }
    }
}
