using UnityEngine;
using UnityEngine.Rendering;

namespace TakeMeToTheMoon.Video
{
    /// <summary>Low-resolution video color spill on a horizontal floor, without reflection cameras or CPU readback.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(CurvedVideoScreen))]
    public sealed class VideoFloorGlow : MonoBehaviour
    {
        [Header("Appearance")]
        [Range(0f, 2f)] public float intensity = .55f;
        [Min(.1f)] public float widthMultiplier = 1.35f;
        [Min(.1f)] public float spreadDistance = 3f;
        [Range(1f, 6f)] public float blur = 3f;
        [Header("Floor (world metres)")]
        public float floorHeight;
        [Min(.001f)] public float surfaceOffset = .015f;
        [Tooltip("Distance towards the viewer from the screen's floor projection.")]
        public float forwardOffset;
        [Header("Performance")]
        [Range(1, 30)] public int updatesPerSecond = 15;
        [Header("Rendering assets")]
        public Material glowMaterial;
        public Shader blurShader;

        CurvedVideoScreen _screen;
        GameObject _floor;
        Mesh _mesh;
        MeshRenderer _renderer;
        Material _glow, _blur;
        Material _lastMaterial;
        Shader _lastShader;
        RenderTexture _small, _temporary;
        Texture _lastTexture;
        double _nextUpdate;
        float _lastBlur;

        void OnEnable() { _nextUpdate = 0; }
        void LateUpdate()
        {
            if (_screen == null) _screen = GetComponent<CurvedVideoScreen>();
            var texture = _screen.sourceTexture;
            bool visible = _screen.isActiveAndEnabled && texture != null && intensity > 0
                && glowMaterial != null && blurShader != null;
            if (!visible)
            {
                if (_renderer != null) _renderer.enabled = false;
                _lastTexture = null;
                return;
            }
            if (_lastMaterial != glowMaterial || _lastShader != blurShader) ReleaseResources();
            if (_floor == null) CreateResources();
            _renderer.enabled = true;
            _floor.layer = gameObject.layer;
            // Keep the patch horizontal even when the screen is tilted or grabbed.
            Vector3 viewer = Vector3.ProjectOnPlane(-transform.forward, Vector3.up);
            if (viewer.sqrMagnitude < .0001f) viewer = Vector3.back;
            viewer.Normalize();
            float distance = Mathf.Max(.1f, spreadDistance);
            Vector3 position = transform.position + viewer * (distance * .5f + forwardOffset);
            position.y = floorHeight + Mathf.Max(.001f, surfaceOffset);
            _floor.transform.SetPositionAndRotation(position, Quaternion.LookRotation(viewer, Vector3.up));
            float surfaceWidth = Mathf.Clamp(_screen.width, .1f, Mathf.Max(.1f, _screen.radius) * Mathf.PI * 1.9f);
            _floor.transform.localScale = new Vector3(surfaceWidth * Mathf.Abs(transform.lossyScale.x)
                * Mathf.Max(.1f, widthMultiplier), 1, distance);
            _glow.SetFloat("_Intensity", Mathf.Clamp(intensity, 0, 2));
            _glow.SetFloat("_FlipVertical", _screen.flipVertical ? 1 : 0);
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < _nextUpdate && texture == _lastTexture && blur == _lastBlur) return;
            // Video textures change contents without changing object identity.
            var previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false; // The intermediate textures store linear color.
                Graphics.Blit(texture, _small, _blur, 0);
                _blur.SetVector("_Direction", new Vector4(Mathf.Clamp(blur, 1, 6), 0, 0, 0));
                Graphics.Blit(_small, _temporary, _blur, 1);
                _blur.SetVector("_Direction", new Vector4(0, Mathf.Clamp(blur, 1, 6), 0, 0));
                Graphics.Blit(_temporary, _small, _blur, 1);
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = previousSrgb; }
            _nextUpdate = now + 1.0 / Mathf.Clamp(updatesPerSecond, 1, 30);
            _lastTexture = texture; _lastBlur = blur;
        }

        void CreateResources()
        {
            _lastMaterial = glowMaterial; _lastShader = blurShader;
            _glow = new Material(glowMaterial) { hideFlags = HideFlags.HideAndDontSave };
            _blur = new Material(blurShader) { hideFlags = HideFlags.HideAndDontSave };
            _small = CreateTexture("Video ambient colors");
            _temporary = CreateTexture("Video ambient blur temporary");
            _glow.SetTexture("_MainTex", _small);
            _mesh = new Mesh { name = "Video Floor Glow Quad", hideFlags = HideFlags.HideAndDontSave };
            _mesh.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f),
                new Vector3(.5f, 0, -.5f), new Vector3(.5f, 0, .5f) };
            // Viewer-facing screen-left is floor-local +X because this patch faces backwards.
            _mesh.uv = new[] { new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 0), new Vector2(0, 1) };
            _mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            _mesh.RecalculateBounds();
            _floor = new GameObject("Video Floor Glow (generated)") { hideFlags = HideFlags.HideAndDontSave };
            _floor.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _floor.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _glow;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _nextUpdate = 0;
        }

        static RenderTexture CreateTexture(string textureName)
        {
            var texture = new RenderTexture(64, 36, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = textureName, hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                useMipMap = false, autoGenerateMips = false
            };
            texture.Create();
            return texture;
        }

        void OnDisable() { ReleaseResources(); }
        void OnDestroy() { ReleaseResources(); }
        void ReleaseResources()
        {
            if (_renderer != null) _renderer.enabled = false;
            DisposeObject(_floor); DisposeObject(_mesh); DisposeObject(_glow); DisposeObject(_blur);
            if (_small != null) { _small.Release(); DisposeObject(_small); }
            if (_temporary != null) { _temporary.Release(); DisposeObject(_temporary); }
            _floor = null; _mesh = null; _glow = _blur = null; _small = _temporary = null;
            _renderer = null; _lastTexture = null;
        }

        static void DisposeObject(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }
    }
}
