"""Game state and rules for "Fly Me to the Moon". No AI in this file.

The game runs through six checkpoints with one decision each, then a verdict.
After the decisions at Campus, Launch Site and Low Orbit a quest stops
the player. Its one skip item ends it at once (POST /use_item); otherwise the
player does the quest's task in Unity until they pass (POST /quest_result).

The LLM judges each choice (speed, wellness_change, score_note, payoff) and
writes the narration and video prompts; this module does all the arithmetic,
moves the player between checkpoints, resolves quests, decides the
ending, picks the key moments, and assembles the ordered list of videos. The
LLM calls are injected into `choose()` as callables, so the rules can be
tested without any network.
"""

import copy
import re
import uuid
from dataclasses import dataclass, field
from typing import Callable, Optional

START_DISTANCE = 238900
START_WELLNESS = 100
ALLOWED_ITEMS = ["alarm clock", "red bull", "flashlight", "cs textbook", "mystery bag"]
GRAB_COUNT = 3  # the player must grab exactly this many
# Opens every video prompt. The orange overheated sky belongs to the Earth places
# only (Dorm Room, Campus, Launch Site), so it lives in their visuals, not here.
STYLE = "Claymation style, a college student in a red hoodie. "
# How each item looks on screen. Video prompts never name a brand.
ITEM_LOOK = {
    "alarm clock": "an old wind-up alarm clock",
    "red bull": "a silver-and-blue energy drink can",
    "flashlight": "a yellow flashlight",
    "cs textbook": "a thick computer science textbook",
    "mystery bag": "a crumpled brown paper bag",
}

# Miles a choice covers at speed 5 on each LLM decision.
BEAT_SIZE = {
    "escape campus": 6000,
    "build the rocket": 110000,
    "in space": 50000,
    "deep space": 40000,
    "final approach": 30000,
}
SPEED_RANGE = (1, 10)
WELLNESS_CHANGE_RANGE = (-12, 5)
FREE_WIN_PENALTY = 3  # dominating choice drops to 3 below the other's wellness_change
PACE_SPEED = 5
PACE_MARGIN = 15000

GOOD_WELLNESS = 85
TOW_RANGE = 40000

# ---------- the six checkpoints ----------
# decision: "fixed" (roommate) or "llm". "after" names the quest that
# stops the player right after this checkpoint's decision, at this checkpoint.
CHECKPOINTS = [
    {
        "number": 1, "name": "Dorm Room", "decision": "fixed",
        "visual": "A small sweltering college dorm room with melting posters, an orange "
        "overheated sky glowing through the window",
        "after": None,
    },
    {
        "number": 2, "name": "Campus", "decision": "llm",
        "visual": "A sun-scorched college quad with wilting palm trees and a boiling "
        "fountain under an orange overheated sky",
        "beat": "escape campus", "encounter": True,
        "hint": "The player is trying to get off campus, and someone on the quad is in "
        "the way.",
        "after": "heat nap",
    },
    {
        "number": 3, "name": "Launch Site", "decision": "llm",
        "visual": "A cracked plaza turned into a makeshift rocket launch pad under an "
        "orange overheated sky",
        "beat": "build the rocket", "encounter": False,
        "hint": "The player must build a homemade rocket here and get it ready to launch. "
        "Only if they "
        "are in items_held, items can be spent here: red bull is the best rocket fuel, cs "
        "textbook is a pilot upgrade (the student can actually fly), mystery bag becomes a "
        "random nose cone. If Kyle came along he can be a rocket part.",
        "after": "blackout",
    },
    {
        "number": 4, "name": "Low Orbit", "decision": "llm",
        "visual": "A homemade rocket floating above a glowing orange Earth",
        "beat": "in space", "encounter": True,
        "hint": "The rocket is in low orbit, and another escapee, a stowaway, a rival or "
        "something strange floating in space gets in the way.",
        "after": "engine stall",
    },
    {
        "number": 5, "name": "Deep Space", "decision": "llm",
        "visual": "A homemade rocket drifting through dark space, Earth a small orange dot "
        "behind it",
        "beat": "deep space", "encounter": False,
        "hint": "The rocket is crossing deep space toward the Moon, far from Earth, and "
        "the player faces one big problem out here.",
        "after": None,
    },
    {
        "number": 6, "name": "Moon Approach", "decision": "llm",
        "visual": "A homemade rocket closing in on the huge pale Moon, craters visible below",
        "beat": "final approach", "encounter": False,
        "hint": "The rocket is closing in on the Moon for the final approach. The player is "
        "approaching, not arriving; the game decides later whether they land. Kyle needs help "
        "on this final approach, even if he escaped separately: offer one generous choice "
        "toward Kyle and one selfish choice.",
        "after": None,
    },
]
CHECKPOINT_TOTAL = len(CHECKPOINTS)
# Where the verdict video plays: on the Moon, or back in Deep Space.
MOON_SURFACE = {"name": "The Moon",
                "visual": "The grey lunar surface, with Earth glowing orange above the "
                "horizon"}

