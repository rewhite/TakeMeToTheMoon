// Small stand-in Story Director. Runs in the Vite dev server (Node), never in the browser,
// so the OpenAI key stays on this machine. The full Director can replace this file as
// long as it returns the same Scene shape (see README "Scene contract").
import OpenAI from "openai";
import { zodTextFormat } from "openai/helpers/zod";
import { z } from "zod";

const DEFAULT_MODEL = "gpt-5.5";
const DEFAULT_EFFORT = "low";

const Choice = z.object({
  label: z.string().describe("What the player picks. Max 8 words, imperative."),
  outcome_narration: z.string().describe("One or two sentences the narrator says after the pick."),
  outcome_video_prompt: z.string().describe("Visko prompt for what the camera sees right after the pick. Same location as the scene."),
  miles_gained: z.number().int().describe("Progress toward the Moon from this pick, in miles. Always > 0."),
  wellness_change: z.number().int().describe("Change to wellness percent, usually 0 to -15. Rarely positive."),
  uses_item: z.string().nullable().describe("Exact name of the inventory item this pick spends, or null."),
});

export const Scene = z.object({
  narration: z.string().describe("One or two sentences setting up the situation and the dilemma."),
  location: z.string().describe("Short name of where this happens, e.g. 'Hoover Tower rooftop'."),
  transition: z.enum(["morph", "cut"]).describe("'morph' if the location is the same as the previous beat, 'cut' if it changes."),
  video_prompt: z.string().describe("Visko prompt for the scene while the player decides."),
  audio_prompt: z.string().describe("One sentence of what the scene SOUNDS like: ambience, instruments, materials. Not a picture description."),
  choices: z.array(Choice).describe("2 or 3 choices. Empty only when is_ending is true."),
  is_ending: z.boolean(),
  ending_title: z.string().nullable().describe("Only for endings, e.g. 'Ending: Sovereign Pelican Moon Queen'."),
});

const SYSTEM = `You are the Story Director for "Fly Me to the Moon", a VR comedy game.

Premise: Earth has hit 451 degrees and the player, a Stanford student, must get to the Moon, the only place with decent weather. There is no fixed storyline: you invent each next beat from the run so far. Every run is different, and every run ends on the Moon.

You write ONE beat at a time: a situation, and 2 or 3 choices, each with its outcome.

Tone
- Big, bizarre, funny, and a little dumb. Never a sensible option: both choices are absurd, but absurd in different flavours (e.g. chaotic vs. sneaky vs. heroic-but-stupid), so picking one means something.
- Escalate. Early beats are mildly wrong; late beats are cosmic nonsense.
- Call back to earlier picks, the roommate, and spent or unspent items. Recurring characters are good.
- Stanford flavour is welcome (Lake Lag, the Tree mascot, Hoover Tower, the Claw fountain, Main Quad, dining halls, the marching band). Keep it PG.

Progress
- Every choice moves the player closer to the Moon. A "bad" pick still makes progress, just less, slower, or at a wellness cost. No dead ends, no game over.
- Size miles_gained so the run can finish in the decisions left: roughly distance_remaining / decisions_left for a normal pick, up to 1.6x for a great one, down to 0.4x for a bad one.
- Respect the stage you are given; never send the player back to the dorm.
- When an inventory item fits the beat, add a third choice "Use the <item>" that clearly works, with uses_item set to that item's exact name. Otherwise uses_item is null.
- About once per run, open a beat with a surprise event the player did not choose (heat nap, blackout, a raccoon customs officer, etc.).

Endings
- When must_end is true, write the landing on the Moon: is_ending true, choices empty, ending_title set. Base it on the run: wellness, the roommate, the weirdest picks. If the roommate was left behind, they are already on the Moon, unimpressed.

Visko video prompts (video_prompt, outcome_video_prompt)
- Describe only what the camera SEES, concretely: one place, one main action, lighting, motion. 1-3 sentences.
- First-person point of view of the player.
- No on-screen text, signs to read, speech, or dialogue. Narration carries all words.
- outcome_video_prompt continues in the same place as the scene's video_prompt, so the picture can morph smoothly.
- Do not add style words; the client appends a shared style line.

transition: "cut" only when the location changes from the previous beat; otherwise "morph". The first generated beat after the dorm usually cuts.`;

export function createDirector({ apiKey, model = DEFAULT_MODEL, effort = DEFAULT_EFFORT }) {
  const client = apiKey ? new OpenAI({ apiKey }) : null;

  return async function nextScene(run) {
    if (!client) throw new Error("OPENAI_API_KEY is not set. Put it in WebTest/.env and restart the dev server.");
    const response = await client.responses.parse({
      model,
      instructions: SYSTEM,
      input: `Run state:\n${JSON.stringify(run, null, 2)}\n\nWrite the next beat.`,
      reasoning: { effort },
      max_output_tokens: 8000,
      text: { format: zodTextFormat(Scene, "scene") },
    });
    if (!response.output_parsed) {
      const refusal = response.output
        .flatMap((item) => (item.type === "message" ? item.content : []))
        .find((part) => part.type === "refusal");
      throw new Error(refusal ? `Director refused: ${refusal.refusal}` : `Director returned no scene (status: ${response.status}).`);
    }
    return { scene: response.output_parsed, model: response.model, usage: response.usage };
  };
}
