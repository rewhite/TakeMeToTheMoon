import { defineConfig, loadEnv } from "vite";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createDirector } from "./director.js";

const here = path.dirname(fileURLToPath(import.meta.url));
const VISKO_MODELS = ["reactor/visko-orbis-stable", "reactor/visko-orbis-dynamic"];

// The legacy bridge already has a local, Git-ignored .env with the Reactor key; reuse it as a fallback.
function readEnvFile(file) {
  if (!fs.existsSync(file)) return {};
  const out = {};
  for (const line of fs.readFileSync(file, "utf8").split(/\r?\n/)) {
    const m = line.match(/^\s*([A-Z0-9_]+)\s*=\s*(.*?)\s*$/);
    if (m) out[m[1]] = m[2].replace(/^["']|["']$/g, "");
  }
  return out;
}

function readJson(req) {
  return new Promise((resolve, reject) => {
    let body = "";
    req.on("data", (c) => (body += c));
    req.on("end", () => {
      try { resolve(body ? JSON.parse(body) : {}); } catch (e) { reject(e); }
    });
  });
}

function send(res, status, data) {
  res.statusCode = status;
  res.setHeader("Content-Type", "application/json");
  res.end(JSON.stringify(data));
}

// Local-only API: keys are read here and never sent to the browser.
function localApi(env) {
  const nextScene = createDirector({
    apiKey: env.OPENAI_API_KEY,
    model: env.OPENAI_MODEL || undefined,
    effort: env.OPENAI_REASONING_EFFORT || undefined,
  });
  return {
    name: "visko-local-api",
    configureServer(server) {
      server.middlewares.use("/api/reactor-token", async (req, res) => {
        if (req.method !== "POST") return send(res, 405, { error: "POST only" });
        if (!env.REACTOR_API_KEY) return send(res, 500, { error: "REACTOR_API_KEY is not set. Put it in WebTest/.env." });
        try {
          const { model = VISKO_MODELS[0] } = await readJson(req);
          if (!VISKO_MODELS.includes(model)) return send(res, 400, { error: `Unknown model ${model}` });
          const r = await fetch("https://api.reactor.inc/tokens", {
            method: "POST",
            headers: { "Reactor-API-Key": env.REACTOR_API_KEY.trim(), "Content-Type": "application/json" },
            body: JSON.stringify({
              authorization_details: [{
                type: "session",
                resources: { models: { match: [model] } },
                constraints: { max_sessions: 5 },
              }],
            }),
          });
          if (!r.ok) return send(res, 502, { error: `Reactor token request failed (HTTP ${r.status}). Check the key, credits and model access.` });
          const { jwt } = await r.json();
          send(res, 200, { jwt });
        } catch (e) {
          send(res, 502, { error: `Reactor token request failed: ${e.message}` });
        }
      });

      server.middlewares.use("/api/director", async (req, res) => {
        if (req.method !== "POST") return send(res, 405, { error: "POST only" });
        const started = Date.now();
        try {
          const run = await readJson(req);
          const result = await nextScene(run);
          send(res, 200, { ...result, ms: Date.now() - started });
        } catch (e) {
          send(res, 500, { error: e.message, ms: Date.now() - started });
        }
      });
    },
  };
}

export default defineConfig(({ mode }) => {
  const env = { ...readEnvFile(path.join(here, "../ReactorBridge/.env")), ...loadEnv(mode, here, "") };
  return { plugins: [localApi(env)] };
});