# Everything the player reads is kept short: a sentence or two at most.
OPENING = (
    "Breaking news: Earth just hit 451 degrees. Lake Lag has boiled, which is impressive, "
    "because it was already empty. The Moon is the only place with decent weather. "
    "Good luck."
)
ROOMMATE_SCENE = "Kyle is asleep while the dorm melts. Wake him or leave him?"

# ---------- fixed video prompts ----------
# Final prompt, as sentences joined by spaces:
#   STYLE, place visual, setting, the items still held (see items_visual), the action.
# Third person, no dialogue, no on-screen text, no real people, places or brands.
ROOMMATE_SETTING = "The student has a backpack, and a roommate is asleep in the other bed."
ROOMMATE_SCENE_VISKO = (
    "The student zips up the stuffed backpack, drenched in sweat, then turns toward the "
    "roommate snoring under a blanket while the window glows orange."
)
# The last decision's outcome: the chosen action (written by the LLM with the final
# turn, one per choice), then this fixed tail so every ending clip can open "From the
# dark cabin". FINAL_OUTCOME_VISKO is used alone only if a choice has no outcome.
FINAL_OUTCOME_TAIL = (
    "Then the engines sputter out and the cabin falls dark and silent, ending on the "
    "student's face lit only by the glow of the Moon in the window."
)
FINAL_OUTCOME_NARRATION_TAIL = "Then your engines die."
FINAL_OUTCOME_VISKO = (
    "The student grips the controls as the engines sputter out and the cabin falls dark "
    "and silent, ending on the student's face lit only by the glow of the Moon in the window."
)
ENDING_VISKO = {
    "Moon Citizen": "From the dark cabin, the rocket settles gently onto the lunar surface, "
    "and the student steps out to a cheering crowd where someone pats an empty seat.",
    "Moon Reject": "From the dark cabin, the rocket bumps down onto the Moon, and the "
    "student climbs out to face a stern panel of robed moon residents pointing toward a "
    "dusty, empty parking lot.",
    "Rescued": "From the dark cabin, a tow cable smacks against the window, and a small "
    "ship hauls the dead rocket down onto the lunar surface while its pilot waves a bag of "
    "snacks.",
    "Too Far to Tow": "From the dark cabin, the student watches a line of distant ships "
    "glide past, their passengers waving from lit windows, as the rocket drifts alone in "
    "the black.",
    "Stranded": "From the dark cabin, the student holds a glowing phone to one ear and "
    "waits, as the dead rocket drifts slowly through empty, silent space.",
}
# Internal choice shape (shared with scored LLM choices): label, miles,
# wellness_change, light, score_note, uses_item. Only the label is ever sent
# to the client. Both roommate outcomes end on the same image, so one Campus
# scene can open from either.
ROOMMATE_CHOICES = [
    {
        "label": "Wake him up",
        "outcome_visko": "The student shakes the snoring roommate awake and the two grab "
        "their backpacks, ending on the dorm room door swinging shut behind them.",
        "outcome_narration": "Kyle groans awake and follows you out.",
        "miles": 0,
        "wellness_change": 0,
        "light": "green",
        "score_note": "for waking your roommate",
        "uses_item": None,
        "woke_kyle": True,
        "history": "Dorm Room: woke up roommate Kyle, he came along",
    },
    {
        "label": "Leave him",
        "outcome_visko": "The student tiptoes past the sleeping roommate with a backpack, "
        "ending on the dorm room door swinging shut behind the student.",
        "outcome_narration": "You slip out. Kyle keeps snoring.",
        "miles": 0,
        "wellness_change": -2,
        "light": "red",
        "score_note": "for leaving Kyle to cook",
        "uses_item": None,
        "woke_kyle": False,
        "history": "Dorm Room: left roommate Kyle asleep in the dorm",
    },
]

