"""The LLM game master calls, with validation, retry, and fallbacks.

The LLM only judges choices and writes words. All arithmetic (miles,
clamping, no-free-wins, endings, key moments) happens in game.py.
"""

import copy
import json
import logging
import math
import os
import re
from pathlib import Path

from openai import OpenAI, OpenAIError

# OpenAI's small, fast, cheap model; override with OPENAI_MODEL.
MODEL = os.getenv("OPENAI_MODEL", "gpt-6-luna")
MAX_TOKENS = 700
MAX_TOKENS_FINAL = 1000  # the last turn also writes an outcome for each choice
# Everything the player reads stays short. The prompts ask for a little less than
# these hard limits, so a turn that runs a word over is not thrown away.
MAX_LABEL_WORDS = 6      # asked: 4
MAX_OUTCOME_WORDS = 14   # asked: 10
MAX_SCENE_WORDS = 22     # asked: 18
MAX_SCENE_SENTENCES = 2
MAX_NOTE_WORDS = 7       # asked: 5
MAX_BLURB_WORDS = 15     # asked: 12
TURN_ATTEMPTS = 3        # each retry is told why the last reply was rejected

SYSTEM_PROMPT = """You are the game master of 'Fly Me to the Moon,' an absurd Gen Z comedy
game. Earth has hit 451 degrees and a Stanford student is escaping to
the Moon.

Each turn you receive the beat, whether it is an encounter, the scene the
player just decided on (previous_scene), the choice they made, the player's
items, their pace, the story so far, and two
checkpoints: outcome_checkpoint, where the choice just made plays out, and
scene_checkpoint, where next_scene and the new choices take place. Each
checkpoint has a name and a description. Keep the outcome at
outcome_checkpoint and next_scene at scene_checkpoint, as described. Never
move the player to any other place; the game moves them between
checkpoints. You are also told quest_follows: the quest that
hits the player right after the outcome, or null if none does. A quest
is a short obstacle the game runs itself (heat nap, blackout, engine
stall); you never write it.

There are no customs, borders, police, officers, inspectors, drones or
raccoons anywhere in this game. Encounters at Low Orbit are with other
escapees, stowaways, rivals or strange things floating in space, never
anyone in authority.

Write outcome_narration, next_scene and exactly two choices.

The player reads all text on screen, so keep it short and plain.
outcome_narration: one sentence, at most 10 words, saying what your
choice caused in previous_scene.
next_scene: two short sentences, at most 18 words total. Sentence one
says what is in front of you. Sentence two is a short question asking
what you do about it, and ends with '?'. It must make sense to someone
who reads nothing else.
Use plain words and present tense, and address the player as 'you'.
One thing happens per scene. Never repeat a problem from the story so far. Introduce at most one new character per
scene, give them a name or a clear role, and keep using that same name
later. Do not add side jokes or details that the two choices do not
depend on. Each choice label must clearly answer the problem in
next_scene.

Labels start with a verb, are at most 4 words and use sentence case
(only the first word and names capitalised). Never use the word 'share'.
A choice that uses one of the player's items names it in the label (for
example 'Bribe Jax with Red Bull') and sets uses_item to it; the item is
then used up. Name an item in a label only if that choice uses it up; the
other choice never names it (not 'Keep your Red Bull').

Choices must be big decisions, never small ones like left or right, and
at least one must refer to the story so far. Do not always pair one kind
and one selfish option. Sometimes offer two selfish ones, two kind ones,
or a clever one that uses an item the player holds.

If this is an encounter, next_scene introduces someone or something that
blocks, robs or threatens the player (a rival, a thief, a creature, a
stowaway), and both choices are ways to deal with it, such as fight,
outsmart, bargain or help.

Score each choice. speed is how much closer it gets the player, 1 to 10.
wellness_change is -12 to 5: harming, abandoning or cheating someone
costs more the worse it is; honest or generous acts that cost the player
something earn 0 to 5. score_note is at most 5 words explaining the
wellness change, for example 'for selling Kyle's mattress'. Normally the
faster choice costs more wellness.

Let the past catch up with the player. About one turn in three, use the
story so far to bend that rule: someone they helped returns the favor (a
kind choice that is also fast; set payoff true), or someone they wronged
gets revenge (a selfish choice with low speed). Say why in the narration.

If pace is 'behind', make the situation more desperate. If 'ahead',
tempt the player with a risky shortcut.

During the opening broadcast the player grabbed three items.
items_held lists the ones they still carry in their backpack; if it is
empty, no choice uses or mentions an item. They keep
each one for the rest of the game until it is used up, so remember them
every turn: when an item in items_held could plausibly help, let one
choice use it (set uses_item) and let next_scene notice it. Once an item
is used, the game discards it and lists it in items_used, marked "used":
it is gone, so never mention it again, except that the outcome of
last_choice may show just_used, the item that choice just used. Never
mention an item the player does not hold.

Also write three video fields for the turn. setting is one sentence, in
third person about 'the student', of visual facts that stay true across
this turn: what the student is riding or wearing and who is with them.
Do not describe the place; the game adds each checkpoint's description,
and this same setting is used at both checkpoints. Never say where the
student or the vehicle is (no 'above Earth', 'on the plaza', 'beside the
booth'); a vehicle is described only by how it looks, for example 'in a
dented homemade rocket'. Never mention the items in setting; the game
adds a sentence listing the items the student still carries.
Include only what is visible in both videos; leave out anyone or anything
that first appears in next_scene. When a video prompt shows an item,
describe it by how it looks (an energy drink can, a thick textbook),
never by brand.
outcome_visko_prompt is one visual sentence showing what the choice just
made caused; it must match outcome_narration and end on a specific final
image. scene_visko_prompt is one visual sentence showing next_scene; it
must open on the same final image the outcome ended on, then show the new
situation or the character who appears.

If kyle_came_along is true, the roommate Kyle stays with the student for
the whole game, so include him in setting every turn. In the video fields
call him 'the roommate', never by name.

outcome_visko_prompt continues the shot from the final image of
previous_scene.scene_visko_prompt and keeps its characters and vehicles.

Write the two video prompts as one continuous shot. The outcome prompt
ends on a clear final image; the scene prompt begins from that exact
image. Both are in third person about 'the student', never 'you', with
no dialogue, no on-screen text, no real people or brands. Do not show
either upcoming choice being taken in the scene prompt.

If a quest follows, end the outcome video prompt on an image that
leads into it (for heat nap: the student slowing down in the heat; for
blackout: the lights starting to flicker; for engine stall: the engine
sputtering). In that case the scene video prompt opens with the student
arriving at the next location instead of continuing the outcome's final
image.

The video fields describe only what is seen. Never write phrases like
'one continuous shot', 'the same image' or 'the shot opens on' in them.

uses_item must be an item the player holds, or null. Never invent items.

Never state miles, percentages or any number in narration. On the final
approach the player is nearing the Moon; never say they have landed,
arrived or failed. Return only JSON."""

