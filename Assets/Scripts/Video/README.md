# Curved video screen and floor glow

Add `Assets/Prefabs/Curved Video Screen.prefab` to a scene. The screen generates
its curved mesh in Edit and Play modes. Width is the unrolled surface width;
Radius controls curvature, and Corner Radius clips the four corners without
stretching the image.

## Ambient floor colors

The prefab includes `VideoFloorGlow`. It reads the same `sourceTexture` used by
`CurvedVideoScreen`; assign a Texture/RenderTexture in the Inspector, or call
`CurvedVideoScreen.SetTexture(texture)` from a video source. Visko is not wired
to this component automatically. A missing source hides the glow (the screen's
white placeholder is not treated as video).

- Intensity: brightness; default 0.55. Zero hides the effect.
- Width Multiplier: glow width relative to the screen; default 1.35.
- Spread Distance: distance towards the viewer in world metres; default 3.
- Blur: color smoothing; default 3.
- Floor Height: world Y of the floor; default 0.
- Surface Offset: lift above the floor to avoid z-fighting; default 0.015 m.
- Forward Offset: move the patch towards the viewer from the screen projection.
- Updates Per Second: color refresh frequency; default 15. Video playback itself
  is not throttled.

The patch follows the screen's horizontal direction but stays parallel to a
horizontal world floor. It adds blurred colors and fades to zero at every edge.
It does not illuminate other objects or produce physical reflections. Both eye
views are supported by the URP shader's stereo macros; Quest performance still
requires device testing.

The effect uses two 64x36 render textures, three small GPU blits per color update,
and one transparent two-triangle floor draw. It performs no CPU pixel readback
and uses no reflection camera. Runtime resources are released on disable/destroy.

Verified in the Unity Editor: shader compilation, GPU blur color preservation,
black input, missing-source visibility, and render-texture cleanup.