# ---------- quests ----------
# Each quest has one item that skips it. A player who holds it is asked
# whether to use it (POST /use_item); otherwise, or if they decline, they play a
# short task in Unity until they pass it, then Unity calls POST /quest_result.
# Player-read text (narration, quest instructions, results) stays short.
# lead_in: what the outcome video before it should end on, told to the LLM.
# skip / passed: (narration, video prompt) for each way it can end. The player keeps
# trying the quest until they pass, so there is no failure and no penalty; the skip
# item just saves them the quest.
QUESTS = {
    "heat nap": {
        "lead_in": "the student slowing down in the heat, still on campus",
        "narration": "The heat is knocking you out.",
        "visko": "The student slows to a stagger in the shimmering heat, eyelids drooping, "
        "about to collapse.",
        "skip_item": "alarm clock",
        "task": {"id": "throw_ball",
                  "instructions": "Throw the ball at the target to wake up!"},
        "skip": ("Your alarm clock blares. You're up!",
                 "A wind-up alarm clock in the backpack rattles wildly, and the student "
                 "bolts upright, wide awake."),
        "passed": ("Bullseye! You're wide awake.",
                   "The student hurls a ball at a clanging metal target and jolts wide "
                   "awake."),
    },
    "blackout": {
        "lead_in": "the launch pad lights starting to flicker while the rocket is still "
                   "sitting on the pad",
        "narration": "The launch pad goes pitch black.",
        "visko": "Every light around the launch pad flickers and dies, leaving the student "
        "frozen beside the dark launch computer.",
        "skip_item": "flashlight",
        "task": {"id": "crank_generator",
                  "instructions": "Crank the generator to get the lights back!"},
        "skip": ("Your flashlight finds the reset button.",
                 "A flashlight beam cuts through the dark onto a giant red button, and the "
                 "student slams it as the screens flicker back on."),
        "passed": ("The lights roar back on.",
                   "The student spins the hand crank of a rusty generator faster and faster "
                   "until the launch pad lights stutter back on."),
    },
    "engine stall": {
        "lead_in": "the engine sputtering, still in low orbit",
        "narration": "Your engine sputters and dies.",
        "visko": "The rocket's engine coughs out a last puff of smoke and goes silent, and "
        "the student stares at the dead controls.",
        "skip_item": "red bull",
        "task": {"id": "pull_cord",
                  "instructions": "Yank the starter cord until the engine catches!"},
        "skip": ("Red Bull gives your rocket wings!",
                 "The student pours a can of energy drink into the fuel line, and the engine "
                 "roars back to life."),
        "passed": ("The engine coughs, then roars.",
                   "The student yanks a starter cord again and again until the rocket's "
                   "engine coughs and roars back to life."),
    },
}
ITEM_DISPLAY = {"alarm clock": "alarm clock", "red bull": "Red Bull",
                "flashlight": "flashlight", "cs textbook": "CS textbook",
                "mystery bag": "mystery bag"}

ENDING_TEXT = {
    "Moon Citizen": "You made it, and so did everyone you helped. Someone saved you a seat.",
    "Moon Reject": "You made it. The Moon voted no. Enjoy the parking lot.",
    "Rescued": "Out of fuel. Then a tow line hits your window: someone you helped.",
    "Too Far to Tow": "Out of fuel, far from anyone. People you helped wave as they pass.",
    "Stranded": "Out of fuel. You call everyone you met. Voicemail.",
}
ON_MOON_ENDINGS = {"Moon Citizen", "Moon Reject", "Rescued"}
KYLE_LINES = {
    (True, True): "Kyle climbs out behind you and asks if there's Wi-Fi.",
    (True, False): "Kyle is already here, holding a smoothie.",
    (False, True): "Kyle floats beside you, still asleep.",
    (False, False): "Through the window you can see Kyle on the Moon, waving.",
}  # key: (on_moon, woke_kyle)
# The same moment in the ending video (videos never use names).
KYLE_VISKO = {
    (True, True): "The sleepy roommate climbs out behind the student.",
    (True, False): "The roommate is already standing nearby, holding a smoothie.",
    (False, True): "The roommate floats asleep beside the student.",
    (False, False): "Far away on the Moon, the roommate stands waving.",
}

LIGHT_SEVERITY = {None: -1, "green": 0, "yellow": 1, "red": 2}


class GameError(Exception):
    """Bad request against game rules (e.g. invalid choice, game already over)."""


@dataclass
class GameState:
    items: list
    session_id: str = field(default_factory=lambda: uuid.uuid4().hex)
    distance: int = START_DISTANCE
    wellness: int = START_WELLNESS
    checkpoint: int = 0  # index into CHECKPOINTS where the player now stands
    mode: str = "decision"  # "decision", "quest" or "verdict"
    quest: Optional[str] = None  # the quest waiting for /use_item
    # During a quest: the next checkpoint's scene parts and choices. The scene
    # video is assembled only after /use_item, so it shows the items still held then.
    held: Optional[dict] = None
    # Items the player has used: discarded from `items` for good and marked "used".
    items_used: list = field(default_factory=list)
    # The scene the player is deciding on now (text and video prompt), so the LLM can
    # write the outcome of the choice as a continuation of it.
    last_scene: dict = field(default_factory=dict)
    history: list = field(default_factory=list)  # short story notes for the LLM
    decisions: list = field(default_factory=list)  # one log entry per decision
    pending_choices: list = field(default_factory=list)  # full dicts incl. hidden scores
    last_setting: str = ROOMMATE_SETTING  # setting of the most recent turn's videos
    woke_kyle: bool = False
    final_kyle_generous: bool = False
    emergencies_with_right_item: int = 0
    payoff_used: bool = False
    game_over: bool = False
    ending_title: Optional[str] = None
    ending: Optional[str] = None
    verdict: Optional[dict] = None