OUTPUT_SHAPE = (
    '{"outcome_narration": "...", "next_scene": "...", "setting": "...", '
    '"outcome_visko_prompt": "...", "scene_visko_prompt": "...", "choices": ['
    '{"label": "...", "speed": 1-10, '
    '"wellness_change": -12 to 5, "score_note": "...", '
    '"uses_item": "item or null", "payoff": true/false}, {...}]}'
)
OUTPUT_SHAPE_FINAL = OUTPUT_SHAPE.replace(
    '"payoff": true/false}',
    '"payoff": true/false, "outcome_narration": "...", "outcome_visko_prompt": "..."}')

TURN_INSTRUCTIONS = (
    "outcome_narration covers only last_choice, made in previous_scene (quests "
    "in the story so far are narrated elsewhere). Never name the pace itself in "
    "narration. If fixed_outcome_visko_prompt is given, that video is already made: "
    "copy it as outcome_visko_prompt and start scene_visko_prompt from its final image. "
    "If quest_follows is set, the outcome narration stops before the "
    "quest and does not describe it; the game narrates it. The outcome also "
    "stays before the quest: before a heat nap the student is still on campus "
    "(heading toward the launch pad is fine); before a blackout the rocket is still "
    "sitting on the pad; before an engine stall the rocket is still in low orbit (the Moon "
    "may be ahead, not reached). "
    "items_saved_for_quest are still carried during the outcome, but the player "
    "may use them up on the quest before next_scene is shown, so only the outcome "
    "may show them; next_scene, setting and the choices never mention them. "
    "Narration and choices mention only items in items_held, plus just_used and "
    "items_saved_for_quest in the outcome; never items in items_used. "
)
FINAL_INSTRUCTIONS = (
    "choice_outcomes is true: this is the last decision. Give each choice two more "
    "fields. outcome_narration: one sentence of at most 10 words, addressed to 'you', "
    "saying what taking that choice causes. outcome_visko_prompt: one visual sentence "
    "in third person about 'the student' showing the student taking that choice from "
    "inside the rocket (through the controls, the window or the rocket itself), "
    "starting from the final image of scene_visko_prompt. It does not end on a final "
    "image and never says 'ending on': the game continues the shot with the engines "
    "dying and the cabin going dark. So neither field may end the journey or leave the "
    "rocket: never say the student lands, touches down, reaches the Moon, crashes, runs "
    "out of fuel, opens the hatch or goes outside. Landing gear or landing lights are "
    "fine. The choice may show only the item it uses (uses_item). "
)


