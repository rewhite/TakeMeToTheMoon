// The decision loop: fixed opening, then Director-generated beats until the Moon.
// Every scene, fixed or generated, uses the same Scene contract (see README).

export const ITEMS = ["Alarm clock", "Red Bull can", "Flashlight", "CS textbook", "Mystery paper bag"];
export const START_DISTANCE = 238900;

const OPENING = {
  location: "Stanford dorm room",
  transition: "cut",
  narration:
    "Breaking news. Earth has officially hit 451 degrees. The oceans are a hot tub. Lake Lag has boiled, which is impressive, because it was already empty. The Moon is now the only place with decent weather. Good luck, and stay hydrated.",
  video_prompt:
    "Waking up in a cramped Stanford dorm room, blinding orange sunlight through the blinds, posters curling off the walls from the heat, a small TV on the desk flashing red breaking-news graphics, the air shimmering.",
  audio_prompt: "Muffled TV newscaster, distant sirens, a rattling struggling air conditioner.",
  choices: [],
  is_ending: false,
  ending_title: null,
};

const ROOMMATE = {
  location: "Stanford dorm room",
  transition: "morph",
  narration: "Your roommate is asleep through the apocalypse.",
  video_prompt:
    "The camera turns to the other bed: a roommate sprawled and snoring under a wobbling desk fan, sweat-soaked pillow, sunlight blazing across the blanket.",
  audio_prompt: "Loud snoring, a wobbling desk fan, distant sirens.",
  choices: [
    {
      label: "Wake him up",
      outcome_narration: "He jolts awake, grabs a backpack, and is somehow out the door before you.",
      outcome_video_prompt:
        "The roommate jolts upright, grabs a backpack and sprints out the dorm door ahead of the camera, papers flying.",
      miles_gained: 0,
      wellness_change: -2,
      uses_item: null,
      roommate: "came along",
    },
    {
      label: "Leave him",
      outcome_narration: "You save time. He keeps snoring. You'll probably never see him again.",
      outcome_video_prompt:
        "The camera backs out of the dorm room, the roommate still snoring under the fan, and the door swings shut.",
      miles_gained: 0,
      wellness_change: 0,
      uses_item: null,
      roommate: "left behind, still asleep",
    },
  ],
  is_ending: false,
  ending_title: null,
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function stageFor(distance) {
  const f = distance / START_DISTANCE;
  if (f > 0.85) return "escape campus and leave the ground";
  if (f > 0.5) return "get airborne, up through the sky";
  if (f > 0.15) return "out in space, travelling to the Moon";
  return "approaching the Moon";
}

export function createStory({ player, ui, log }) {
  let run = null;
  let busy = false;

  function directorInput() {
    const decisionsLeft = Math.max(0, run.options.maxDecisions - run.decisions);
    return {
      decision_number: run.decisions + 1,
      decisions_left: decisionsLeft,
      must_end: run.distance <= 0 || decisionsLeft === 0,
      stage: stageFor(run.distance),
      distance_remaining_miles: run.distance,
      wellness_percent: run.wellness,
      inventory: run.items,
      roommate: run.roommate,
      previous_location: run.scene?.location ?? null,
      history: run.history,
    };
  }

  async function play(scene, transition = scene.transition) {
    if (transition === "cut") await player.cut(scene.video_prompt, scene.audio_prompt);
    else await player.morph(scene.video_prompt, scene.audio_prompt);
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
      await show(OPENING);
      await sleep(options.openingSeconds * 1000);
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
    run.wellness = Math.max(0, Math.min(100, run.wellness + c.wellness_change));
    if (c.uses_item) run.items = run.items.filter((i) => i.toLowerCase() !== c.uses_item.toLowerCase());
    if (c.roommate) run.roommate = c.roommate;
    else run.decisions++;
    run.history.push({ location: scene.location, situation: scene.narration, picked: c.label, outcome: c.outcome_narration });
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