TRAVELER_ARCHETYPES = {
    "The Guardian": "You never put yourself first. Everyone you met made it because of you.",
    "The Redeemed": "You made a mistake in the first minute and spent the rest of the journey making it right.",
    "The Loyal One": "You would do anything for your own people. Strangers had to earn it.",
    "The Prepared": "You packed for the worst and it came. Nothing on the way caught you off guard.",
    "The Survivor": "You weren't perfect, and you knew it. You did enough good to be let in.",
}


def traveler_archetype(state):
    if state.wellness < 85:
        return None
    bad = [d for d in state.decisions if d.get("verdict") == "bad"]
    if not bad:
        return "The Guardian"
    if not state.woke_kyle and state.final_kyle_generous:
        return "The Redeemed"
    if state.woke_kyle and state.final_kyle_generous and any(
            d.get("checkpoint_number") in (2, 4, 5) for d in bad):
        return "The Loyal One"
    if state.emergencies_with_right_item == 3:
        return "The Prepared"
    return "The Survivor"


# ---------- pure helpers ----------

def clamp(v, lo, hi):
    return max(lo, min(hi, v))


def miles_gained(beat_name: str, speed: int) -> int:
    return round(BEAT_SIZE[beat_name] * (0.7 + 0.06 * speed))


def light_for(wellness_change: int) -> str:
    if wellness_change >= 0:
        return "green"
    if wellness_change >= -4:
        return "yellow"
    return "red"


def _sentence(text: str) -> str:
    """Trimmed, first letter capitalised, ending in . ! or ? ("" stays "")."""
    text = (text or "").strip()
    if not text:
        return ""
    text = text[0].upper() + text[1:]
    return text if text[-1] in ".!?" else text + "."


def items_visual(items) -> str:
    """The items still held, as they look on screen, for every video prompt."""
    looks = [ITEM_LOOK[i] for i in items if i in ITEM_LOOK]
    if not looks:
        return ""
    listed = looks[0] if len(looks) == 1 else ", ".join(looks[:-1]) + " and " + looks[-1]
    return f"The student's backpack holds {listed}."


def video(kind: str, place: dict, setting: str, prompt: str, narration: str,
          items=()) -> dict:
    """One Visko clip. `items` are the items held during this clip."""
    parts = [STYLE, place["visual"], setting, items_visual(items), prompt]
    return {
        "kind": kind,
        "prompt": " ".join(s for s in (_sentence(p) for p in parts) if s),
        "narration": narration,
    }


def validate_items(items) -> list:
    """The player must grab exactly three different items."""
    items = [str(i).strip().lower() for i in items]
    if len(items) != GRAB_COUNT or len(set(items)) != GRAB_COUNT:
        raise GameError(f"Pick exactly {GRAB_COUNT} different items.")
    bad = [i for i in items if i not in ALLOWED_ITEMS]
    if bad:
        raise GameError(f"Unknown item(s): {bad}. Allowed: {ALLOWED_ITEMS}")
    return items


def compute_pace(distance: int, checkpoint_index: int) -> str:
    """Remaining miles if every LLM decision from checkpoint_index on were speed 5."""
    ahead_miles = sum(miles_gained(c["beat"], PACE_SPEED)
                      for c in CHECKPOINTS[checkpoint_index:] if c["decision"] == "llm")
    left = distance - ahead_miles
    if left > PACE_MARGIN:
        return "behind"
    if left < -PACE_MARGIN:
        return "ahead"
    return "on track"