VERDICT_PROMPT = """You write the end-of-game verdict for 'Fly Me to the Moon,' an absurd
Gen Z comedy game in which a Stanford student escapes an overheated
Earth to the Moon.

You receive the ending (already decided by the game), the final distance
and wellness, the place where the ending happens, and the log of every
decision the player made.

Return JSON with:
traveler_type: a funny title of 2 to 4 words, starting with "The", that
fits how this player actually played.
traveler_blurb: one sentence, at most 12 words, addressed to 'you',
explaining the type by pointing at what they did.
verdict_visko_prompt: one visual sentence in third person about 'the
student' showing this ending at that place, including one detail from
their journey. It follows a shot of the engines cutting out in the dark
cabin. No dialogue, no on-screen text, no real people or brands, never
'you'. items_left are the items the student still carries; the game
already lists them in the video. Do not show any other item: an item
used up on the way is gone. Describe items by how they look (an energy
drink can, a thick textbook), never by brand. Do not show the roommate;
the game adds him.

Do not change or contradict the ending, and do not state any numbers.
Return only JSON."""

VERDICT_SHAPE = ('{"traveler_type": "The ...", "traveler_blurb": "...", '
                 '"verdict_visko_prompt": "..."}')

TURN_FIELDS = ("outcome_narration", "next_scene", "setting", "outcome_visko_prompt",
               "scene_visko_prompt", "choices")
VIDEO_FIELDS = ("setting", "outcome_visko_prompt", "scene_visko_prompt")
CHOICE_FIELDS = ("label", "speed", "wellness_change", "score_note",
                 "uses_item", "payoff")
VERDICT_FIELDS = ("traveler_type", "traveler_blurb", "verdict_visko_prompt")
SECOND_PERSON = re.compile(r"\b(you|your)\b", re.IGNORECASE)
BRAND = re.compile(r"\bred\s*bull\b", re.IGNORECASE)  # the one brand among the items
REAL_NAME = re.compile(r"\bstanford\b", re.IGNORECASE)  # narration may say it; videos may not
# Nobody in authority exists in this game; any of these words rejects a turn.
BANNED = re.compile(r"\b(customs|officers?|inspectors?|patrol\w*|drones?|raccoons?)\b",
                    re.IGNORECASE)
