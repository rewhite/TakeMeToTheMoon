using System.Collections.Generic;
using Oculus.Interaction;
using TakeMeToTheMoon.Story;
using TakeMeToTheMoon.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.Game
{
    public sealed class FuelThrowGame : MonoBehaviour
    {
        public StoryController story;
        public GameManager manager;
        public PrologueEmergencyPulse resultFlash;
        public FuelResultToast resultToast;
        public FuelThrowItem[] items;
        public FuelPhysicsTarget[] targets;
        public Transform[] itemPositions;
        public FuelInstructionPanel instructions;
        public TMP_Text resultText;
        public Button continueButton;
        public AudioSource audioSource;
        public AudioClip startSound, hitSound, missSound;
        [Min(0)] public int hitPoints = 10, missPoints = 10;
        [Min(1)] public float flightTimeout = 5;
        private FuelThrowItem flying;
        private float deadline, hideAt;
        private readonly HashSet<FuelThrowItem> spentItems = new HashSet<FuelThrowItem>();
        private bool resolved, ready;
        private void OnEnable()
        {
            // Editor direct-stage preview only. Quest always uses actual carried inventory.
#if UNITY_EDITOR
            if (story.Checkpoint != 3 && manager.Phase == GamePhase.Main && manager.CurrentStageNumber == 3)
                story.PrepareFuelChallengePreview();
#endif
            PreparePanelForThrows();
            if (!story.FuelChallengeActive) spentItems.Clear();
            story.StateChanged.AddListener(Refresh);
            continueButton.onClick.AddListener(Continue);
            flying = null; resolved = false;
            foreach (var target in targets) target.ResetTarget();
            int count = 0;
            foreach (var item in items)
            {
                bool carried = item.definition != null && story.HasItem(item.definition) && !spentItems.Contains(item);
                item.gameObject.SetActive(carried);
                if (!carried) continue;
                item.home = itemPositions[count++ % itemPositions.Length];
                item.ReturnHome();
            }
            // The final preplaced projectile is a fallback when all story items were consumed.
            if (count == 0 && spentItems.Count == 0 && items.Length > 0)
            {
                var fallback = items[items.Length - 1];
                fallback.gameObject.SetActive(true); fallback.home = itemPositions[1]; fallback.ReturnHome(); count = 1;
            }
            ready = story.BeginFuelChallenge(count);
            resultText.text = ready ? "Throw an item at a target" : "Start the story at checkpoint 3 to play";
            Refresh();
            if (ready && startSound != null) audioSource.PlayOneShot(startSound);
        }
        private void OnDisable()
        {
            story.StateChanged.RemoveListener(Refresh);
            continueButton.onClick.RemoveListener(Continue);
            // Stage changes cancel an unresolved throw as a miss rather than leaving the run locked.
            if (flying != null && !resolved) story.ResolveFuelThrow(false, hitPoints, missPoints);
            if (flying != null) flying.gameObject.SetActive(false);
            foreach (var item in items) if (!spentItems.Contains(item)) item.ReturnHome();
            flying = null; ready = false;
            if (resultFlash != null) resultFlash.ClearResult();
            if (resultToast != null) resultToast.Clear();
            if (audioSource != null) audioSource.Stop();
        }
        public void PreparePanelForThrows()
        {
            if (instructions == null) return;
            MakeUiNonSolid(instructions.gameObject);
            var canvas = instructions.GetComponent<Canvas>();
            // Meta's interaction surface may be placed outside the visual canvas tree.
            foreach (var pointable in Object.FindObjectsByType<PointableCanvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (pointable.Canvas == canvas) MakeUiNonSolid(pointable.gameObject);
        }
        private static void MakeUiNonSolid(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                collider.isTrigger = true;
        }
        public void IgnorePanelCollisions(FuelThrowItem item)
        {
            // Trigger colliders remain raycastable, but cannot block projectiles.
            // Unlike per-pair IgnoreCollision, this survives collider reactivation.
            PreparePanelForThrows();
            if (item == null) return;
            int supportLayer = LayerMask.NameToLayer("FuelSupport");
            if (supportLayer < 0) throw new System.InvalidOperationException("FuelSupport layer is required");
            int supportMask = 1 << supportLayer;
            item.body.excludeLayers |= supportMask;
            var projectileColliders = item.GetComponentsInChildren<Collider>(true);
            foreach (var projectileCollider in projectileColliders)
                projectileCollider.excludeLayers |= supportMask;
            for (int i = 1; i <= targets.Length; i++)
            {
                var support = transform.Find("Invisible Support " + i);
                if (support == null) continue;
                foreach (var supportCollider in support.GetComponentsInChildren<Collider>(true))
                {
                    supportCollider.gameObject.layer = supportLayer;
                    foreach (var projectileCollider in projectileColliders)
                        Physics.IgnoreCollision(projectileCollider, supportCollider, true);
                }
            }
        }
        public bool BeginThrow(FuelThrowItem item)
        {
            if (!ready || flying != null || item == null || !item.gameObject.activeInHierarchy || spentItems.Contains(item) || !story.TryBeginFuelThrow()) return false;
            spentItems.Add(item);
            flying = item; resolved = false;
            foreach (var target in targets) target.BeginThrow();
            deadline = Time.time + flightTimeout;
            foreach (var prop in items) prop.SetPickable(false);
            instructions.SetThrowsLeft(story.FuelThrowsLeft);
            return true;
        }
        public bool TargetFell(FuelPhysicsTarget target, FuelThrowItem projectile)
        {
            if (projectile != flying || resolved || target.KnockedDown) return false;
            Resolve(true); return true;
        }
        private void Update()
        {
            if (flying == null) return;
            if (resolved)
            {
                if (Time.time < hideAt) return;
                flying.gameObject.SetActive(false); flying = null; Refresh(); return;
            }
            if (Time.time >= deadline || flying.transform.position.y < transform.position.y - 1.5f ||
                Vector3.Distance(flying.transform.position, transform.position) > 12) Resolve(false);
        }
        private void Resolve(bool hit)
        {
            if (resolved || !story.ResolveFuelThrow(hit, hitPoints, missPoints)) return;
            resolved = true; hideAt = Time.time + .8f;
            if (resultFlash != null) resultFlash.PlayResult(hit);
            if (resultToast != null) resultToast.Show(hit);
#if UNITY_EDITOR
            Debug.Log($"[FuelThrow] resolved hit={hit}, hits={story.FuelHits}, left={story.FuelThrowsLeft}");
#endif
            resultText.text = hit ? $"HIT! +{hitPoints}" : $"MISS -{missPoints}";
            var clip = hit ? hitSound : missSound;
            if (clip != null) audioSource.PlayOneShot(clip);
        }
        private void Refresh()
        {
            instructions.SetThrowsLeft(story.FuelThrowsLeft);
            bool complete = ready && story.FuelThrowsLeft == 0 && !story.FuelThrowPending;
            continueButton.gameObject.SetActive(complete && flying == null);
            if (complete && flying == null) resultText.text = $"{story.FuelHits}/3 targets • Wellness {story.Wellness}";
            foreach (var item in items) item.SetPickable(ready && flying == null && !complete && !spentItems.Contains(item));
        }
        public void Continue()
        {
            if (flying == null && story.CompleteFuelChallenge()) gameObject.SetActive(false);
        }
    }
}
