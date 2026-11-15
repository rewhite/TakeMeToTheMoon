import { createPlayer, MODEL_NAMES } from "./visko.js";
import { createStory, ITEMS, START_DISTANCE } from "./story.js";

const $ = (id) => document.getElementById(id);
const logEntries = [];
const t0 = performance.now();

function log(kind, text) {
  const t = ((performance.now() - t0) / 1000).toFixed(1);
  logEntries.push({ t: Number(t), kind, text });
  const li = document.createElement("li");
  li.className = `k-${kind}`;
  li.innerHTML = `<span class="t">${t}s</span><span class="k">${kind}</span>`;
  li.append(document.createTextNode(text));
  $("log").prepend(li);
}

const player = createPlayer({
  video: $("video"),
  audio: $("audio"),
  log,
  onStatus: (s) => {
    $("status").textContent = s;
    $("status").dataset.state = s.startsWith("retry") ? "waiting" : s;
    syncConnectButton();
  },
});

function syncConnectButton() {
  const connecting = player.isConnecting();
  $("connect").textContent = connecting ? "Cancel" : "Connect";
  $("connect").disabled = !connecting && player.isReady();
  $("model").disabled = connecting || player.isReady();
}

// ---- Story UI ----
function renderMeters(run) {
  if (!run) return ($("meters").textContent = "");
  const pct = Math.round((1 - run.distance / START_DISTANCE) * 100);
  $("meters").innerHTML = `
    <span><b>${run.distance.toLocaleString()}</b> mi to the Moon (${pct}%)</span>
    <span><b>${run.wellness}%</b> wellness</span>
    <span>Decision <b>${run.decisions}/${run.options.maxDecisions}</b></span>
    <span>Roommate: <b>${run.roommate ?? "?"}</b></span>
    <span>Items: <b>${run.items.join(", ") || "none"}</b></span>`;
}

function choiceButton(label, onClick) {
  const b = document.createElement("button");
  b.textContent = label;
  b.onclick = onClick;
  return b;
}

const ui = {
  renderScene(scene, run) {
    $("setup").hidden = true;
    $("scene").hidden = false;
    $("ending").hidden = true;
    $("location").textContent = `${scene.location} · ${scene.transition}`;
    $("scene-text").textContent = scene.narration;
    $("narration").textContent = scene.narration;
    $("choices").replaceChildren(
      ...scene.choices.map((c, i) => choiceButton(c.label + (c.uses_item ? `  [uses ${c.uses_item}]` : ""), () => story.choose(i))),
    );
    renderMeters(run);
  },
  renderOutcome(choice, run) {
    $("narration").textContent = choice.outcome_narration;
    $("scene-text").textContent = choice.outcome_narration;
    $("choices").replaceChildren();
    renderMeters(run);
  },
  renderEnding(scene, run) {
    $("ending").hidden = false;
    $("ending").replaceChildren(
      Object.assign(document.createElement("h3"), { textContent: scene.ending_title ?? "The End" }),
      choiceButton("New run", () => {
        $("scene").hidden = true;
        $("setup").hidden = false;
        $("narration").textContent = "Pick 3 items and start a new run.";
        renderMeters(null);
      }),
    );
    renderMeters(run);
  },
  directorStatus(text) {
    $("director").textContent = text;
  },
  error(message, retry) {
    log("error", message);
    $("director").textContent = `Error: ${message}`;
    if (retry) $("choices").replaceChildren(choiceButton("Retry Director", retry));
  },
};

const story = createStory({ player, ui, log });

for (const item of ITEMS) {
  const label = document.createElement("label");
  label.className = "check";
  label.innerHTML = `<input type="checkbox" value="${item}" /> ${item}`;
  $("items").append(label);
}
$("items").addEventListener("change", () => {
  const boxes = [...$("items").querySelectorAll("input")];
  const picked = boxes.filter((b) => b.checked).length;
  for (const b of boxes) b.disabled = !b.checked && picked >= 3;
});

$("start-run").onclick = () => {
  const items = [...$("items").querySelectorAll("input:checked")].map((b) => b.value);
  if (items.length !== 3) return ui.error("Pick exactly 3 items.");
  if (!player.isReady()) return ui.error("Connect to Visko first.");
  $("director").textContent = "";
  log("run", `new run with ${items.join(", ")}`);
  story.start(items, {
    maxDecisions: Number($("max-decisions").value) || 6,
    outcomeSeconds: Number($("outcome-s").value) || 0,
  });
};

// ---- Manual UI ----
async function manual(action) {
  try {
    await action();
  } catch (e) {
    log("error", e.message);
  }
}
$("m-morph").onclick = () => manual(() => player.morph($("m-prompt").value, $("m-audio").value || null));
$("m-cut").onclick = () => manual(() => player.cut($("m-prompt").value, $("m-audio").value || null, $("m-image").files[0]));
$("m-pause").onclick = () => manual(() => player.pause());
$("m-resume").onclick = () => manual(() => player.resume());
$("m-reset").onclick = () => manual(() => player.reset());

// ---- Shared ----
const syncSettings = () => {
  player.settings.styleLine = $("style").value;
  player.settings.autoContinue = $("auto-continue").checked;
};
$("style").oninput = syncSettings;
$("auto-continue").onchange = syncSettings;
syncSettings();

for (const tab of document.querySelectorAll(".tabs button")) {
  tab.onclick = () => {
    for (const t of document.querySelectorAll(".tabs button")) t.classList.toggle("active", t === tab);
    $("tab-story").hidden = tab.dataset.tab !== "story";
    $("tab-manual").hidden = tab.dataset.tab !== "manual";
  };
}

for (const name of MODEL_NAMES) $("model").append(new Option(name.replace("reactor/", ""), name));
$("connect").onclick = () => {
  if (player.isConnecting()) return player.cancelConnect();
  manual(() => player.connect($("model").value)).then(syncConnectButton);
};
$("disconnect").onclick = () => manual(() => player.disconnect()).then(syncConnectButton);
syncConnectButton();
$("video").onclick = () => { $("video").play(); $("audio").play(); };
window.addEventListener("pagehide", () => player.disconnect());

$("clear-log").onclick = () => { logEntries.length = 0; $("log").replaceChildren(); };
$("export").onclick = () => {
  const data = { exportedAt: new Date().toISOString(), styleLine: player.settings.styleLine, run: story.getRun(), log: logEntries };
  const a = document.createElement("a");
  a.href = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: "application/json" }));
  a.download = `visko-run-${Date.now()}.json`;
  a.click();
  URL.revokeObjectURL(a.href);
};
