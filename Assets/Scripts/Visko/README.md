# Visko on Quest 3 (hackathon development)

`ViskoStreamClient` connects directly to Reactor over HTTPS and WebRTC.
Reactor currently requires TLS 1.3. HTTPS uses the macOS system curl in the Editor
and Android HttpsURLConnection on Quest, with normal certificate validation.
There is no PC/Python bridge or separate token server in this flow. Internet and
an account with access/credits for `reactor/visko-orbis-dynamic` are required.

## Run

1. Let Unity finish importing packages and compiling.
2. Open **Tools > Visko > Local API Key**, paste your Reactor key, and save.
3. Open `Assets/Scenes/ViskoTest.unity` and press Play for an Editor smoke test.
   The video screen is created at runtime. Session placement can take a while.
4. For Quest, choose **Tools > Visko > Build Quest Test APK**. This builds only
   ViskoTest to `Builds/ViskoQuestTest.apk`, using Android ARM64/IL2CPP with internet
   permission and optimized frame pacing disabled. Install the APK on your Quest.
5. The initial prompt starts automatically. The status appears above the screen.
   Set `Start Prompt` in the Inspector before building to change the opening scene.

The key is saved to `Assets/Resources/ReactorLocalConfig.json`; both it and its
`.meta` are Git-ignored. Local APKs contain the key: do not share them. Do not put
keys into scenes, source files, or committed configuration. No key ships in this repo.

Use the Disconnect control before ending a demo. Backgrounding the app also
requests session termination. Forced process termination or lost connectivity can
prevent cleanup; verify the session has ended in Reactor in those cases.

## Integration

Add `ViskoStreamClient` to another scene and assign `Assets/Materials/Visko/ViskoScreen.mat` to
`Screen Material` so the shader is included in builds. Set a renderer to display
video on an existing surface, or let the client create its own. Keep only one client
active per scene. Use the scene's XR rig for head tracking. No controller button
bindings are provided by the Visko prefab.

The existing methods (`SetPrompt`, `StartGeneration`, `PauseGeneration`,
`ResumeGeneration`, `ResetGeneration`, `SetImage`, etc.) now use the WebRTC data
channel. Images upload over HTTPS; queued start commands wait for `image_accepted`.
Reconnect obtains a fresh token/session. Token lifetime is six hours for development;
long-running demos should reconnect before expiry. Automatic network recovery and
production authentication are intentionally out of scope.

## Protocol references and validation

Wire format follows the official Reactor JS SDK 2.12.4 (API 1, WebRTC 1.0):
https://github.com/reactor-team/js-sdk

Media transport uses Unity WebRTC 3.0.0:
https://github.com/Unity-Technologies/com.unity.webrtc

Verified: Unity Editor compilation, scene startup, screen/status creation, missing-key
handling, Git exclusion rules, native HTTPS token issuance (HTTP 200), and Android Java
source compilation. Live Reactor media, image uploads, and Quest device
playback require an API key and device validation; they have not yet been verified.

`ReactorBridge/` is retained as a legacy reference; do not run it for this client.