# A sentence ends at . ! ? or an ellipsis (… or ...), optionally followed by closing
# quotes, before a space, the end, or straight into a capital letter ("here.Kyle").
# Any way a turn might show one of the five items, by name or by look.
ITEM_MENTION = {
    "alarm clock": re.compile(r"\balarm\s*clocks?\b", re.IGNORECASE),
    "red bull": re.compile(r"\bred\s*bulls?\b|\benergy\s*drinks?\b", re.IGNORECASE),
    "flashlight": re.compile(r"\bflash\s*lights?\b", re.IGNORECASE),
    "cs textbook": re.compile(r"\btext\s*books?\b", re.IGNORECASE),
    "mystery bag": re.compile(r"\bmystery\s*bags?\b|\bpaper\s*bags?\b", re.IGNORECASE),
}
# The last decision's outcomes come before the engines die and the cabin goes dark;
# the game decides the rest. "Landing lights" or "a ship arrives" are fine.
ENDS_JOURNEY = re.compile(
    r"\b(lands|landed|land(s|ed|ing)?\s+(on|onto|at|in|safely|softly|gently)|"
    r"touch(es|ed|ing)?\s*-?\s*down|touchdown|sets?\s+down\s+on|"
    r"settl(e|es|ed|ing)\s+(on|onto)\s+the\s+(moon|lunar|surface)|"
    r"(arriv(e|es|ed|ing)|reach(es|ed|ing)?)\s+(at\s+|on\s+)?(the\s+)?(moon|lunar|surface)|"
    r"crash(es|ed|ing)?\s+(into|on|onto)\s+(the\s+)?(moon|lunar|surface|crater)|"
    r"out\s+of\s+fuel)\b", re.IGNORECASE)
# The outcome right before a quest must not move past where it happens. These
# match arriving or leaving, not naming a place: "race toward the launch pad", "aim
# for the Moon" and "ready for orbit" are fine.
TOO_FAR_BEFORE = {
    "heat nap": re.compile(
        r"\b(leaves?|leaving|left|escapes?|escaped|out\s+of)\s+(the\s+)?campus\b|"
        r"\b(is|are|gets?|got|makes?\s+it|made\s+it|runs?|ran)\s+off\s+(the\s+)?campus\b|"
        r"\b(on|onto|at|reach(es|ed|ing)?|arriv(e|es|ed|ing)\s+at)\s+(the\s+)?launch\s*pad\b",
        re.IGNORECASE),
    "blackout": re.compile(
        r"(?<!before\s)\b(lift(s|ed|ing)?\s*-?\s*off|liftoff|blast(s|ed|ing)?\s+off)\b|"
        r"\b(rocket|ship|it)\s+(takes?\s+off|took\s+off|launch(es|ed))\b|"
        r"\blaunch(es|ed)?\s+(the\s+|your\s+)?(rocket|ship)\s+(into|toward|to|up|skyward)\b|"
        r"\b(into|reach(es|ed)?|in)\s+(low\s+)?orbit\b",
        re.IGNORECASE),
    "engine stall": re.compile(
        r"\b(lands?|landed|landing|arriv\w*|reach\w*|touch\w*\s*down)\s+(on\s+|at\s+)?the\s+moon\b|"
        r"\b(into|through|across|enters?|entered|reach\w*)\s+deep\s+space\b",
        re.IGNORECASE),
}
TOO_FAR_HINT = {
    "heat nap": "the student stays on campus: they may head toward the launch pad but "
                "do not reach it",
    "blackout": "the rocket stays sitting on the pad until the lights go out",
    "engine stall": "the rocket stays in low orbit; the Moon may be ahead, but is not "
                    "reached yet",
}
ITEM_DISPLAY = {"alarm clock": "alarm clock", "red bull": "Red Bull",
                "flashlight": "flashlight", "cs textbook": "CS textbook",
                "mystery bag": "mystery bag"}
# How item names look in text the player reads.
ITEM_CASE = [(re.compile(r"\bcs\s+textbook\b", re.IGNORECASE), "CS textbook"),
             (re.compile(r"\bred\s*bull\b", re.IGNORECASE), "Red Bull")]
