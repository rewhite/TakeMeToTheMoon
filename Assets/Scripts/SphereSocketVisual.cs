using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Visible docking guide; Meta's Snap components own grabbing and snapping.</summary>
[ExecuteAlways]
[RequireComponent(typeof(SnapInteractable))]
public sealed class SphereSocketVisual : MonoBehaviour
{
    [SerializeField] private SnapInteractable socket;
    [SerializeField] private Material guideMaterial;
    [SerializeField, Min(0.01f)] private float sphereRadius = 0.1f;
    [SerializeField] private Color idleColor = new Color(0.1f, 0.65f, 1f);
    [SerializeField] private Color readyColor = new Color(1f, 0.7f, 0.1f);
    [SerializeField] private Color occupiedColor = new Color(0.15f, 1f, 0.45f);

    private GameObject visuals;
    private Material material;
    private LineRenderer[] rings;
    private MaterialPropertyBlock properties;

    private void OnEnable()
    {
        if (socket == null) socket = GetComponent<SnapInteractable>();
        CreateVisuals();
    }

    private void CreateVisuals()
    {
        if (visuals != null || guideMaterial == null) return;
        material = new Material(guideMaterial) { hideFlags = HideFlags.HideAndDontSave };
        material.EnableKeyword("_EMISSION");
        visuals = new GameObject("Socket Guide (generated)") { hideFlags = HideFlags.HideAndDontSave };
        visuals.transform.SetParent(transform, false);
        rings = new LineRenderer[4];
        properties = new MaterialPropertyBlock();
        for (int ring = 0; ring < rings.Length; ring++)
        {
            var child = new GameObject("Guide Ring " + ring) { hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(visuals.transform, false);
            var line = child.AddComponent<LineRenderer>();
            rings[ring] = line;
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 64;
            line.widthMultiplier = ring == 3 ? 0.009f : 0.003f;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.generateLightingData = true;
        }
    }

    private void Update()
    {
        CreateVisuals();
        if (visuals == null) return;
        var state = Application.isPlaying && socket != null ? socket.State : InteractableState.Normal;
        Color color = state == InteractableState.Select ? occupiedColor
            : state == InteractableState.Hover ? readyColor : idleColor;
        properties.SetColor("_BaseColor", color);
        properties.SetColor("_Color", color);
        properties.SetColor("_EmissionColor", color);
        for (int ring = 0; ring < rings.Length; ring++)
        {
            var line = rings[ring];
            line.SetPropertyBlock(properties);
            // Keep the base visible once the sphere fills the outline.
            line.enabled = ring == 3 || state != InteractableState.Select;
            for (int point = 0; point < line.positionCount; point++)
            {
                float angle = point * Mathf.PI * 2f / line.positionCount;
                float radius = sphereRadius * (ring == 3 ? 1.25f : 1.04f);
                float x = Mathf.Cos(angle) * radius;
                float y = Mathf.Sin(angle) * radius;
                Vector3 position = ring == 0 ? new Vector3(x, y, 0)
                    : ring == 1 ? new Vector3(0, x, y)
                    : new Vector3(x, ring == 3 ? -sphereRadius : 0, y);
                line.SetPosition(point, position);
            }
        }
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            Destroy(visuals);
            Destroy(material);
        }
        else
        {
            DestroyImmediate(visuals);
            DestroyImmediate(material);
        }
        visuals = null;
        material = null;
        rings = null;
    }
}
