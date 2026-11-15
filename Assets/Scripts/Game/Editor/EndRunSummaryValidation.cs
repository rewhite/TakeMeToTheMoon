using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TakeMeToTheMoon.Story;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class EndRunSummaryValidation
    {
        [MenuItem("Tools/Game Flow/Validate End Summary")]
        public static void Validate()
        {
            void Check(bool value,string name){if(!value)throw new Exception("End summary: "+name);}
            var go=new GameObject("End summary validation");go.SetActive(false);
            var prefab=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Game Flow/End Screen.prefab");
            try
            {
                var story=go.AddComponent<StoryController>();
                var history=(List<StoryHistoryEntry>)typeof(StoryController).GetField("history",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(story);
                void Seed(params string[] kinds)
                {
                    history.Clear();
                    for(int i=0;i<kinds.Length;i++) history.Add(new StoryHistoryEntry{checkpoint=i+1,kind=kinds[i],picked="Test choice "+(i+1)});
                }
                Seed("good","neutral","neutral","good","good");
                Check(story.GetVerdict().archetype=="The Guardian","Guardian");
                Seed("bad","neutral","neutral","good","bad");
                Check(story.GetVerdict().archetype=="The Redeemed","remote Redeemed includes checkpoint 4");
                Seed("good","neutral","neutral","bad","good");
                Check(story.GetVerdict().archetype=="The Loyal One","Loyal");
                Seed("good","bad","neutral","neutral","good");
                Check(story.GetVerdict().archetype=="The Survivor","checkpoint 2 alone does not make Loyal");
                story.PrepareFuelChallengePreview();story.BeginFuelChallenge();
                for(int i=0;i<3;i++){story.TryBeginFuelThrow();story.ResolveFuelThrow(true,5,5);}
                story.CompleteFuelChallenge();
                Check(story.ThrowsUsed==3,"throw counter");
                Check(story.History.Any(h=>h.checkpoint==3 && h.picked=="Fuel the rocket: 3/3 hits"),"remote fuel history");
                Seed("good","bad","neutral","bad","bad");
                Check(story.GetVerdict().archetype=="The Ace Pilot","Ace Pilot");
                Seed("bad","neutral","neutral","good","bad");
                Check(story.GetVerdict().archetype=="The Redeemed","Redeemed before Ace Pilot");
                Seed("good","neutral","neutral","good","good");
                Check(story.GetVerdict().archetype=="The Guardian","Guardian before Ace Pilot");
                typeof(StoryController).GetProperty("Wellness").SetValue(story,85);
                Check(story.GetVerdict().passed,"85 passes");
                var summary=prefab.GetComponent<EndRunSummary>();
                foreach(string type in new[]{"The Guardian","The Redeemed","The Loyal One","The Ace Pilot","The Survivor"})
                {
                    summary.Show(new StoryVerdict{passed=true,wellness=85,archetype=type,archetype_line="Remote fixed line"},3,story.History.ToArray());
                    var fields=new SerializedObject(summary);
                    var portrait=(UnityEngine.UI.Image)fields.FindProperty("portrait").objectReferenceValue;
                    Check(portrait.enabled==(type!="The Redeemed" && type!="The Ace Pilot"),"correct card or text fallback: "+type);
                }
                typeof(StoryController).GetProperty("Wellness").SetValue(story,84);
                Check(!story.GetVerdict().passed && story.GetVerdict().archetype==null,"below 85 no type");
                summary.Show(story.GetVerdict(),3,story.History.ToArray());
                Check(prefab.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="Moon Reject"),"failure title");
                story.ResetStory();
                Check(story.ThrowsUsed==0 && story.History.Count==0,"replay resets summary");
                Debug.Log("[EndSummary] PASSED: remote archetypes, priority, threshold, history, throws, card fallbacks and reset.");
            }
            finally {PrefabUtility.UnloadPrefabContents(prefab);UnityEngine.Object.DestroyImmediate(go);}
        }
    }
}