LEAVES_CABIN = re.compile(
    r"\b(hatch|spacewalk\w*|airlock|floats?\s+out|climbs?\s+out|steps?\s+out|"
    r"outside\s+the\s+(rocket|ship|cabin)|leaves?\s+the\s+(rocket|ship|cabin|cockpit))\b",
    re.IGNORECASE)
SENTENCE_END = re.compile(r"(?:[.!?]+|…)[\"'”’)\]]*(?=\s|$|[A-Z])")
WORD_SPLIT = re.compile(r"[\s—–]+")  # unspaced em/en dashes separate words too

FALLBACKS = json.loads((Path(__file__).parent / "fallbacks.json").read_text())
log = logging.getLogger("gm")
_client = None


def _get_client():
    global _client
    if _client is None:
        # max_retries=0: get_turn does the one retry itself.
        _client = OpenAI(api_key=os.environ["OPENAI_API_KEY"], max_retries=0, timeout=20.0)
    return _client


def word_count(text: str) -> int:
    return len([w for w in WORD_SPLIT.split(text) if w.strip()])


def sentence_count(text: str) -> int:
    """Sentences end at . ! or ? (optionally followed by quotes) before a space or the end.
    Trailing text without end punctuation counts as one more sentence."""
    text = text.strip()
    ends = list(SENTENCE_END.finditer(text))
    trailing = text[ends[-1].end():].strip() if ends else text
    return len(ends) + (1 if trailing else 0)


def _extract_json(text: str):
    start, end = text.find("{"), text.rfind("}")
    if start == -1 or end <= start:
        raise ValueError("no JSON object in response")
    return json.loads(text[start : end + 1])


def _number(value, name) -> float:
    if isinstance(value, bool):
        raise ValueError(f"{name} is not a number")
    try:
        number = float(value)
    except (TypeError, ValueError, OverflowError):
        raise ValueError(f"{name} is not a number: {value!r}")
    if not math.isfinite(number):
        raise ValueError(f"{name} is not finite: {value!r}")
    return number


def _text(value, name) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{name} is empty or not text")
    return value.strip()


def _visual(value, name) -> str:
    """A video field: text, third person, no brand, no real names."""
    text = _text(value, name)
    if SECOND_PERSON.search(text):
        raise ValueError(f"{name} uses you/your: {text!r}")
    if BRAND.search(text):
        raise ValueError(f"{name} names a brand: {text!r}")
    if REAL_NAME.search(text):
        raise ValueError(f"{name} names a real place: {text!r}")
    return text


def _check_banned(texts) -> None:
    for t in texts:
        if isinstance(t, str) and BANNED.search(t):
            raise ValueError(f"mentions someone in authority: {t!r}")


def items_named(text) -> set:
    """Which of the five items a text shows, by name or by look."""
    if not isinstance(text, str):
        return set()
    return {item for item, pattern in ITEM_MENTION.items() if pattern.search(text)}


def _check_items(texts, allowed, what) -> None:
    for t in texts:
        extra = items_named(t) - set(allowed)
        if extra:
            raise ValueError(f"{what} mentions {sorted(extra)}, which the player does not "
                             f"hold here: {t!r}")


def _item_case(text: str) -> str:
    for pattern, display in ITEM_CASE:
        text = pattern.sub(display, text)
    return text