def score_choices(state: "GameState", beat_name: str, raw_choices: list) -> list:
    """Clamp the LLM's scores, apply the no-free-wins rule, compute miles and light."""
    choices = []
    for c in raw_choices:
        choices.append({
            "label": c["label"],
            "score_note": c["score_note"],
            "uses_item": c["uses_item"],
            "payoff": bool(c["payoff"]),
            "verdict": c.get("verdict", "good" if c["wellness_change"] >= 0 else "bad"),
            "generous": c.get("generous", c["wellness_change"] >= 0),
            "speed": clamp(int(round(float(c["speed"]))), *SPEED_RANGE),
            "wellness_change": clamp(int(round(float(c["wellness_change"]))),
                                     *WELLNESS_CHANGE_RANGE),
        })
        # The last decision's choices carry their own outcome (see FINAL_OUTCOME_TAIL).
        if c.get("outcome_visko_prompt"):
            choices[-1]["final_outcome_narration"] = c["outcome_narration"]
            choices[-1]["final_outcome_visko"] = c["outcome_visko_prompt"]

    # No free wins. Only one choice can have strictly higher speed.
    a, b = choices
    if b["speed"] > a["speed"]:
        a, b = b, a
    if a["speed"] > b["speed"] and a["wellness_change"] >= b["wellness_change"]:
        if a["payoff"] and not state.payoff_used:
            state.payoff_used = True
        else:
            a["wellness_change"] = b["wellness_change"] - FREE_WIN_PENALTY

    for c in choices:
        c["miles"] = miles_gained(beat_name, c["speed"])
        c["light"] = light_for(c["wellness_change"])
    return choices


def format_moment(decision: dict) -> str:
    # "you chose to <label>": lowercase only the first letter of the leading verb, so
    # names and items keep their case ("feed Cart", "Red Bull"; "CS ..." stays).
    label = decision["label"]
    if label[1:2].islower():
        label = label[0].lower() + label[1:]
    return (f"{decision['checkpoint']}: you chose to {label} "
            f"({decision['wellness_change']:+d}% wellness)")


def key_moments(decisions: list) -> list:
    """Lowest wellness_change, highest wellness_change, most miles; skip duplicates
    and fill from the remaining decisions in order. Ties go to the earliest."""
    if not decisions:
        return []
    order = range(len(decisions))
    wanted = [
        min(order, key=lambda i: decisions[i]["wellness_change"]),
        max(order, key=lambda i: decisions[i]["wellness_change"]),
        max(order, key=lambda i: decisions[i]["miles"]),
    ]
    picked = []
    for i in wanted + list(order):
        if i not in picked:
            picked.append(i)
        if len(picked) == 3:
            break
    return [format_moment(decisions[i]) for i in picked]


def quest_info_for_client(state) -> Optional[dict]:
    """What Unity needs while a quest waits: offer the skip item if the player
    holds it (POST /use_item), otherwise run its task (POST /quest_result)."""
    if state.mode != "quest":
        return None
    d = QUESTS[state.quest]
    item = d["skip_item"]
    has_item = item in state.items
    return {
        "name": state.quest,
        "narration": d["narration"],
        "skip_item": item,
        "has_skip_item": has_item,
        "skip_prompt": f"Use your {ITEM_DISPLAY[item]} to skip it?" if has_item else None,
        "task": dict(d["task"]),
    }


def _response(state, outcome_narration, videos, light, next_scene,
              distance_change=0, wellness_change=0, score_note=""):
    cp = CHECKPOINTS[state.checkpoint]
    return {
        "session_id": state.session_id,
        "mode": state.mode,
        "checkpoint_number": cp["number"],
        "checkpoint_name": cp["name"],
        "checkpoint_total": CHECKPOINT_TOTAL,
        "quest_name": state.quest,
        "quest": quest_info_for_client(state),
        "outcome_narration": outcome_narration,
        "videos": videos,
        "distance": state.distance,
        "wellness": state.wellness,
        "light": light,
        "distance_change": distance_change,
        "wellness_change": wellness_change,
        "score_note": score_note,
        "items": list(state.items),  # still held
        "items_used": list(state.items_used),  # used and discarded, in order
        "delay_seconds": 0,  # kept for clients; nothing delays the player any more
        "next_scene": next_scene,
        # labels only; scores for upcoming choices stay on the server
        "choices": [c["label"] for c in state.pending_choices],
        # The item each choice would use up (or null), e.g. to show its icon.
        "choice_items": [c.get("uses_item") for c in state.pending_choices],
        "game_over": state.game_over,
        "ending_title": state.ending_title,
        "ending": state.ending,
        "verdict": copy.deepcopy(state.verdict),
    }


def _atomic(state: GameState, step: Callable, *args) -> dict:
    """All-or-nothing: if `step` raises, the session is left untouched."""
    snapshot = copy.deepcopy(state)
    try:
        return step(state, *args)
    except Exception:
        state.__dict__.update(snapshot.__dict__)
        raise


# ---------- game flow ----------

