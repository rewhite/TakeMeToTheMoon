using UnityEngine;
using UnityEngine.Rendering;

namespace TakeMeToTheMoon.Video
{
    /// <summary>A cylindrical video surface. Local -Z faces the viewer; no Canvas or XR input is required.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class CurvedVideoScreen : MonoBehaviour
    {
        [Header("Shape (metres at scale 1)")]
        [Min(0.1f)] public float width = 3.2f;
        [Min(0.1f)] public float radius = 1.8f;
        [Min(0.1f)] public float aspectRatio = 832f / 480f;
        [Range(8, 256)] public int segments = 64;
        [Header("Video")]
        public Texture sourceTexture;
        public bool matchTextureAspect = true;
        public bool flipVertical;
        public Material screenMaterial;

        Mesh _mesh;
        MeshFilter _filter;
        MeshRenderer _renderer;
        MaterialPropertyBlock _properties;
        bool _dirty = true;
        Texture _lastTexture;
        float _lastWidth, _lastRadius, _lastAspect;
        int _lastSegments;
        bool _lastFlip;
        Material _lastMaterial;

        public MeshRenderer ScreenRenderer => GetComponent<MeshRenderer>();
        public float ArcDegrees => Mathf.Min(width / Mathf.Max(.1f, radius), Mathf.PI * 1.9f) * Mathf.Rad2Deg;

        public void SetTexture(Texture texture)
        {
            sourceTexture = texture;
            _dirty = true;
        }

        void OnEnable() { _dirty = true; Refresh(); }
        void OnValidate() { _dirty = true; } // Mesh operations run on the main thread in Update.
        void Update() { Refresh(); }

        void Refresh()
        {
            float safeRadius = Mathf.Max(.1f, radius);
            float safeWidth = Mathf.Clamp(width, .1f, safeRadius * Mathf.PI * 1.9f);
            float aspect = matchTextureAspect && sourceTexture != null && sourceTexture.height > 0
                ? (float)sourceTexture.width / sourceTexture.height : Mathf.Max(.1f, aspectRatio);
            int count = Mathf.Clamp(segments, 8, 256);
            bool shapeChanged = _mesh == null || safeWidth != _lastWidth || safeRadius != _lastRadius
                || aspect != _lastAspect || count != _lastSegments || flipVertical != _lastFlip;
            if (!_dirty && !shapeChanged && sourceTexture == _lastTexture && screenMaterial == _lastMaterial) return;
            if (_filter == null) _filter = GetComponent<MeshFilter>();
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            if (shapeChanged)
            {
                if (_mesh == null) _mesh = new Mesh { name = "Curved Video Surface", hideFlags = HideFlags.HideAndDontSave };
                var vertices = new Vector3[(count + 1) * 2];
                var uv = new Vector2[vertices.Length];
                var triangles = new int[count * 6];
                float halfHeight = safeWidth / aspect * .5f;
                for (int i = 0; i <= count; i++)
                {
                    float u = (float)i / count;
                    float angle = (u - .5f) * safeWidth / safeRadius;
                    float x = Mathf.Sin(angle) * safeRadius;
                    float z = (Mathf.Cos(angle) - 1) * safeRadius;
                    int v = i * 2;
                    vertices[v] = new Vector3(x, -halfHeight, z);
                    vertices[v + 1] = new Vector3(x, halfHeight, z);
                    uv[v] = new Vector2(u, flipVertical ? 1 : 0);
                    uv[v + 1] = new Vector2(u, flipVertical ? 0 : 1);
                    if (i == count) continue;
                    int t = i * 6;
                    triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                    triangles[t + 3] = v + 2; triangles[t + 4] = v + 1; triangles[t + 5] = v + 3;
                }
                _mesh.Clear();
                _mesh.vertices = vertices; _mesh.uv = uv; _mesh.triangles = triangles;
                _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
                _filter.sharedMesh = _mesh;
            }
            if (screenMaterial != null) _renderer.sharedMaterial = screenMaterial;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            if (_properties == null) _properties = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_properties);
            var texture = sourceTexture != null ? sourceTexture : Texture2D.whiteTexture;
            _properties.SetTexture("_BaseMap", texture);
            _properties.SetTexture("_MainTex", texture);
            _renderer.SetPropertyBlock(_properties);
            _lastWidth = safeWidth; _lastRadius = safeRadius; _lastAspect = aspect;
            _lastSegments = count; _lastFlip = flipVertical;
            _lastTexture = sourceTexture; _lastMaterial = screenMaterial; _dirty = false;
        }

        void OnDisable()
        {
            if (_filter != null && _filter.sharedMesh == _mesh) _filter.sharedMesh = null;
            if (_renderer != null) _renderer.SetPropertyBlock(null);
            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
                _mesh = null;
            }
        }
    }
}