def validate_turn(data, items, reserved=(), just_used=None, choice_outcomes=False,
                  quest=None) -> dict:
    """Raise ValueError if the turn breaks the rules. Numbers are checked for type
    only; game.py clamps them. `reserved` items may be spent on the coming quest
    before these choices are shown, so no choice may use or mention them. The outcome
    may also show `just_used`, the item the last choice used up. With
    `choice_outcomes` (the last decision) each choice carries its own outcome.
    `quest` names the quest right after the outcome, if any."""
    if not isinstance(data, dict):
        raise ValueError("turn is not an object")
    missing = [f for f in TURN_FIELDS if f not in data]
    if missing:
        raise ValueError(f"turn missing {missing}")
    choices = data["choices"]
    if not isinstance(choices, list) or len(choices) != 2:
        raise ValueError("need exactly two choices")

    outcome = _text(data["outcome_narration"], "outcome_narration")
    if word_count(outcome) > MAX_OUTCOME_WORDS:
        raise ValueError(f"outcome_narration over {MAX_OUTCOME_WORDS} words: {outcome!r}")
    scene = _text(data["next_scene"], "next_scene")
    if word_count(scene) > MAX_SCENE_WORDS:
        raise ValueError(f"next_scene over {MAX_SCENE_WORDS} words: {scene!r}")
    if sentence_count(scene) > MAX_SCENE_SENTENCES:
        raise ValueError(f"next_scene over {MAX_SCENE_SENTENCES} sentences: {scene!r}")
    if not scene.rstrip("\"'”’)").endswith("?"):
        raise ValueError(f"next_scene must end with a question: {scene!r}")

    out = {"outcome_narration": _item_case(outcome), "next_scene": _item_case(scene),
           "choices": []}
    _check_banned([outcome, scene] + [data.get(f) for f in
                                      ("setting", "outcome_visko_prompt", "scene_visko_prompt")]
                  + [c.get(f) for c in choices if isinstance(c, dict)
                     for f in ("label", "score_note")])
    # setting is part of every final prompt, so it gets the same checks.
    for f in VIDEO_FIELDS:
        out[f] = _visual(data[f], f)

    # Items: the game lists the held ones in every video, so setting names none; the
    # outcome may show what was held before the choice; everything after it shows only
    # what is still held and safe from the coming quest.
    if items_named(out["setting"]):
        raise ValueError(f"setting mentions items (the game adds them): {out['setting']!r}")
    _check_items([outcome, out["outcome_visko_prompt"]],
                 list(items) + ([just_used] if just_used else []), "outcome")
    too_far = TOO_FAR_BEFORE.get(quest)
    if too_far:
        for t in (outcome, out["outcome_visko_prompt"]):
            m = too_far.search(t)
            if m:
                raise ValueError(f"outcome goes past the coming {quest} "
                                 f"('{m.group(0)}'); {TOO_FAR_HINT[quest]}: {t!r}")
    later_ok = [i for i in items if i not in reserved]
    _check_items([scene, out["scene_visko_prompt"]]
                 + [c.get(f) for c in choices if isinstance(c, dict)
                    for f in ("label", "score_note")], later_ok, "scene or choice")

    def declared(c):
        u = c.get("uses_item") if isinstance(c, dict) else None
        return u.strip().lower() if isinstance(u, str) else None

    for n, c in enumerate(choices):
        if not isinstance(c, dict):
            raise ValueError("choice is not an object")
        missing = [f for f in CHOICE_FIELDS if f not in c]
        if missing:
            raise ValueError(f"choice missing {missing}")

        label = _text(c["label"], "label")
        if word_count(label) > MAX_LABEL_WORDS:
            raise ValueError(f"label over {MAX_LABEL_WORDS} words: {label!r}")

        note = _text(c["score_note"], "score_note")
        if word_count(note) > MAX_NOTE_WORDS:
            raise ValueError(f"score_note over {MAX_NOTE_WORDS} words: {note!r}")

        item = c["uses_item"]
        if isinstance(item, str):
            item = item.strip().lower()
            if item in ("", "null", "none"):
                item = None
        # A label that uses an item spends it, even if uses_item was left out, unless
        # the other choice is the one using it ("Keep your Red Bull").
        named = items_named(label) & set(items)
        other = declared(choices[1 - n]) if len(choices) == 2 else None
        if item is None and len(named) == 1 and other not in named:
            item = next(iter(named))
        elif named - ({item} if item else set()):
            raise ValueError(f"label {label!r} names {sorted(named)} but uses_item is "
                             f"{item!r}; name an item only in the choice that uses it up")
        if item is not None and item not in items:
            raise ValueError(f"uses_item {item!r} not held")
        if item is not None and item in reserved:
            raise ValueError(f"uses_item {item!r} may be spent on the quest first")

        payoff = c["payoff"]
        if isinstance(payoff, str) and payoff.strip().lower() in ("true", "false"):
            payoff = payoff.strip().lower() == "true"
        if not isinstance(payoff, bool):
            raise ValueError(f"payoff is not true/false: {payoff!r}")

        if item is not None and item not in named:
            # Spending must be visible to the player: say which item the choice uses.
            label = f"{label} (uses {ITEM_DISPLAY[item]})"
        checked = {
            "label": _item_case(label),
            "speed": _number(c["speed"], "speed"),
            "wellness_change": _number(c["wellness_change"], "wellness_change"),
            "score_note": _item_case(note),
            "uses_item": item,
            "payoff": payoff,
        }
        if choice_outcomes:  # the clip may show only the item this choice spends
            checked.update(_choice_outcome(c, [item] if item else []))
        out["choices"].append(checked)
    return out


