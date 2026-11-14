 # Prompt: port the WebTest story loop to Unity

Paste everything below the line into Claude Code (or another coding agent) opened at the
repo root. It assumes the `web-test` branch (or wherever `WebTest/` was merged) is checked out.

---

Port the browser test bench in `WebTest/` into the Unity project so the same story loop runs
on a standalone Quest 3, with Visko video on the existing screen.

## Read first

- `AGENTS.md` (project rules: hackathon prototype, standalone Quest, no PC bridge, dev keys
  only in local Git-ignored config, one commit per feature).
- `GAME_DESIGN.md` (the game).
- `WebTest/README.md` (the Scene contract and how web maps to Unity).
- `WebTest/director.js` (Director system prompt and Scene schema: copy these verbatim).
- `WebTest/src/story.js` (the loop: fixed opening, roommate choice, Director beats, ending).
- `WebTest/src/visko.js` (morph vs cut, chunk-limit auto-restart).
- `Assets/Scripts/Visko/ViskoStreamClient.cs` and `Assets/Scripts/Visko/README.md`
  (the existing Unity Visko client; reuse it, don't rewrite it).

## What exists and is verified on the web

- Visko model: `reactor/visko-orbis-stable` works; `reactor/visko-orbis-dynamic` often has no
  capacity. Same commands for both.
- Morph = `set_prompt` (and `set_audio_prompt`) on the running shot; it changes at the next
  1.8 s chunk. Cut = `reset`, optional `set_image`, `set_prompt`, `start`.
- If a run sends `generation_complete` (chunk limit), sending `start` again continues it.
- Director: OpenAI Responses API with structured output (`text.format` = JSON schema built
  from the `Scene` schema in `director.js`), model `gpt-5.5`, reasoning effort `low`.
  It takes ~12 s, so request the next beat as soon as the player picks, while the outcome
  video plays.
- Every video prompt gets a shared style line appended (default in `WebTest/index.html`).

## Build

1. **Model choice.** Make `reactor/visko-orbis-stable` the model used by the game scene
   (the `model` field on `ViskoStreamClient`). Keep dynamic selectable in the Inspector.
2. **Scene contract in C#.** Plain serializable classes matching the Scene/Choice fields in
   `WebTest/README.md`, parsed with Newtonsoft (already in the project).
3. **Director client.** A small C# class that calls `POST https://api.openai.com/v1/responses`
   with the same system prompt, the same run-state input and the Scene JSON schema
   (`strict: true`), and returns a Scene. Use the project's existing HTTPS approach if
   UnityWebRequest has trouble on Quest. The OpenAI key goes in a local Git-ignored config,
   the same way `Tools > Visko > Local API Key` stores the Reactor key
   (`Assets/Resources/ReactorLocalConfig.json`); add the field there or a sibling file, and
   add it to `.gitignore`. Never log the key or commit it.
4. **Story runner.** A MonoBehaviour that mirrors `story.js`: run state (distance 238,900 mi,
   wellness, 3 items, roommate, decision count, history), the fixed OPENING and ROOMMATE
   scenes, `must_end` after N decisions or at 0 miles, morph/cut via `ViskoStreamClient`,
   outcome dwell in parallel with the Director call, retry on Director error, ending.
5. **Minimal choice UI.** World-space buttons (or 3D grabbable cards) for 2-3 choices and
   a narration text panel, usable with the existing Meta XR rig/hands in `Main.unity`.
   Items can be a temporary picker until the real 30-second grab exists.
6. **Logging.** Log to the Unity console: prompts sent, chunk_complete with seconds since the
   last prompt, Director latency. Same information as the web log.

## Done when

- In the Editor (Play mode) a full run works: opening, roommate choice, at least 3 Director
  beats with morphs/cuts visible on the screen, an ending.
- The same build runs standalone on Quest 3 (Build Quest Test APK flow) with no PC process.
- No keys in tracked files: `git status` shows no config with keys.
- Commits split by feature (contract + Director client, story runner, UI, model default).

## Out of scope

The full Director your teammate is designing (keep the contract so it can replace this one),
the 30-second grab interaction, production auth, store builds.