# Before the game: a pre-recorded 20-second opening video (the breaking-news broadcast)
# plays in Unity. Five seconds in, a 15-second countdown starts, and the player must
# grab three items before it ends. Then Unity calls /start with them.
OPENING_VIDEO_SECONDS = 20
COUNTDOWN_STARTS_AT = 5
GRAB_SECONDS = 15


def intro() -> dict:
    """The opening timeline and the items to grab; Unity needs it before /start.
    `opening` is the broadcast's text, for subtitles (the video itself is pre-recorded)."""
    return {"opening": OPENING, "opening_video_seconds": OPENING_VIDEO_SECONDS,
            "countdown_starts_at_second": COUNTDOWN_STARTS_AT,
            "timer_seconds": GRAB_SECONDS, "items": list(ALLOWED_ITEMS),
            "grab_count": GRAB_COUNT, "distance": START_DISTANCE,
            "wellness": START_WELLNESS}


def new_game(items) -> tuple:
    state = GameState(items=validate_items(items))
    state.pending_choices = [dict(c) for c in ROOMMATE_CHOICES]
    state.last_scene = {"next_scene": ROOMMATE_SCENE,
                        "scene_visko_prompt": ROOMMATE_SCENE_VISKO}
    videos = [video("scene", CHECKPOINTS[0], ROOMMATE_SETTING, ROOMMATE_SCENE_VISKO,
                    ROOMMATE_SCENE, state.items)]
    # The broadcast already played in the pre-recorded opening, so it is not repeated.
    return state, _response(state, "", videos, None, ROOMMATE_SCENE)


def decide_ending(state) -> None:
    landed = state.distance <= 0
    good = state.wellness >= GOOD_WELLNESS
    if landed:
        title = "Moon Citizen" if good else "Moon Reject"
    elif good and state.distance <= TOW_RANGE:
        title = "Rescued"
        state.distance = 0
    elif good:
        title = "Too Far to Tow"
    else:
        title = "Stranded"
    state.distance = max(0, state.distance)
    kyle = KYLE_LINES[(title in ON_MOON_ENDINGS, state.woke_kyle)]
    state.game_over = True
    state.mode = "verdict"
    state.pending_choices = []
    state.ending_title = title
    state.ending = f"{ENDING_TEXT[title]} {kyle}"


def ending_place(title: str) -> dict:
    """On-Moon endings play on the lunar surface; the others back in Deep Space."""
    return MOON_SURFACE if title in ON_MOON_ENDINGS else CHECKPOINTS[4]


def choose(state: GameState, choice_index: int, get_turn: Callable,
           get_verdict: Optional[Callable] = None) -> dict:
    """Apply a decision. At Campus, Launch Site and Low Orbit this stops at a
    quest (mode "quest"); at Moon Approach it ends the game with the
    verdict; otherwise it moves on to the next decision.

    get_turn(beat_name, hint, encounter, last_choice, items, pace, history,
    outcome_checkpoint, scene_checkpoint, fixed_outcome_visko, quest_follows,
    items_used, just_used, choice_outcomes, previous_scene, kyle_came_along) returns a
    validated turn.
    get_verdict(ending_title, ending, final_distance, final_wellness, decisions, place,
    items, traveler_type, traveler_blurb) returns {traveler_detail} or None.
    """
    if state.game_over:
        raise GameError("Game is over. Start a new one.")
    if state.mode == "quest":
        raise GameError("A quest is waiting: call /use_item (skip item) or "
                        "/quest_result first.")
    if choice_index not in (0, 1) or len(state.pending_choices) != 2:
        raise GameError("choice_index must be 0 or 1.")
    return _atomic(state, _apply_choice, choice_index, get_turn, get_verdict)


def use_item(state: GameState, item: Optional[str]) -> dict:
    """Skip the waiting quest with its item. The item is used up."""
    if state.mode != "quest":
        raise GameError("No quest is waiting.")
    item = item.strip().lower() if isinstance(item, str) else item
    skip_item = QUESTS[state.quest]["skip_item"]
    if item != skip_item:
        raise GameError(f"Only the {skip_item} skips the {state.quest}. Without it, "
                        "do the task and call /quest_result.")
    if item not in state.items:
        raise GameError(f"You don't have the {item}. Do the task and call /quest_result.")
    return _atomic(state, _resolve_quest, "skip")


def quest_result(state: GameState, passed=True) -> dict:
    """Finish the waiting quest once the player has passed the Unity quest.
    Players retry until they pass, so only passed=true is accepted."""
    if state.mode != "quest":
        raise GameError("No quest is waiting.")
    if passed is not True:
        raise GameError("The quest ends only when the player passes; let them keep "
                        "trying, then send passed=true.")
    return _atomic(state, _resolve_quest, "passed")


