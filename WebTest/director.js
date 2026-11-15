// Small stand-in Story Director. Runs in the Vite dev server (Node), never in the browser,
// so the OpenAI key stays on this machine. The full Director can replace this file as
// long as it returns the same Scene shape (see README "Scene contract").
import fs from "node:fs";
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
  uses_item: z.string().nullable().describe("Always null: choices never use inventory items."),
});

export const Scene = z.object({
  narration: z.string().describe("One or two sentences setting up the situation and the dilemma."),
  location: z.string().describe("Short name of where this happens, e.g. 'Memorial Church'."),
  transition: z.enum(["morph", "cut"]).describe("'morph' if the location is the same as the previous beat, 'cut' if it changes."),
  arrival_video_prompt: z.string().nullable().describe("Visko prompt for the journey from the previous location into this checkpoint, continuing the last outcome. Null only if the location did not change."),
  video_prompt: z.string().describe("Visko prompt for the checkpoint while the player decides."),
  audio_prompt: z.string().describe("One sentence of what the scene SOUNDS like: ambience, instruments, materials. Not a picture description."),
  choices: z.array(Choice).describe("Exactly 2 choices, each a way to leave this checkpoint for next_checkpoint. Empty only when is_ending is true."),
  is_ending: z.boolean(),
  ending_title: z.string().nullable().describe("Only for endings, e.g. 'Ending: Sovereign Pelican Moon Queen'."),
});

// Shared with Unity's DirectorClient: edit the story rules in that one file.
const SYSTEM = fs.readFileSync(new URL("../Assets/Story/DirectorSystemPrompt.txt", import.meta.url), "utf8");

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
    const scene = response.output_parsed;
    // Story choices are travel decisions; items are only used physically (launch pad, orbit).
    for (const choice of scene.choices) choice.uses_item = null;
    return { scene, model: response.model, usage: response.usage };
  };
}