def _choice_outcome(c, allowed) -> dict:
    """The last decision's per-choice outcome: narration and the video of taking it."""
    narration = _text(c.get("outcome_narration"), "choice outcome_narration")
    prompt = _visual(c.get("outcome_visko_prompt"), "choice outcome_visko_prompt")
    if word_count(narration) > MAX_OUTCOME_WORDS:
        raise ValueError(f"choice outcome_narration over {MAX_OUTCOME_WORDS} words: "
                         f"{narration!r}")
    _check_banned([narration, prompt])
    _check_items([narration, prompt], allowed, "choice outcome")
    for t in (narration, prompt):
        if ENDS_JOURNEY.search(t):
            raise ValueError(f"choice outcome ends the journey: {t!r}")
        if LEAVES_CABIN.search(t):
            raise ValueError(f"choice outcome leaves the cabin: {t!r}")
    return {"outcome_narration": _item_case(narration), "outcome_visko_prompt": prompt}


def validate_verdict(data, items_left=None) -> dict:
    """items_left: items still held; the ending video may show no others (None = skip)."""
    if not isinstance(data, dict):
        raise ValueError("verdict is not an object")
    missing = [f for f in VERDICT_FIELDS if f not in data]
    if missing:
        raise ValueError(f"verdict missing {missing}")
    traveler_type = _text(data["traveler_type"], "traveler_type")
    if not 2 <= word_count(traveler_type) <= 4 or not traveler_type.startswith("The "):
        raise ValueError(f"traveler_type must be 2-4 words starting with 'The': "
                         f"{traveler_type!r}")
    blurb = _text(data["traveler_blurb"], "traveler_blurb")
    _check_banned([traveler_type, blurb, data.get("verdict_visko_prompt")])
    if word_count(blurb) > MAX_BLURB_WORDS:
        raise ValueError(f"traveler_blurb over {MAX_BLURB_WORDS} words: {blurb!r}")
    if not SECOND_PERSON.search(blurb):
        raise ValueError(f"traveler_blurb is not addressed to you: {blurb!r}")
    prompt = _visual(data["verdict_visko_prompt"], "verdict_visko_prompt")
    if items_left is not None:
        _check_items([prompt], items_left, "verdict_visko_prompt")
    return {
        "traveler_type": traveler_type,
        "traveler_blurb": blurb,
        "verdict_visko_prompt": prompt,
    }


def turn_instructions(choice_outcomes=False) -> str:
    if choice_outcomes:
        return (TURN_INSTRUCTIONS + FINAL_INSTRUCTIONS
                + "Respond with only a JSON object shaped like:\n" + OUTPUT_SHAPE_FINAL)
    return TURN_INSTRUCTIONS + "Respond with only a JSON object shaped like:\n" + OUTPUT_SHAPE


