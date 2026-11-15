// The decision loop: fixed roommate choice, then Director-generated beats until the Moon.
// Every scene, fixed or generated, uses the same Scene contract (see README).

export const ITEMS = ["Alarm clock", "Red Bull can", "Flashlight", "CS textbook", "Mystery paper bag"];
export const START_DISTANCE = 238900;

// Fixed checkpoint 1 scenes, shared with Unity (Assets/Story/OpeningScenes.json, inlined by vite.config.js).
// eslint-disable-next-line no-undef
const { roommate: ROOMMATE } = __OPENING_SCENES__;

// Same checkpoints and numbering as StoryController.Checkpoints in Unity.
export const CHECKPOINTS = [
  "Stanford dorm room",
  "Stanford campus at Memorial Church",
  "Rocket launch pad",
  "Low Earth orbit",
  "Approaching the Moon",
  "Moon landing",
];
const DIRECTOR_BEATS = 4; // Checkpoints 2-5, one decision each.

function pickApproaches() {
  const first = 1 + Math.floor(Math.random() * 10);
  let second = 1 + Math.floor(Math.random() * 9);
  if (second >= first) second++;
  // Rarely, one is swapped for 11, the magic-portal wild card (same odds as Unity).
  if (Math.random() < 1 / 12) second = 11;
  return [first, second];
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

export function createStory({ player, ui, log }) {
  let run = null;
  let busy = false;

  function directorInput() {
    const decisionsLeft = Math.max(0, Math.min(run.options.maxDecisions, DIRECTOR_BEATS) - run.decisions);
    let number = Math.min(Math.max(run.decisions + 2, 2), CHECKPOINTS.length);
    const mustEnd = number === CHECKPOINTS.length || decisionsLeft === 0;
    if (mustEnd) number = CHECKPOINTS.length;
    const here = CHECKPOINTS[number - 1];
    const next = number < CHECKPOINTS.length ? CHECKPOINTS[number] : null;
    return {
      decision_number: run.decisions + 1,
      decisions_left: decisionsLeft,
      must_end: mustEnd,
      checkpoint_number: number,
      checkpoint: here,
      next_checkpoint: next,
      stage: next ? `arrive at ${here}, then leave it for ${next}` : "land on the Moon",
      // Two different random approaches (1-10, see the Director prompt), as Unity's DirectorClient.
      choice_approaches: pickApproaches(),
      distance_remaining_miles: run.distance,
      wellness_percent: run.wellness,
      inventory: run.items,
      roommate: run.roommate,
      previous_location: run.scene?.location ?? null,
      history: run.history,
    };
  }

  async function play(scene, transition = scene.transition) {
    // New checkpoint: play the journey there first, then morph into the checkpoint (as ViskoStoryBridge).
    const first = scene.arrival_video_prompt || scene.video_prompt;
    if (transition === "cut") await player.cut(first, scene.audio_prompt);
    else await player.morph(first, scene.audio_prompt);
    if (scene.arrival_video_prompt) {
      await sleep((run.options.arrivalSeconds ?? 8) * 1000);
      await player.morph(scene.video_prompt, scene.audio_prompt);
    }
  }

  async function show(scene) {
    run.scene = scene;
    run.scenes.push({ at: new Date().toISOString(), scene });
    ui.renderScene(scene, run);
    // First beat of a run always cuts, whatever the Director said.
    await play(scene, run.scenes.length === 1 ? "cut" : scene.transition);
    if (scene.is_ending) ui.renderEnding(scene, run);
  }

  async function fetchNext() {
    const input = directorInput();
    ui.directorStatus("Director is writing the next beat…");
    const r = await fetch("/api/director", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(input),
    });
    const body = await r.json();
    if (!r.ok) throw new Error(body.error);
    log("director", `${body.ms} ms · ${body.scene.location} · ${body.scene.transition}${body.scene.is_ending ? " · ENDING" : ""}`);
    run.directorCalls.push({ input, ms: body.ms, model: body.model, usage: body.usage });
    ui.directorStatus(`Director: ${(body.ms / 1000).toFixed(1)}s`);
    return body.scene;
  }

  async function start(items, options) {
    if (busy) return;
    busy = true;
    run = {
      options,
      items: [...items],
      distance: START_DISTANCE,
      wellness: 100,
      roommate: null,
      decisions: 0,
      history: [],
      scene: null,
      scenes: [],
      directorCalls: [],
      startedAt: new Date().toISOString(),
    };
    try {
      await show(ROOMMATE);
    } catch (e) {
      ui.error(e.message);
    } finally {
      busy = false;
    }
  }

  async function choose(index) {
    if (busy || !run?.scene) return;
    const scene = run.scene;
    const c = scene.choices[index];
    if (!c) return;
    busy = true;

    run.distance = Math.max(0, run.distance - Math.max(0, c.miles_gained));
    const wellnessBefore = run.wellness;
    run.wellness = Math.max(0, Math.min(100, run.wellness + c.wellness_change));
    if (c.uses_item) run.items = run.items.filter((i) => i.toLowerCase() !== c.uses_item.toLowerCase());
    if (c.roommate) run.roommate = c.roommate;
    else run.decisions++;
    run.history.push({ location: scene.location, situation: scene.narration, picked: c.label, outcome: c.outcome_narration,
      wellness_change: run.wellness - wellnessBefore, wellness_after: run.wellness });
    log("choice", `${c.label}  (+${c.miles_gained} mi, ${c.wellness_change}% wellness${c.uses_item ? `, used ${c.uses_item}` : ""})`);

    ui.renderOutcome(c, run);
    try {
      await player.morph(c.outcome_video_prompt, null);
      // Write the next beat while the outcome plays; show it after both are done.
      const [next] = await Promise.all([fetchNext(), sleep(run.options.outcomeSeconds * 1000)]);
      await show(next);
    } catch (e) {
      ui.error(e.message, () => retryNext());
    } finally {
      busy = false;
    }
  }

  async function retryNext() {
    if (busy) return;
    busy = true;
    try {
      await show(await fetchNext());
    } catch (e) {
      ui.error(e.message, () => retryNext());
    } finally {
      busy = false;
    }
  }

  return { start, choose, getRun: () => run, isBusy: () => busy };
}
