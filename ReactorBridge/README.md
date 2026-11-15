> Legacy PC bridge: the current Unity client connects directly to Reactor.
> Use [the Quest setup guide](../Assets/Scripts/Visko/README.md) for the current flow.
> The instructions below describe the original bridge implementation.

# Visko Orbis in Unity (AI-generated video + sound)

Reactor's Visko Orbis model generates a live, steerable video stream from a text
prompt. Unity has no Reactor SDK, so a small Python program here (the "bridge")
talks to Reactor and passes video, audio and events to Unity on this PC.

```
Unity (Assets/Scripts/Visko/ViskoStreamClient.cs)  <->  ReactorBridge/visko_bridge.py  <->  Reactor
```

## First-time setup (each teammate, once)
1. Install Python 3.10+ (check with `python --version`).
2. In this folder, copy `.env.example` to `.env` and put the team's Reactor API key in it:
   `REACTOR_API_KEY=rk_...`
   Get the key from a teammate privately. **Never commit `.env`** (it's gitignored)
   and never put the key in Unity.

## Every time
1. Double-click `run_bridge.bat` and wait for
   `Visko bridge listening on 127.0.0.1:7777`. Leave the window open.
   (The first run takes a minute to install dependencies.)
2. In Unity open `Assets/Scenes/ViskoTest.unity` and press Play.
   The top-left box shows the status; type a prompt and click **Steer** to change the scene.
3. Press Play again to stop. That also ends the paid Reactor session.

To use it in another scene, add the `ViskoStreamClient` component to any GameObject.

## Quest 3
- **Quest Link / Air Link (easiest):** press Play in Unity on the PC running the bridge. Works as is.
- **Standalone Android build:** not wired up yet. The headset would need to reach the bridge
  over Wi-Fi (PC's IP instead of 127.0.0.1, and the bridge started with `--host 0.0.0.0`).

## Steering from code
```csharp
var visko = FindFirstObjectByType<Visko.ViskoStreamClient>();
visko.SetPrompt("The rocket lifts off through clouds toward the moon...");  // morphs at next chunk (~1.8 s)
visko.ResetGeneration(); visko.SetImage(tex); visko.SetPrompt(p); visko.StartGeneration(); // new opening frame
visko.MessageReceived += (type, json) => { if (type == "generation_complete") { /* ... */ } };
```

Options: `--model reactor/visko-orbis-dynamic` for the dynamic variant, `--port` to change the port.

## Troubleshooting
- Unity says "bridge not running": the black bridge window isn't open, or it closed with an error.
- `REACTOR_API_KEY is not set`: the `.env` file is missing or in the wrong folder (must be `ReactorBridge/.env`).
- Anything else: check the bridge window and Unity's Console tab for the error.