def _ask_llm(payload: dict, system: str = SYSTEM_PROMPT, instructions: str = None,
             max_tokens: int = MAX_TOKENS) -> dict:
    if instructions is None:
        instructions = turn_instructions()
    user_msg = "This turn:\n" + json.dumps(payload, indent=2) + "\n\n" + instructions
    resp = _get_client().chat.completions.create(
        model=MODEL,
        max_completion_tokens=max_tokens,
        reasoning_effort="none",  # skip reasoning tokens: faster, and fits the limit
        response_format={"type": "json_object"},
        messages=[
            {"role": "system", "content": system},
            {"role": "user", "content": user_msg},
        ],
    )
    choice = resp.choices[0]
    if choice.finish_reason != "stop" or choice.message.refusal:
        raise ValueError(f"finish_reason={choice.finish_reason} refusal={choice.message.refusal}")
    return _extract_json(choice.message.content or "")


LLM_ERRORS = (OpenAIError, ValueError, TypeError, KeyError, ArithmeticError)


def _retry_note(error) -> str:
    """Tell the next attempt what was wrong with the last one. Network or API errors
    say nothing about the reply, so they add no note."""
    if isinstance(error, OpenAIError):
        return ""
    return (f"\n\nYour previous reply was rejected: {str(error)[:300]}. "
            "Write a new reply that follows every rule.")


def get_turn(beat_name, hint, encounter, last_choice, items, pace, history,
             outcome_checkpoint=None, scene_checkpoint=None,
             fixed_outcome_visko=None, quest_follows=None,
             items_used=None, just_used=None, choice_outcomes=False,
             previous_scene=None, kyle_came_along=False) -> dict:
    # Items the player may use up on the coming quest are not offered as held:
    # the scene and choices are shown only after it. Validation still knows them.
    reserved = list((quest_follows or {}).get("may_use_up", []))
    follows = ({k: v for k, v in quest_follows.items() if k != "may_use_up"}
               if quest_follows else None)
    payload = {
        "beat": beat_name,
        "beat_setting": hint,
        "encounter": encounter,
        "outcome_checkpoint": outcome_checkpoint,
        "scene_checkpoint": scene_checkpoint,
        "previous_scene": previous_scene,
        "last_choice": last_choice,
        "items_held": [i for i in items if i not in reserved],
        "items_saved_for_quest": reserved,
        "items_used": items_used or [],
        "just_used": just_used,
        "kyle_came_along": kyle_came_along,
        "pace": pace,
        "story_so_far": history,
        "quest_follows": follows,
        "choice_outcomes": choice_outcomes,
    }
    if fixed_outcome_visko:
        payload["fixed_outcome_visko_prompt"] = fixed_outcome_visko
    instructions = turn_instructions(choice_outcomes)
    max_tokens = MAX_TOKENS_FINAL if choice_outcomes else MAX_TOKENS
    feedback = ""
    for attempt in range(1, TURN_ATTEMPTS + 1):
        try:
            return validate_turn(
                _ask_llm(payload, SYSTEM_PROMPT, instructions + feedback, max_tokens),
                items, reserved, just_used, choice_outcomes, (follows or {}).get("name"))
        except LLM_ERRORS as e:
            log.warning("GM attempt %d for %r failed: %s", attempt, beat_name, e)
            feedback = _retry_note(e)
    log.warning("Using fallback turn for %r", beat_name)
    return validate_turn(copy.deepcopy(FALLBACKS[beat_name]), items,
                         choice_outcomes=choice_outcomes,
                         quest=(follows or {}).get("name"))


def get_verdict(ending_title, ending, final_distance, final_wellness, decisions,
                place, items=None) -> dict:
    """Tries once, retries once; returns the validated verdict, or None so game.py
    uses its fixed table."""
    payload = {
        "ending_title": ending_title,
        "ending": ending,
        "final_distance_miles": final_distance,
        "final_wellness": final_wellness,
        "place": place,
        "items_left": items or [],
        "decisions": decisions,
    }
    instructions = "Respond with only a JSON object shaped like:\n" + VERDICT_SHAPE
    feedback = ""
    for attempt in (1, 2):
        try:
            return validate_verdict(_ask_llm(payload, VERDICT_PROMPT, instructions + feedback),
                                    items or [])
        except LLM_ERRORS as e:
            log.warning("Verdict attempt %d failed: %s", attempt, e)
            feedback = _retry_note(e)
    log.warning("Using the fixed verdict")
    return None
