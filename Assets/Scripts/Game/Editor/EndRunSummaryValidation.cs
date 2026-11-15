using System;
using System.Linq;
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
            StoryHistoryEntry Bad(int checkpoint) => new StoryHistoryEntry { checkpoint=checkpoint, verdict="bad" };
            void Check(bool value,string name){if(!value)throw new Exception("End summary: "+name);}
            Check(TravelerArchetypes.Classify(85,true,true,3,Array.Empty<StoryHistoryEntry>())=="The Guardian","Guardian priority");
            Check(TravelerArchetypes.Classify(85,false,true,3,new[]{Bad(1)})=="The Redeemed","Redeemed priority");
            Check(TravelerArchetypes.Classify(85,true,true,3,new[]{Bad(2)})=="The Loyal One","Loyal priority");
            Check(TravelerArchetypes.Classify(85,true,false,3,new[]{Bad(4)})=="The Prepared","Prepared");
            Check(TravelerArchetypes.Classify(85,true,false,2,new[]{Bad(5)})=="The Survivor","Survivor");
            Check(TravelerArchetypes.Classify(84,true,true,3,Array.Empty<StoryHistoryEntry>())==null,"85 threshold");
            var go=new GameObject("End summary validation");go.SetActive(false);
            var prefab=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Game Flow/End Screen.prefab");
            try
            {
                var story=go.AddComponent<StoryController>();story.PrepareFuelChallengePreview();story.BeginFuelChallenge();
                for(int i=0;i<3;i++){story.TryBeginFuelThrow();story.ResolveFuelThrow(true,5,5);}
                story.CompleteFuelChallenge();
                Check(story.ThrowsUsed==3,"throw counter");
                Check(story.RightItemEmergencies==0,"hits are not item emergencies");
                Check(story.ChoiceHistory.Any(h=>h.checkpoint==3 && h.picked=="Fill the fuel: 3/3 targets"),"fuel history");
                story.RecordRightItemEmergency("heat");story.RecordRightItemEmergency("heat");
                Check(story.RightItemEmergencies==1,"emergency deduplication");
                var summary=prefab.GetComponent<EndRunSummary>();
                foreach(string type in TravelerArchetypes.Names)
                {
                    summary.Show(type,85,3,story.ChoiceHistory);
                    var fields=new SerializedObject(summary);
                    Check(((UnityEngine.UI.Image)fields.FindProperty("portrait").objectReferenceValue).sprite!=null,"portrait "+type);
                }
                summary.Show(null,84,3,story.ChoiceHistory);
                Check(prefab.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="Mission Failed"),"failure card");
                story.ResetStory();
                Check(story.ThrowsUsed==0 && story.RightItemEmergencies==0 && story.ChoiceHistory.Length==0 && !story.WokeKyle && !story.FinalKyleGenerous,"replay resets summary");
                Debug.Log("[EndSummary] PASSED: five archetypes, priority, threshold, portraits, history, throws, item separation and reset.");
            }
            finally {PrefabUtility.UnloadPrefabContents(prefab);UnityEngine.Object.DestroyImmediate(go);}
        }
    }
}
