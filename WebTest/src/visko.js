// The "player": turns scenes into Visko commands. This is the piece Unity's
// ViskoStreamClient replaces later; morph/cut map 1:1 to SetPrompt and
// ResetGeneration + SetImage + SetPrompt + StartGeneration.
import { ViskoOrbisDynamicModel } from "@reactor-models/visko-orbis-dynamic";
import { ViskoOrbisStableModel } from "@reactor-models/visko-orbis-stable";

// Same commands and messages; separate server pools, so one may have capacity when the other is full.
const MODELS = {
  "reactor/visko-orbis-stable": ViskoOrbisStableModel,
  "reactor/visko-orbis-dynamic": ViskoOrbisDynamicModel,
};
export const MODEL_NAMES = Object.keys(MODELS);

const CAPACITY_RETRY_MS = 20000;
const MAX_CAPACITY_RETRIES = 30; // about 10 minutes
const MAX_QUOTA_RETRIES = 3;

// Reactor answers both "too many sessions this minute" and "no free servers" with HTTP 429.
function retryPlan(error, attempt) {
  if (error?.code !== "RATE_LIMITED") return null;
  if (/no available capacity/i.test(error.message)) {
    return attempt <= MAX_CAPACITY_RETRIES ? { ms: CAPACITY_RETRY_MS, why: "Reactor has no free servers" } : null;
  }
  return attempt <= MAX_QUOTA_RETRIES
    ? { ms: Math.max(error.retry_after_ms ?? 0, 5000), why: "session-per-minute limit" }
    : null;
}

export function createPlayer({ video, audio, log, onStatus }) {
  let model = null;
  let connecting = false;
  let cancelled = false;
  let promptSentAt = 0;
  let chunksSincePrompt = 0;
  let lastAudioPrompt = null;
  const settings = { styleLine: "", autoContinue: true };

  const withStyle = (prompt) => (settings.styleLine.trim() ? `${prompt.trim()} ${settings.styleLine.trim()}` : prompt.trim());
  const sincePrompt = () => ((performance.now() - promptSentAt) / 1000).toFixed(1);

  function onMessage(msg) {
    switch (msg.type) {
      case "chunk_complete":
        chunksSincePrompt++;
        log("chunk", `#${msg.chunk_index} done, ${chunksSincePrompt} since last prompt (+${sincePrompt()}s)`);
        break;
      case "prompt_accepted":
        log("visko", `prompt accepted (+${sincePrompt()}s)`);
        break;
      case "generation_started":
        log("visko", `generation started (+${sincePrompt()}s)`);
        break;
      case "generation_complete":
        log("visko", `run hit its chunk limit after ${msg.total_chunks} chunks`);
        if (settings.autoContinue) {
          log("visko", "auto-continue: start again (hard cut)");
          model.start();
        }
        break;
      case "image_accepted":
        log("visko", `start image accepted ${msg.width}x${msg.height}`);
        break;
      case "command_error":
        log("error", `${msg.command} rejected: ${msg.reason}`);
        break;
    }
  }

  function createModel(modelName) {
    const m = new MODELS[modelName]();
    m.on("statusChanged", (s) => { onStatus(s); log("status", s); });
    // Connect failures are logged by the retry loop; this covers errors after connecting.
    m.on("error", (e) => { if (!connecting) log("error", `${e.code}: ${e.message}`); });
    m.on("trackReceived", (name, _track, stream) => {
      // Video element stays muted; sound plays through the separate audio element.
      const el = name === "main_video" ? video : name === "main_audio" ? audio : null;
      if (!el) return;
      el.srcObject = stream;
      el.play().catch(() => log("error", `browser blocked ${name} autoplay; click the video`));
    });
    m.onMessage(onMessage);
    return m;
  }

  async function waitOrCancel(ms, attempt) {
    for (let left = Math.ceil(ms / 1000); left > 0; left--) {
      if (cancelled) return;
      onStatus(`retry ${attempt} in ${left}s`);
      await new Promise((r) => setTimeout(r, 1000));
    }
  }

  async function connect(modelName) {
    if (model || connecting) return;
    connecting = true;
    cancelled = false;
    try {
      onStatus("token");
      const r = await fetch("/api/reactor-token", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ model: modelName }),
      });
      const body = await r.json();
      if (!r.ok) throw new Error(body.error);

      const t0 = performance.now();
      for (let attempt = 1; ; attempt++) {
        if (cancelled) throw new Error("Connect cancelled.");
        const m = createModel(modelName);
        model = m;
        try {
          // Default options: maxAttempts also caps SDP-answer polling, so lowering it times out the handshake.
          await m.connect(body.jwt);
          if (cancelled) throw new Error("Connect cancelled.");
          log("visko", `connected to ${modelName} in ${((performance.now() - t0) / 1000).toFixed(1)}s`);
          return;
        } catch (e) {
          model = null;
          await m.disconnect().catch(() => {});
          const plan = cancelled ? null : retryPlan(e, attempt);
          log("error", `${e.code ?? "error"}: ${e.message}`);
          if (!plan) throw e;
          log("visko", `${plan.why}; retrying in ${Math.round(plan.ms / 1000)}s (attempt ${attempt + 1})`);
          await waitOrCancel(plan.ms, attempt + 1);
        }
      }
    } catch (e) {
      onStatus("disconnected");
      throw e;
    } finally {
      connecting = false;
    }
  }

  function cancelConnect() {
    if (!connecting) return;
    cancelled = true;
    log("visko", "cancelling connect");
    model?.disconnect().catch(() => {});
  }

  async function disconnect() {
    if (connecting) return cancelConnect();
    if (!model) return;
    const m = model;
    model = null;
    await m.disconnect();
    video.srcObject = null;
    audio.srcObject = null;
    lastAudioPrompt = null;
    log("status", "disconnected (session ended)");
  }

  function requireModel() {
    if (!model || model.getStatus() !== "ready") throw new Error("Connect to Visko first.");
    return model;
  }

  function markPrompt(kind, prompt) {
    promptSentAt = performance.now();
    chunksSincePrompt = 0;
    log(kind, prompt);
  }

  async function setAudio(m, audioPrompt) {
    if (audioPrompt == null || audioPrompt === lastAudioPrompt) return;
    lastAudioPrompt = audioPrompt;
    await m.setAudioPrompt({ prompt: audioPrompt });
  }

  // Same shot, new prompt: picture morphs at the next chunk boundary.
  async function morph(prompt, audioPrompt) {
    const m = requireModel();
    const full = withStyle(prompt);
    markPrompt("morph", full);
    await m.setPrompt({ prompt: full });
    await setAudio(m, audioPrompt);
  }

  // New shot: reset clears prompt, image and the model's memory of characters/setting.
  async function cut(prompt, audioPrompt, imageFile) {
    const m = requireModel();
    const full = withStyle(prompt);
    markPrompt("cut", full);
    await m.reset();
    if (imageFile) {
      const ref = await m.uploadFile(imageFile, { name: imageFile.name });
      await m.setImage({ image: ref });
    }
    await m.setPrompt({ prompt: full });
    lastAudioPrompt = null;
    await setAudio(m, audioPrompt);
    await m.start();
  }

  return {
    settings,
    connect,
    cancelConnect,
    disconnect,
    isConnecting: () => connecting,
    morph,
    cut,
    pause: () => requireModel().pause(),
    resume: () => requireModel().resume(),
    reset: () => requireModel().reset(),
    isReady: () => !!model && model.getStatus() === "ready",
  };
}