def _quest_info(name: Optional[str], items: list) -> Optional[dict]:
    """What the LLM is told about the quest after this decision. The next scene is
    written now but shown after the player may have spent the skip item on it."""
    if not name:
        return None
    d = QUESTS[name]
    return {"name": name, "outcome_ends_on": d["lead_in"],
            "may_use_up": [d["skip_item"]] if d["skip_item"] in items else []}


def _place(cp: dict) -> dict:
    return {"name": cp["name"], "description": cp["visual"]}


def _apply_choice(state, choice_index, get_turn, get_verdict) -> dict:
    cp = CHECKPOINTS[state.checkpoint]
    choice = state.pending_choices[choice_index]
    items_before = list(state.items)  # the outcome clip may show the item being used

    before_distance, before_wellness = state.distance, state.wellness
    state.distance = max(0, state.distance - choice["miles"])
    state.wellness = clamp(state.wellness + choice["wellness_change"], 0, 100)
    extras = dict(distance_change=state.distance - before_distance,
                  wellness_change=state.wellness - before_wellness,
                  score_note=choice.get("score_note", ""))

    state.decisions.append({
        "checkpoint": cp["name"],
        "checkpoint_number": cp["number"],
        "verdict": choice.get("verdict", "good" if choice["wellness_change"] >= 0 else "bad"),
        "label": choice["label"],
        "miles": choice["miles"],
        "wellness_change": choice["wellness_change"],
        "score_note": choice.get("score_note", ""),
    })
    if "woke_kyle" in choice:
        state.woke_kyle = choice["woke_kyle"]
    item = choice.get("uses_item")
    just_used = None
    if item and item in state.items:
        state.items.remove(item)
        state.items_used.append(item)
        state.history.append(f"used the {item} (discarded)")
        just_used = item
    note = f" ({choice['score_note']})" if choice.get("score_note") else ""
    state.history.append(choice.get("history") or f"{cp['name']}: {choice['label']}{note}")

    if state.checkpoint == CHECKPOINT_TOTAL - 1:
        state.final_kyle_generous = choice.get("generous", choice["wellness_change"] >= 0)
        return _finish(state, cp, choice, extras, get_verdict, items_before)

    ncp = CHECKPOINTS[state.checkpoint + 1]
    qname = cp["after"]
    turn = get_turn(
        beat_name=ncp["beat"],
        hint=ncp["hint"],
        encounter=ncp["encounter"],
        last_choice=choice["label"],
        items=list(state.items),
        pace=compute_pace(state.distance, state.checkpoint + 1),
        history=list(state.history),
        outcome_checkpoint=_place(cp),
        scene_checkpoint=_place(ncp),
        # The roommate outcome video is fixed; the LLM must start its scene from it.
        fixed_outcome_visko=choice.get("outcome_visko"),
        quest_follows=_quest_info(qname, state.items),
        items_used=list(state.items_used),
        just_used=just_used,
        # The scene the player just decided on; the outcome continues from it.
        previous_scene=None if choice.get("outcome_visko") else dict(state.last_scene),
        kyle_came_along=state.woke_kyle,
        # The last decision's choices come with their own outcomes.
        choice_outcomes=state.checkpoint + 1 == CHECKPOINT_TOTAL - 1,
    )
    next_choices = score_choices(state, ncp["beat"], turn["choices"])
    setting = turn["setting"]
    # Kyle, once woken, is in every clip; fallback or forgetful turns may leave him out.
    if state.woke_kyle and not re.search(r"\b(roommate|kyle)\b", setting, re.IGNORECASE):
        setting = f"{_sentence(setting)} The roommate rides along with the student."
    state.last_setting = setting

    if choice.get("outcome_visko"):  # the roommate beat's fixed outcome and narration
        outcome_narration = choice["outcome_narration"]
        outcome = video("outcome", cp, ROOMMATE_SETTING, choice["outcome_visko"],
                        outcome_narration, items_before)
    else:
        outcome_narration = turn["outcome_narration"]
        outcome = video("outcome", cp, setting, turn["outcome_visko_prompt"],
                        outcome_narration, items_before)

    if qname:
        # Stop here; the next checkpoint's scene and choices wait for /use_item.
        d = QUESTS[qname]
        state.mode, state.quest = "quest", qname
        state.held = {"choices": next_choices, "setting": setting,
                      "scene_prompt": turn["scene_visko_prompt"],
                      "next_scene": turn["next_scene"]}
        state.last_scene = {}  # nothing to decide on until the quest is over
        state.pending_choices = []
        videos = [outcome, video("quest", cp, setting, d["visko"], d["narration"],
                                 state.items)]
        return _response(state, outcome_narration, videos, choice["light"], "", **extras)

    scene = video("scene", ncp, setting, turn["scene_visko_prompt"], turn["next_scene"],
                  state.items)
    state.checkpoint += 1
    state.pending_choices = next_choices
    state.last_scene = {"next_scene": turn["next_scene"],
                        "scene_visko_prompt": turn["scene_visko_prompt"]}
    return _response(state, outcome_narration, [outcome, scene], choice["light"],
                     turn["next_scene"], **extras)


