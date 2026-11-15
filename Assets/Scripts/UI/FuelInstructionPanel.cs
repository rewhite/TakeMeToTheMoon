using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.UI
{
    /// <summary>Presentation only. The minigame supplies the remaining attempts.</summary>
    public sealed class FuelInstructionPanel : MonoBehaviour
    {
        [Header("Preview / Display")]
        [SerializeField, Range(0, 3)] private int throwsLeft = 3;
        [SerializeField] private TMP_Text counterText;
        [SerializeField] private Image[] fuelIcons;
        [SerializeField] private GameObject[] placeholderMarks;
        [Header("Replace Artwork")]
        [SerializeField] private Sprite panelArtwork;
        [SerializeField] private Sprite counterArtwork;
        [SerializeField] private Sprite fuelArtwork;
        [SerializeField] private Sprite usedFuelArtwork;
        [SerializeField] private Image panelBackground;
        [SerializeField] private Image counterBackground;
        public int ThrowsLeft => throwsLeft;
        private void OnEnable() => Refresh();
        private void OnValidate() => Refresh();
        public void SetThrowsLeft(int value) { throwsLeft = Mathf.Clamp(value, 0, 3); Refresh(); }
        private void Refresh()
        {
            throwsLeft = Mathf.Clamp(throwsLeft, 0, 3);
            if (counterText != null) counterText.text = $"{throwsLeft} / 3";
            SetArtwork(panelBackground, panelArtwork);
            SetArtwork(counterBackground, counterArtwork);
            if (fuelIcons == null) return;
            for (int i = 0; i < fuelIcons.Length; i++)
            {
                var icon = fuelIcons[i];
                if (icon == null) continue;
                bool remaining = i < throwsLeft;
                var artwork = !remaining && usedFuelArtwork != null ? usedFuelArtwork : fuelArtwork;
                icon.sprite = artwork;
                icon.preserveAspect = true;
                icon.color = artwork != null ? new Color(1, 1, 1, remaining || usedFuelArtwork != null ? 1 : .22f)
                    : new Color(1, .48f, .08f, remaining ? 1 : .22f);
                if (placeholderMarks != null && i < placeholderMarks.Length && placeholderMarks[i] != null)
                    placeholderMarks[i].SetActive(artwork == null);
            }
        }
        private static void SetArtwork(Image image, Sprite artwork)
        {
            if (image == null) return;
            image.overrideSprite = artwork;
            image.type = artwork != null ? Image.Type.Simple : Image.Type.Tiled;
        }
    }
}
