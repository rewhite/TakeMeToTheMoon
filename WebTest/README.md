# Visko web test (no Unity)

A browser test bench for the core loop of the game:

```
Story Director (OpenAI)  ->  Scene (choices + Visko prompts)  ->  player (this web page now, Unity/Quest later)
```

It is for tuning prompts, morph vs. cut timing and the Director's output before anything
goes to the headset. Nothing here ships in the Quest app.

## Run

1. Install Node 18+ and run `npm install` in this folder (once).
2. Keys, local only and Git-ignored (copy `.env.example` to `.env`):
   - `REACTOR_API_KEY`: if unset here, the key in `../ReactorBridge/.env` is used.
   - `OPENAI_API_KEY`: used by the Director.
   - Optional: `OPENAI_MODEL` (default `gpt-5.5`) and `OPENAI_REASONING_EFFORT`
     (default `low`; lower is faster).
3. `npm run dev` and open the printed URL (http://localhost:5173).
4. Pick the model (`visko-orbis-stable` by default, or `visko-orbis-dynamic`) and press
   **Connect**. If Reactor is full it retries every 20 s (countdown in the status pill;
   press Cancel to stop). If the per-minute session limit is hit it waits and retries
   up to 3 times. **Connect** starts a paid Visko session. **Disconnect** ends it. Closing the tab
   also tries to end it, but use the button.

Keys stay in the Vite dev server (`vite.config.js`): the browser only ever gets a
short-lived Reactor token and Director results.

## What's on the page

- **Story tab**: pick 3 items (stands in for the grab), start a run. Fixed opening and
  roommate choice, then every beat comes from the Director until the Moon. The next beat
  is written while the outcome video plays.
- **Manual tab**: type any prompt and press **Morph** (same shot, changes at the next
  chunk) or **Cut** (reset + prompt + start, optional start image).
- **Style line**: appended to every video prompt so the look stays consistent.
- **Log**: every prompt sent, Visko events with seconds since the last prompt
  (`chunk` lines show when a morph can land), Director latency. **Export run JSON**
  saves the run and log to share or compare.

## Scene contract

Fixed scenes (`src/story.js`) and Director scenes (`director.js`) use the same shape.
The full Director and the Unity client should keep it:

| Field | Meaning |
| --- | --- |
| `location` | Short place name |
| `transition` | `morph` (same place) or `cut` (new place) |
| `narration` | 1-2 sentences, shown/spoken by the game, never drawn by Visko |
| `video_prompt` | What the camera sees while the player decides |
| `audio_prompt` | What it sounds like (sounds, not visuals) |
| `choices[]` | `label`, `outcome_narration`, `outcome_video_prompt`, `miles_gained`, `wellness_change`, `uses_item` |
| `is_ending`, `ending_title` | Landing on the Moon |

Director input is the run state: decisions left, `must_end`, stage, distance, wellness,
inventory, roommate, previous location and a short history of picks.

## Moving to Unity

`src/visko.js` is the only part that is web-specific:

| Web (`visko.js`) | Unity (`ViskoStreamClient`) |
| --- | --- |
| `morph(prompt, audio)` | `SetPrompt` + `SetAudioPrompt` |
| `cut(prompt, audio, image)` | `ResetGeneration`, `SetImage`, `SetPrompt`, `StartGeneration` |
| `generation_complete` auto-restart | `MessageReceived("generation_complete")` then `StartGeneration` |

The Director (`director.js`), the contract, the style line and the timings carry over.

## Visko notes (from the model's SDK docs)

- A new prompt morphs at the next chunk boundary; characters and setting persist until `reset`.
- A start image only applies on `start` after `reset`.
- The audio prompt should describe sounds, about one sentence; scene descriptions make audio worse.
- A run may stop after a chunk limit (`generation_complete`); starting again is a hard cut.