def _resolve_quest(state, how) -> dict:
    """how: "skip" (used the skip item) or "passed" (beat the Unity quest)."""
    name = state.quest
    d = QUESTS[name]
    cp = CHECKPOINTS[state.checkpoint]
    before_distance, before_wellness = state.distance, state.wellness
    items_before = list(state.items)  # the skip clip shows the item being used
    narration, prompt = d[how]

    if how == "skip":
        state.emergencies_with_right_item += 1
        item = d["skip_item"]
        state.items.remove(item)
        state.items_used.append(item)
        light, note = "green", f"skipped with your {ITEM_DISPLAY[item]}"
        state.history.append(f"{name}: skipped it with the {item} (used, discarded)")
    else:
        light, note = "green", "quest passed"
        state.history.append(f"{name}: beat it the hard way")

    held = state.held
    result = video("quest_result", cp, held["setting"], prompt, narration,
                   items_before)
    scene = video("scene", CHECKPOINTS[state.checkpoint + 1], held["setting"],
                  held["scene_prompt"], held["next_scene"], state.items)
    state.checkpoint += 1
    state.mode, state.quest, state.held = "decision", None, None
    state.pending_choices = held["choices"]
    state.last_scene = {"next_scene": held["next_scene"],
                        "scene_visko_prompt": held["scene_prompt"]}
    return _response(state, narration, [result, scene], light, held["next_scene"],
                     distance_change=state.distance - before_distance,
                     wellness_change=state.wellness - before_wellness, score_note=note)


def _finish(state, cp, choice, extras, get_verdict, items_before) -> dict:
    decide_ending(state)
    title = state.ending_title
    end_place = ending_place(title)
    moments = key_moments(state.decisions)

    traveler_type = traveler_archetype(state)
    blurb = TRAVELER_ARCHETYPES.get(traveler_type, "")
    detail = ""
    ending_prompt = ENDING_VISKO[title]
    if get_verdict and traveler_type:
        try:
            result = get_verdict(
                ending_title=title, ending=state.ending, final_distance=state.distance,
                final_wellness=state.wellness, decisions=copy.deepcopy(state.decisions),
                place=_place(end_place), items=list(state.items),
                traveler_type=traveler_type, traveler_blurb=blurb)
            if result:
                candidate = result.get("traveler_detail", "")
                if (isinstance(candidate, str) and len(candidate.split()) <= 20
                        and re.search(r"\byou\b", candidate, re.I)
                        and len(re.findall(r"[.!?](?:\s|$)", candidate)) <= 1):
                    detail = candidate.strip()
        except Exception:
            # Optional flavor text never blocks a finished run.
            pass

    state.verdict = {
        "ending_title": title,
        "ending": state.ending,
        "final_distance": state.distance,
        "final_wellness": state.wellness,
        "traveler_type": traveler_type,
        "traveler_blurb": blurb,
        "traveler_detail": detail,
        "key_moments": moments,
    }

    # The outcome clip shows the chosen action, then the engines dying. The ending clip
    # takes no turn setting ("sits at the controls" would contradict stepping out).
    if choice.get("final_outcome_visko"):
        narration = f"{choice['final_outcome_narration']} {FINAL_OUTCOME_NARRATION_TAIL}"
        # The tail supplies the final image, so drop any "ending on ..." of its own.
        action = re.sub(r",?\s*ending on\b.*$", "", choice["final_outcome_visko"],
                        flags=re.IGNORECASE | re.DOTALL) or choice["final_outcome_visko"]
        outcome_prompt = f"{_sentence(action)} {FINAL_OUTCOME_TAIL}"
    else:
        narration = f"You commit to \"{choice['label']}\" as the engines sputter out."
        outcome_prompt = FINAL_OUTCOME_VISKO
    kyle = KYLE_VISKO[(title in ON_MOON_ENDINGS, state.woke_kyle)]
    ending_prompt = f"{_sentence(ending_prompt)} {kyle}"
    videos = [video("outcome", cp, state.last_setting, outcome_prompt, narration,
                    items_before),
              video("ending", end_place, "", ending_prompt, state.ending, state.items)]
    return _response(state, narration, videos, choice["light"], "", **extras)
