# Visko Orbis bridge for Unity

Unity has no Reactor SDK, so `visko_bridge.py` holds the Reactor session with the
Python `reactor-sdk` and streams video, audio and events to Unity over localhost
TCP (port 7777). `Assets/Visko/ViskoStreamClient.cs` is the Unity side.

## Run
1. Copy `.env.example` to `.env` and put your `rk_...` key in it
   (or set the `REACTOR_API_KEY` environment variable). Never put the key in Unity.
2. Double-click `run_bridge.bat` (first run creates `.venv` and installs deps).
3. In Unity, add `ViskoStreamClient` to any GameObject and press Play.

A Reactor session opens when Unity connects and closes when you leave Play mode.
Use `--model reactor/visko-orbis-dynamic` for the dynamic variant, `--port` to change the port.

## Steering from your scene
```csharp
var visko = FindFirstObjectByType<Visko.ViskoStreamClient>();
visko.SetPrompt("The rocket lifts off through clouds toward the moon...");  // morphs at next chunk (~1.8 s)
visko.ResetGeneration(); visko.SetImage(tex); visko.SetPrompt(p); visko.StartGeneration(); // new opening frame
visko.MessageReceived += (type, json) => { if (type == "generation_complete") { /* ... */ } };
```
