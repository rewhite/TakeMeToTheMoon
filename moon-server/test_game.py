"""Rules tests. No LLM calls: get_turn/get_verdict are stubbed and gm._ask_llm is patched."""

import copy
import json
import pathlib
import re

import pytest

import game
import gm

FALLBACK = gm.FALLBACKS
CP = {c["name"]: c for c in game.CHECKPOINTS}
V = {n: c["visual"] for n, c in CP.items()}


def stub_turn(beat_name, choice_outcomes=False, **_):
    return copy.deepcopy(gm.validate_turn(FALLBACK[beat_name], [],
                                          choice_outcomes=choice_outcomes))


def raw_choice(speed, wellness_change, payoff=False, label="Do a thing"):
    return {"label": label, "speed": speed, "wellness_change": wellness_change,
            "score_note": "for a thing", "uses_item": None, "payoff": payoff}


def cp_index(name):
    return next(i for i, c in enumerate(game.CHECKPOINTS) if c["name"] == name)


def state_at(checkpoint_name, items=("alarm clock", "flashlight", "mystery bag"), **kw):
    """A state standing at the named checkpoint with its decision pending."""
    s = game.GameState(items=list(items), **kw)
    s.checkpoint = cp_index(checkpoint_name)
    return s


def set_pending(s, choices):
    s.pending_choices = game.score_choices(s, game.CHECKPOINTS[s.checkpoint]["beat"], choices)


def prompt(place, setting, text, items=()):
    """The exact Visko prompt: STYLE, place, setting, items still held, action."""
    parts = [game.STYLE.strip(), place["visual"] + ".", setting, game.items_visual(items), text]
    return " ".join(p for p in parts if p)


def kinds(r):
    return [v["kind"] for v in r["videos"]]


def places(r):
    return [v["prompt"][len(game.STYLE):].split(". ")[0] for v in r["videos"]]


def at_quest(name, items):
    """A state stopped at the named quest, with the next turn held. The quest
    videos use the setting of the turn just generated (s.last_setting)."""
    where = {"heat nap": "Campus", "blackout": "Launch Site", "engine stall": "Low Orbit"}[name]
    s = state_at(where, items=items)
    set_pending(s, [raw_choice(5, 0), raw_choice(3, 2)])
    r = game.choose(s, 0, stub_turn)
    assert r["mode"] == "quest" and r["quest_name"] == name
    return s, r


def resolve(s, how):
    """Finish a quest: an item name skips it; "pass" or None plays the quest
    (players retry until they pass)."""
    if how in ("pass", None):
        return game.quest_result(s, True)
    return game.use_item(s, how)


def play(items, picks, uses, verdict=None):
    """Play a whole game. `uses` says how each quest ends, in order (see resolve).
    Returns (state, responses incl. /start, turn calls)."""
    calls = []

    def turn(**kw):
        calls.append(kw)
        return stub_turn(**kw)

    s, r = game.new_game(items)
    rs, uses = [r], list(uses)
    for i in picks:
        r = game.choose(s, i, turn, verdict)
        rs.append(r)
        if r["mode"] == "quest":
            r = resolve(s, uses.pop(0))
            rs.append(r)
    return s, rs, calls


# ---------- six checkpoints, one decision each ----------

def test_six_checkpoints_in_order():
    assert game.CHECKPOINT_TOTAL == 6
    assert [(c["number"], c["name"]) for c in game.CHECKPOINTS] == [
        (1, "Dorm Room"), (2, "Campus"), (3, "Launch Site"), (4, "Low Orbit"),
        (5, "Deep Space"), (6, "Moon Approach")]
    assert V["Deep Space"] == ("A homemade rocket drifting through dark space, Earth a small "
                               "orange dot behind it")
    assert V["Moon Approach"] == ("A homemade rocket closing in on the huge pale Moon, "
                                  "craters visible below")
    assert [c["decision"] for c in game.CHECKPOINTS] == ["fixed"] + ["llm"] * 5
    assert [c.get("beat") for c in game.CHECKPOINTS] == [
        None, "escape campus", "build the rocket", "in space", "deep space", "final approach"]
    assert [c.get("encounter") for c in game.CHECKPOINTS[1:]] == [
        True, False, True, False, False]
    assert [game.BEAT_SIZE[c["beat"]] for c in game.CHECKPOINTS[1:]] == [
        6000, 110000, 50000, 40000, 30000]
    assert "Moon Border" not in CP
    assert game.MOON_SURFACE["visual"] == ("The grey lunar surface, with Earth glowing orange "
                                           "above the horizon")


def test_full_game_six_decisions_three_quests():
    s, rs, calls = play(["alarm clock", "flashlight", "mystery bag"], [0, 0, 0, 0, 0, 0],
                        ["alarm clock", "flashlight", "pass"])
    steps = [(r["mode"], r["checkpoint_number"], r["quest_name"], kinds(r)) for r in rs]
    assert steps == [
        ("decision", 1, None, ["scene"]),                                  # /start
        ("decision", 2, None, ["outcome", "scene"]),                       # Dorm Room
        ("quest", 2, "heat nap", ["outcome", "quest"]),        # Campus
        ("decision", 3, None, ["quest_result", "scene"]),            # /use_item
        ("quest", 3, "blackout", ["outcome", "quest"]),        # Launch Site
        ("decision", 4, None, ["quest_result", "scene"]),
        ("quest", 4, "engine stall", ["outcome", "quest"]),    # Low Orbit
        ("decision", 5, None, ["quest_result", "scene"]),
        ("decision", 6, None, ["outcome", "scene"]),                       # Deep Space
        ("verdict", 6, None, ["outcome", "ending"]),                       # Moon Approach
    ]
    # A quest happens at the checkpoint of the decision before it.
    assert places(rs[2]) == [V["Campus"], V["Campus"]]
    assert places(rs[3]) == [V["Campus"], V["Launch Site"]]
    assert places(rs[4]) == [V["Launch Site"], V["Launch Site"]]
    assert places(rs[5]) == [V["Launch Site"], V["Low Orbit"]]
    assert places(rs[6]) == [V["Low Orbit"], V["Low Orbit"]]
    assert places(rs[7]) == [V["Low Orbit"], V["Deep Space"]]
    assert places(rs[8]) == [V["Deep Space"], V["Moon Approach"]]
    assert places(rs[9])[0] == V["Moon Approach"]
    # Quest responses hold back the next choices; decision responses carry them.
    for r in rs[:-1]:
        assert (r["choices"] == []) == (r["mode"] == "quest")
    assert rs[-1]["game_over"] and rs[-1]["choices"] == []
    # One LLM call per decision; it is told which quest follows.
    assert [c["beat_name"] for c in calls] == [
        "escape campus", "build the rocket", "in space", "deep space", "final approach"]
    follows = [c["quest_follows"] and c["quest_follows"]["name"] for c in calls]
    assert follows == [None, "heat nap", "blackout", "engine stall", None]
    assert calls[1]["quest_follows"]["outcome_ends_on"].startswith(
        "the student slowing down in the heat")
    assert "still sitting on the pad" in calls[2]["quest_follows"]["outcome_ends_on"]
    # The skip item, if held, might be spent on the quest; the held scene avoids it.
    assert calls[1]["quest_follows"]["may_use_up"] == ["alarm clock"]
    assert calls[2]["quest_follows"]["may_use_up"] == ["flashlight"]
    assert calls[3]["quest_follows"]["may_use_up"] == []  # no red bull held
    # Only the last decision's turn writes an outcome for each choice.
    assert [c["choice_outcomes"] for c in calls] == [False, False, False, False, True]
    assert s.decisions and len(s.decisions) == 6
    assert [d["checkpoint"] for d in s.decisions] == [c["name"] for c in game.CHECKPOINTS]


def test_quest_response_shape():
    s, r = at_quest("heat nap", ["flashlight", "alarm clock", "cs textbook"])
    d = game.QUESTS["heat nap"]
    assert r["videos"][1] == {"kind": "quest", "narration": d["narration"],
                              "prompt": prompt(CP["Campus"], s.last_setting, d["visko"],
                                               r["items"])}
    assert s.last_setting == FALLBACK["build the rocket"]["setting"]
    assert r["items"] == ["flashlight", "alarm clock", "cs textbook"]
    assert r["next_scene"] == "" and r["choices"] == []
    assert s.held["choices"] and s.held["scene_prompt"] and s.held["setting"]
    assert r["quest"] == {
        "name": "heat nap", "narration": d["narration"], "skip_item": "alarm clock",
        "has_skip_item": True, "skip_prompt": "Use your alarm clock to skip it?",
        "task": {"id": "throw_ball", "instructions": "Throw the ball at the target to wake up!"}}


def test_held_scene_and_choices_come_back_without_another_llm_call():
    s = state_at("Campus", items=["alarm clock", "flashlight", "mystery bag"])
    set_pending(s, [raw_choice(5, 0), raw_choice(3, 2)])
    turn = stub_turn("build the rocket")
    calls = []
    game.choose(s, 0, lambda **kw: calls.append(kw) or copy.deepcopy(turn))
    r = game.use_item(s, "alarm clock")
    assert len(calls) == 1
    assert r["choices"] == [c["label"] for c in turn["choices"]]
    assert r["next_scene"] == turn["next_scene"]
    # The held scene is assembled after /use_item: the spent alarm clock is gone from it.
    assert r["videos"][1]["prompt"] == prompt(CP["Launch Site"], turn["setting"],
                                              turn["scene_visko_prompt"],
                                              ["flashlight", "mystery bag"])
    assert r["videos"][0]["prompt"].startswith(game.STYLE + V["Campus"] + ". "
                                               + turn["setting"])


# ---------- quests: right item, wrong item, nothing ----------

def test_three_quests_each_with_one_skip_item_and_a_quest():
    assert set(game.QUESTS) == {"heat nap", "blackout", "engine stall"}
    assert {n: d["skip_item"] for n, d in game.QUESTS.items()} == {
        "heat nap": "alarm clock", "blackout": "flashlight", "engine stall": "red bull"}
    assert {n: d["task"]["id"] for n, d in game.QUESTS.items()} == {
        "heat nap": "throw_ball", "blackout": "crank_generator", "engine stall": "pull_cord"}
    # Players retry the quest until they pass: no failure, no penalty.
    for d in game.QUESTS.values():
        assert "failed" not in d and "fail" not in d
    assert [c["after"] for c in game.CHECKPOINTS] == [
        None, "heat nap", "blackout", "engine stall", None, None]


@pytest.mark.parametrize("name", list(game.QUESTS))
def test_quest_text_the_player_reads_is_short(name):
    d = game.QUESTS[name]
    assert gm.word_count(d["narration"]) <= 8
    assert gm.word_count(d["task"]["instructions"]) <= 10
    for how in ("skip", "passed"):
        assert gm.word_count(d[how][0]) <= 10, d[how][0]


def skip_case(name):
    item = game.QUESTS[name]["skip_item"]
    others = [i for i in game.ALLOWED_ITEMS if i != item]
    return item, others


@pytest.mark.parametrize("name", list(game.QUESTS))
def test_skip_item_ends_it_and_is_used_up(name):
    item, others = skip_case(name)
    s, r0 = at_quest(name, [item] + others[:2])
    assert r0["quest"]["has_skip_item"] and r0["quest"]["skip_item"] == item
    distance, wellness = s.distance, s.wellness
    r = game.use_item(s, item)
    narration, video_prompt = game.QUESTS[name]["skip"]
    assert item not in r["items"] and r["items"] == others[:2] and item in s.items_used
    assert (r["distance"], r["wellness"]) == (distance, wellness)
    assert (r["distance_change"], r["wellness_change"]) == (0, 0)
    assert r["light"] == "green" and r["mode"] == "decision" and len(r["choices"]) == 2
    assert r["quest"] is None
    # The skip clip still shows the item being used; the next scene no longer has it.
    assert r["videos"][0] == {"kind": "quest_result", "narration": narration,
                              "prompt": prompt(game.CHECKPOINTS[s.checkpoint - 1],
                                               s.last_setting, video_prompt,
                                               [item] + others[:2])}
    assert game.ITEM_LOOK[item] not in r["videos"][1]["prompt"]


@pytest.mark.parametrize("name", list(game.QUESTS))
def test_passing_the_quest_costs_nothing(name):
    item, others = skip_case(name)
    s, r0 = at_quest(name, others[:3])
    assert r0["quest"]["has_skip_item"] is False
    assert r0["quest"]["skip_prompt"] is None
    s.wellness = 90
    distance = s.distance
    r = game.quest_result(s, True)
    assert r["items"] == others[:3]  # nothing is used up
    assert (r["wellness"], r["distance"]) == (90, distance)
    assert (r["wellness_change"], r["distance_change"]) == (0, 0)
    assert r["light"] == "green" and r["mode"] == "decision"
    narration, video_prompt = game.QUESTS[name]["passed"]
    assert r["videos"][0]["narration"] == r["outcome_narration"] == narration
    assert r["videos"][0]["prompt"].endswith(video_prompt)


def test_quest_only_ends_when_passed():
    s, _ = at_quest("engine stall", ["alarm clock", "flashlight", "cs textbook"])
    before = copy.deepcopy(s)
    for bad in (False, None, "yes", 1):
        with pytest.raises(game.GameError):
            game.quest_result(s, bad)
    assert s == before
    assert game.quest_result(s)["mode"] == "decision"  # passed defaults to true


def test_holding_the_skip_item_may_still_play_the_quest():
    s, _ = at_quest("heat nap", ["alarm clock", "flashlight", "mystery bag"])
    r = game.quest_result(s, True)
    assert r["items"] == ["alarm clock", "flashlight", "mystery bag"]


def test_use_item_and_quest_errors():
    s, _ = game.new_game(["alarm clock", "flashlight", "mystery bag"])
    with pytest.raises(game.GameError):
        game.use_item(s, "alarm clock")  # nothing waiting
    with pytest.raises(game.GameError):
        game.quest_result(s, True)
    s, _ = at_quest("heat nap", ["flashlight", "cs textbook", "mystery bag"])
    for bad in ("alarm clock", "flashlight", None):  # not held / not the skip item / none
        with pytest.raises(game.GameError):
            game.use_item(s, bad)
    with pytest.raises(game.GameError):
        game.quest_result(s, "yes")
    with pytest.raises(game.GameError):
        game.choose(s, 0, stub_turn)  # must finish the quest first
    s, _ = at_quest("heat nap", ["alarm clock", "cs textbook", "mystery bag"])
    r = game.use_item(s, " ALARM CLOCK ")  # normalised
    assert r["light"] == "green" and "alarm clock" not in r["items"]


def test_use_item_is_all_or_nothing(monkeypatch):
    s, _ = at_quest("heat nap", ["alarm clock", "flashlight", "cs textbook"])
    before = copy.deepcopy(s)
    monkeypatch.setattr(game, "video", lambda *a, **k: (_ for _ in ()).throw(RuntimeError()))
    with pytest.raises(RuntimeError):
        game.use_item(s, "alarm clock")
    assert s == before


# ---------- no customs, no officials, no real names ----------

def test_no_customs_anywhere():
    banned = re.compile(r"customs|raccoon|moon border", re.I)
    here = pathlib.Path(__file__).parent
    for path in ("game.py", "fallbacks.json", "play.py", "main.py"):
        assert not banned.search((here / path).read_text()), path
    assert not banned.search(json.dumps(game.CHECKPOINTS) + json.dumps(game.QUESTS))
    # ...and nothing a played game returns mentions it either.
    _, rs, _ = play(["red bull", "flashlight", "mystery bag"], [0, 1, 0, 1, 0, 1],
                    ["pass", None, "red bull"])
    assert not banned.search(json.dumps(rs))
    assert not gm.BANNED.search(json.dumps(rs))


def test_fixed_text_has_no_officials_or_real_names():
    texts = [c["visual"] for c in game.CHECKPOINTS] + [game.MOON_SURFACE["visual"]]
    texts += all_fixed_prompts()
    for d in game.QUESTS.values():
        texts += [d["narration"], d["task"]["instructions"]]
        texts += [d[how][0] for how in ("skip", "passed")]
    for t in texts:
        assert not gm.BANNED.search(t), t
        assert not gm.REAL_NAME.search(t), t


@pytest.mark.parametrize("field,value", [
    ("next_scene", "A drone swoops down. You must dodge it."),
    ("outcome_narration", "You slip past the customs booth."),
    ("next_scene", "Officer Dana blocks the hatch. You must get past her."),
    ("setting", "The student flies past an inspector."),
    ("scene_visko_prompt", "A patrol ship blocks the rocket."),
    ("outcome_visko_prompt", "Two raccoons wave at the student."),
])
def test_validation_rejects_officials(field, value):
    t = good_turn()
    t[field] = value
    with pytest.raises(ValueError):
        gm.validate_turn(t, [])


def test_validation_rejects_officials_in_labels_and_notes():
    t = good_turn()
    t["choices"][0]["label"] = "Bribe the officer"
    with pytest.raises(ValueError):
        gm.validate_turn(t, [])
    t = good_turn()
    t["choices"][1]["score_note"] = "for robbing a drone"
    with pytest.raises(ValueError):
        gm.validate_turn(t, [])


@pytest.mark.parametrize("field", ["setting", "outcome_visko_prompt", "scene_visko_prompt"])
def test_validation_rejects_stanford_in_video_prompts(field):
    t = good_turn()
    t[field] = "The student runs across the Stanford quad."
    with pytest.raises(ValueError):
        gm.validate_turn(t, [])


def test_stanford_is_fine_in_narration():
    t = good_turn()
    t["outcome_narration"] = "You sprint off the Stanford quad."
    gm.validate_turn(t, [])


def test_system_prompt_has_the_new_rules():
    p = " ".join(gm.SYSTEM_PROMPT.split())
    assert ("There are no customs, borders, police, officers, inspectors, drones or raccoons "
            "anywhere in this game. Encounters at Low Orbit are with other escapees, "
            "stowaways, rivals or strange things floating in space, never anyone in "
            "authority.") in p
    assert ("If a quest follows, end the outcome video prompt on an image that leads "
            "into it (for heat nap: the student slowing down in the heat; for blackout: the "
            "lights starting to flicker; for engine stall: the engine sputtering). In that "
            "case the scene video prompt opens with the student arriving at the next "
            "location instead of continuing the outcome's final image.") in p
    assert "official" not in p.replace("officers", "")
    assert "grabbed three items" in p and "possibly none" not in p
    assert 'marked "used"' in p


# ---------- scoring, pace ----------

@pytest.mark.parametrize("beat,speed,expected", [
    ("escape campus", 1, 4560), ("escape campus", 10, 7800),
    ("build the rocket", 1, 83600), ("build the rocket", 10, 143000),
    ("in space", 1, 38000), ("in space", 10, 65000),
    ("deep space", 1, 30400), ("deep space", 10, 52000),
    ("final approach", 1, 22800), ("final approach", 10, 39000),
])
def test_miles_formula(beat, speed, expected):
    assert game.miles_gained(beat, speed) == expected


def test_speed_and_wellness_are_clamped():
    s = game.GameState(items=[])
    a, b = game.score_choices(s, "in space", [raw_choice(99, -50), raw_choice(-3, 40)])
    assert (a["speed"], a["wellness_change"], a["miles"]) == (10, -12, 65000)
    assert (b["speed"], b["wellness_change"], b["miles"]) == (1, 5, 38000)


@pytest.mark.parametrize("wc,light", [(5, "green"), (0, "green"), (-1, "yellow"),
                                      (-4, "yellow"), (-5, "red"), (-12, "red")])
def test_light_from_wellness_change(wc, light):
    assert game.light_for(wc) == light


def test_no_free_wins():
    s = game.GameState(items=[])
    fast, slow = game.score_choices(s, "in space", [raw_choice(8, 2), raw_choice(4, 1)])
    assert (fast["wellness_change"], slow["wellness_change"]) == (-2, 1)
    a, b = game.score_choices(s, "in space", [raw_choice(5, 3), raw_choice(5, -1)])
    assert (a["wellness_change"], b["wellness_change"]) == (3, -1)
    fast, _ = game.score_choices(s, "in space", [raw_choice(9, 3, True), raw_choice(4, 0)])
    assert fast["wellness_change"] == 3 and s.payoff_used
    fast, _ = game.score_choices(s, "deep space", [raw_choice(9, 3, True), raw_choice(4, 0)])
    assert fast["wellness_change"] == -3


def test_pace_counts_remaining_llm_checkpoints():
    assert sum(game.BEAT_SIZE.values()) == 236000
    assert game.compute_pace(236000, cp_index("Campus")) == "on track"
    assert game.compute_pace(236000 + 15001, cp_index("Campus")) == "behind"
    assert game.compute_pace(236000 - 15001, cp_index("Campus")) == "ahead"
    assert game.compute_pace(30000 + 15000, cp_index("Moon Approach")) == "on track"
    assert game.compute_pace(30000 + 15001, cp_index("Moon Approach")) == "behind"


# ---------- endings, Kyle, verdict ----------

@pytest.mark.parametrize("start_distance,wellness,title,final_distance,where", [
    (20000, 90, "Moon Citizen", 0, "moon"),
    (20000, 80, "Moon Reject", 0, "moon"),
    (60000, 90, "Rescued", 0, "moon"),          # 60000-30000 = 30000 <= 40000
    (100000, 90, "Too Far to Tow", 70000, "space"),
    (60000, 80, "Stranded", 30000, "space"),
])
@pytest.mark.parametrize("get_verdict", [None, lambda **_: None])
def test_each_ending_with_verdict_fallback(start_distance, wellness, title, final_distance,
                                           where, get_verdict):
    s = state_at("Moon Approach", distance=start_distance, wellness=wellness,
                 last_setting="The student flies a rocket.")
    set_pending(s, [raw_choice(5, 0), raw_choice(1, 1)])
    r = game.choose(s, 0, lambda **_: pytest.fail("no LLM turn"), get_verdict)
    assert r["mode"] == "verdict" and r["game_over"] and r["checkpoint_number"] == 6
    v = r["verdict"]
    assert (r["ending_title"], v["ending_title"]) == (title, title)
    assert r["distance"] == final_distance == v["final_distance"]
    assert r["wellness"] == wellness == v["final_wellness"]
    assert (v["traveler_type"], v["traveler_blurb"]) == game.TRAVELER_FALLBACK[title]
    place = game.MOON_SURFACE if where == "moon" else CP["Deep Space"]
    # The ending clip carries no turn setting, only the items still held, and shows
    # the roommate as the Kyle line describes him.
    kyle = game.KYLE_VISKO[(where == "moon", False)]
    assert r["videos"][-1] == {"kind": "ending", "narration": r["ending"],
                               "prompt": prompt(place, "", f"{game.ENDING_VISKO[title]} {kyle}",
                                                r["items"])}
    assert "flies a rocket" not in r["videos"][-1]["prompt"]
    assert r["videos"][0]["prompt"].startswith(game.STYLE + V["Moon Approach"])


def test_verdict_from_llm(monkeypatch):
    good = {"traveler_type": "The Snack Diplomat",
            "traveler_blurb": "You bribed a pirate and still felt good about it.",
            "verdict_visko_prompt": "The student waves a burrito on the grey dust."}
    seen = {}
    monkeypatch.setattr(gm, "_ask_llm", lambda p, *a, **k: seen.update(p) or dict(good))
    s = state_at("Moon Approach", distance=20000, wellness=90, last_setting="S.")
    set_pending(s, [raw_choice(5, 0), raw_choice(1, 1)])
    r = game.choose(s, 0, lambda **_: None, gm.get_verdict)
    assert r["verdict"]["traveler_type"] == "The Snack Diplomat"
    kyle = game.KYLE_VISKO[(True, False)]
    assert r["videos"][-1]["prompt"] == prompt(game.MOON_SURFACE, "",
                                               f"{good['verdict_visko_prompt']} {kyle}",
                                               r["items"])
    assert seen["place"]["name"] == "The Moon"
    assert seen["items_left"] == ["alarm clock", "flashlight", "mystery bag"]


@pytest.mark.parametrize("mutate", [
    lambda v: v.update(traveler_type="Snack Diplomat"),
    lambda v: v.update(traveler_blurb="You " + "really " * 20 + "did that."),
    lambda v: v.update(verdict_visko_prompt="The student drinks a Red Bull."),
    lambda v: v.update(verdict_visko_prompt="The student lands near Stanford."),
    lambda v: v.update(traveler_blurb="You bribed every officer on the way."),
])
def test_bad_verdict_falls_back(monkeypatch, mutate):
    bad = {"traveler_type": "The Snack Diplomat", "traveler_blurb": "You did that.",
           "verdict_visko_prompt": "The student waves."}
    mutate(bad)
    monkeypatch.setattr(gm, "_ask_llm", lambda *a, **k: bad)
    assert gm.get_verdict("Moon Citizen", "e", 0, 90, [], {}) is None


@pytest.mark.parametrize("distance,wellness,woke,line", [
    (0, 90, True, "Kyle climbs out behind you and asks if there's Wi-Fi."),
    (0, 90, False, "Kyle is already here, holding a smoothie."),
    (90000, 90, True, "Kyle floats beside you, still asleep."),
    (90000, 50, False, "Through the window you can see Kyle on the Moon, waving."),
])
def test_kyle_lines(distance, wellness, woke, line):
    s = game.GameState(items=[], distance=distance, wellness=wellness, woke_kyle=woke)
    game.decide_ending(s)
    assert s.ending.endswith(line)


def d(cp, label, miles, wc):
    return {"checkpoint": cp, "label": label, "miles": miles, "wellness_change": wc,
            "score_note": "n"}


def test_key_moments():
    log = [d("Dorm Room", "Leave him", 0, -2),
           d("Campus", "Steal the bike", 7800, -9),
           d("Launch Site", "Help the crew", 100000, 5),
           d("Low Orbit", "Rob the pirate", 200000, -12)]
    assert game.key_moments(log) == [
        "Low Orbit: you chose to rob the pirate (-12% wellness)",
        "Launch Site: you chose to help the crew (+5% wellness)",
        "Dorm Room: you chose to leave him (-2% wellness)",
    ]
    assert game.format_moment(d("Campus", "Ask Skyler about Kyle's bike", 1, -3)) == \
        "Campus: you chose to ask Skyler about Kyle's bike (-3% wellness)"
    assert game.format_moment(d("Launch Site", "Fuel the rocket with Red Bull", 1, -1)) == \
        "Launch Site: you chose to fuel the rocket with Red Bull (-1% wellness)"
    assert game.format_moment(d("Low Orbit", "CS textbook to the rescue", 1, 2)) == \
        "Low Orbit: you chose to CS textbook to the rescue (+2% wellness)"


# ---------- validation, fallbacks, fixed text ----------

def good_turn():
    return {"outcome_narration": "You jump the fence and land on your feet.",
            "next_scene": "A stowaway blocks the hatch. How do you get past him?",
            "setting": "The student carries a backpack.",
            "outcome_visko_prompt": "The student jumps, ending on a stowaway's face.",
            "scene_visko_prompt": "From the stowaway's face, a rival appears.",
            "choices": [raw_choice(5, 0, label="Help the stowaway"),
                        raw_choice(8, -6, label="Rob the stowaway")]}


@pytest.mark.parametrize("mutate", [
    lambda t: t["choices"].pop(),
    lambda t: t.pop("scene_visko_prompt"),
    lambda t: t.update(outcome_visko_prompt="You sprint across the quad."),
    lambda t: t.update(setting="The student is carrying a Red Bull."),
    lambda t: t["choices"][0].update(label="Run very fast toward the big rocket"),
    lambda t: t.update(outcome_narration=" ".join(["word"] * 15) + "."),
    lambda t: t["choices"][0].update(uses_item="jetpack"),
    lambda t: t["choices"][0].update(speed=float("nan")),
    lambda t: t.update(next_scene=" ".join(["word"] * 22) + " now?"),
    lambda t: t.update(next_scene="A rival appears. He blocks the gate. What now?"),
    lambda t: t.update(next_scene="A rival blocks the gate. You must get past him."),
    lambda t: t["choices"][0].update(score_note="for being kind to a very rude old stowaway"),
])
def test_validation_rejects(mutate):
    t = good_turn()
    mutate(t)
    with pytest.raises(ValueError):
        gm.validate_turn(t, ["alarm clock"])


def test_fallbacks_one_per_llm_checkpoint_and_follow_rules():
    assert set(FALLBACK) == {c["beat"] for c in game.CHECKPOINTS[1:]}
    for c_prev, c in zip(game.CHECKPOINTS, game.CHECKPOINTS[1:]):
        t = gm.validate_turn(FALLBACK[c["beat"]], [])
        s = game.GameState(items=[])
        before = [x["wellness_change"] for x in t["choices"]]
        assert [x["wellness_change"] for x in game.score_choices(s, c["beat"], t["choices"])] \
            == before  # no free wins
        text = json.dumps(FALLBACK[c["beat"]])
        assert "share" not in text.lower()
        assert not any(ch.isdigit() for ch in t["outcome_narration"] + t["next_scene"])
        # Continuity: a quest follows the decision at c_prev.
        if c_prev["after"]:
            lead_in = {"heat nap": "slowing", "blackout": "flicker",
                       "engine stall": "sputter"}[c_prev["after"]]
            assert lead_in in t["outcome_visko_prompt"], c["beat"]
            assert "arrives" in t["scene_visko_prompt"], c["beat"]
        else:
            assert t["scene_visko_prompt"].startswith("From "), c["beat"]


def all_fixed_prompts():
    prompts = [game.ROOMMATE_SETTING, game.ROOMMATE_SCENE_VISKO, game.FINAL_OUTCOME_VISKO,
               game.FINAL_OUTCOME_TAIL, game.STYLE] + list(game.ITEM_LOOK.values())
    prompts += [c["outcome_visko"] for c in game.ROOMMATE_CHOICES]
    prompts += list(game.ENDING_VISKO.values())
    prompts += list(game.KYLE_VISKO.values())
    for x in game.QUESTS.values():
        prompts += [x["visko"]] + [x[how][1] for how in ("skip", "passed")]
    return prompts


def test_fixed_prompts_follow_rules():
    assert set(game.ENDING_VISKO) == set(game.ENDING_TEXT)
    for p in all_fixed_prompts():
        assert not gm.SECOND_PERSON.search(p), p
        assert not gm.BRAND.search(p), p
        assert '"' not in p, p


def test_first_choose_uses_fixed_roommate_outcome():
    s, _ = game.new_game(["alarm clock", "flashlight", "mystery bag"])
    seen = {}
    turn = stub_turn("escape campus")
    r = game.choose(s, 1, lambda **kw: seen.update(kw) or copy.deepcopy(turn))
    fixed = game.ROOMMATE_CHOICES[1]["outcome_visko"]
    assert seen["fixed_outcome_visko"] == fixed and seen["quest_follows"] is None
    assert r["videos"][0]["prompt"] == prompt(CP["Dorm Room"], game.ROOMMATE_SETTING, fixed,
                                              r["items"])
    # The roommate outcome's narration is fixed too, so it matches its video.
    narration = game.ROOMMATE_CHOICES[1]["outcome_narration"]
    assert r["outcome_narration"] == r["videos"][0]["narration"] == narration
    assert r["mode"] == "decision" and s.woke_kyle is False and r["wellness"] == 98


def test_choices_may_not_use_items_reserved_for_the_quest(monkeypatch):
    t = good_turn()
    t["choices"][0].update(uses_item="red bull", label="Bribe him with Red Bull")
    gm.validate_turn(t, ["red bull"])
    with pytest.raises(ValueError):
        gm.validate_turn(t, ["red bull"], ["red bull"])
    monkeypatch.setattr(gm, "_ask_llm", lambda *a, **k: copy.deepcopy(t))
    out = gm.get_turn("build the rocket", "h", False, "x", ["red bull"], "on track", [],
                      quest_follows={"name": "heat nap", "outcome_ends_on": "x",
                                           "may_use_up": ["red bull"]})
    assert out == gm.validate_turn(FALLBACK["build the rocket"], ["red bull"])


def test_get_turn_payload(monkeypatch):
    payloads = []
    monkeypatch.setattr(gm, "_ask_llm", lambda p, *a, **k: payloads.append(p) or good_turn())
    gm.get_turn("build the rocket", "h", False, "x", [], "on track", [],
                quest_follows={"name": "heat nap", "outcome_ends_on": "slowing"})
    gm.get_turn("deep space", "h", False, "x", [], "on track", [])
    assert payloads[0]["quest_follows"]["name"] == "heat nap"
    assert payloads[1]["quest_follows"] is None


# ---------- retries, atomicity, API ----------

def test_get_turn_retries_with_the_reason_then_falls_back(monkeypatch):
    calls = []
    monkeypatch.setattr(gm, "_ask_llm",
                        lambda p, s, i, *a: calls.append(i) or (_ for _ in ()).throw(
                            ValueError("label over 6 words: 'x'")))
    out = gm.get_turn("deep space", "hint", False, "x", [], "on track", [])
    assert len(calls) == gm.TURN_ATTEMPTS == 3
    assert out == gm.validate_turn(FALLBACK["deep space"], [])
    assert "rejected" not in calls[0]
    assert "Your previous reply was rejected: label over 6 words" in calls[1]


def test_choose_is_all_or_nothing():
    s, _ = game.new_game(["flashlight", "cs textbook", "mystery bag"])
    before = copy.deepcopy(s)
    with pytest.raises(RuntimeError):
        game.choose(s, 1, lambda **_: (_ for _ in ()).throw(RuntimeError()))
    assert s == before


def test_api_full_game_with_use_item(monkeypatch):
    from fastapi.testclient import TestClient
    import main

    monkeypatch.setattr(gm, "_ask_llm", lambda *a, **k: (_ for _ in ()).throw(ValueError("x")))
    c = TestClient(main.app)
    # The alarm clock skips the heat nap; the other two quests run their quests.
    r = c.post("/start", json={"items": ["alarm clock", "cs textbook", "mystery bag"]}).json()
    sid = r["session_id"]
    assert r["mode"] == "decision" and r["quest_name"] is None
    assert c.post("/use_item", json={"session_id": sid, "item": None}).status_code == 400
    assert c.post("/quest_result", json={"session_id": sid, "passed": True}).status_code == 400
    modes, quests = [], 0
    for i in [0, 1, 1, 1, 1, 1]:
        r = c.post("/choose", json={"session_id": sid, "choice_index": i}).json()
        modes.append(r["mode"])
        if r["mode"] == "quest":
            assert c.post("/choose", json={"session_id": sid,
                                           "choice_index": 0}).status_code == 400
            assert c.post("/use_item", json={"session_id": sid,
                                             "item": "jetpack"}).status_code == 400
            d = r["quest"]
            if d["has_skip_item"]:
                r = c.post("/use_item", json={"session_id": sid,
                                              "item": d["skip_item"]}).json()
            else:
                assert c.post("/quest_result", json={"session_id": sid,
                                                     "passed": False}).status_code == 400
                assert c.post("/quest_result", json={"session_id": sid,
                                                     "passed": None}).status_code == 400
                r = c.post("/quest_result", json={"session_id": sid}).json()
                assert r["score_note"] == "quest passed"
                quests += 1
            modes.append(r["mode"])
    assert modes == ["decision", "quest", "decision", "quest", "decision",
                     "quest", "decision", "decision", "verdict"]
    assert r["game_over"] and len(r["verdict"]["key_moments"]) == 3
    assert quests == 2
    assert set(r) == {"session_id", "mode", "checkpoint_number", "checkpoint_name",
                      "checkpoint_total", "quest_name", "quest",
                      "outcome_narration", "videos",
                      "distance", "wellness", "light", "distance_change", "wellness_change",
                      "score_note", "items", "items_used", "delay_seconds", "next_scene",
                      "choices",
                      "choice_items",
                      "game_over", "ending_title", "ending", "verdict"}


def test_use_item_is_locked_too():
    from fastapi.testclient import TestClient
    import main

    c = TestClient(main.app)
    sid = c.post("/start", json={"items": ["alarm clock", "flashlight",
                                           "mystery bag"]}).json()["session_id"]
    main.LOCKS[sid].acquire()
    try:
        assert c.post("/use_item", json={"session_id": sid, "item": None}).status_code == 409
        assert c.post("/quest_result", json={"session_id": sid,
                                             "passed": True}).status_code == 409
        assert c.post("/choose", json={"session_id": sid, "choice_index": 0}).status_code == 409
    finally:
        main.LOCKS[sid].release()


# ---------- intro ----------

def test_intro_endpoint_comes_before_start():
    from fastapi.testclient import TestClient
    import main

    r = TestClient(main.app).get("/intro").json()
    assert r == {"opening": game.OPENING, "opening_video_seconds": 20,
                 "countdown_starts_at_second": 5, "timer_seconds": 15,
                 "items": game.ALLOWED_ITEMS, "grab_count": 3,
                 "distance": 238900, "wellness": 100}


def test_play_shows_opening_before_the_items(monkeypatch, capsys):
    import play

    monkeypatch.setattr("builtins.input", lambda _: "1 3 5")
    picked = play.pick_items(game.intro())
    out = capsys.readouterr().out
    assert picked == ["alarm clock", "flashlight", "mystery bag"]
    assert out.index("Breaking news") < out.index("seconds to grab") < out.index("1. alarm clock")


# ---------- Visko prompts: sky, capitals, items, ending ----------

EARTH = ("Dorm Room", "Campus", "Launch Site")
SPACE = ("Low Orbit", "Deep Space", "Moon Approach")


def all_prompts(rs):
    return [(v["kind"], v["prompt"]) for r in rs for v in r["videos"]]


def test_orange_sky_only_on_earth():
    assert "sky" not in game.STYLE
    for name in EARTH:
        assert "orange overheated sky" in V[name], name
    for visual in [V[n] for n in SPACE] + [game.MOON_SURFACE["visual"]]:
        assert "sky" not in visual, visual
    # ...and in a played game, no space or Moon clip mentions an overheated sky.
    _, rs, _ = play(["alarm clock", "flashlight", "mystery bag"], [0] * 6,
                    ["alarm clock", "flashlight", "pass"])
    for _, p in all_prompts(rs):
        if not any(V[n] in p for n in EARTH):
            assert "overheated sky" not in p, p


def test_every_sentence_after_style_starts_with_a_capital():
    for visual in list(V.values()) + [game.MOON_SURFACE["visual"]]:
        assert visual[0].isupper(), visual
    _, rs, _ = play(["alarm clock", "flashlight", "mystery bag"], [1] * 6,
                    [None, "flashlight", None])
    for _, p in all_prompts(rs):
        assert p.startswith(game.STYLE)
        assert p[len(game.STYLE)].isupper(), p
        assert ". a " not in p and ". the " not in p, p
    # Lowercase or unpunctuated LLM text is tidied too.
    v = game.video("scene", CP["Campus"], "the student runs", "a rival appears", "n")
    assert v["prompt"] == (f"{game.STYLE}{V['Campus']}. The student runs. A rival appears.")


@pytest.mark.parametrize("items,expected", [
    ([], ""),
    (["flashlight"], "The student's backpack holds a yellow flashlight."),
    (["alarm clock", "mystery bag"],
     "The student's backpack holds an old wind-up alarm clock and a crumpled brown paper bag."),
    (["red bull", "cs textbook", "flashlight"],
     "The student's backpack holds a silver-and-blue energy drink can, a thick computer "
     "science textbook and a yellow flashlight."),
])
def test_items_visual(items, expected):
    assert game.items_visual(items) == expected
    assert not gm.BRAND.search(expected)


def test_every_clip_shows_the_items_held_until_they_are_used():
    items = ["alarm clock", "flashlight", "red bull"]
    s, rs, _ = play(items, [0] * 6, ["alarm clock", None, "red bull"])
    look = game.ITEM_LOOK
    assert look["alarm clock"] in rs[0]["videos"][0]["prompt"]  # the dorm, at /start
    for r in rs:
        held = r["items"]
        scene = [v for v in r["videos"] if v["kind"] in ("scene", "quest", "ending")]
        for v in scene:  # clips after any spending show exactly what is still held
            assert game.items_visual(held) in v["prompt"], v
            for gone in set(items) - set(held):
                assert look[gone] not in v["prompt"], (gone, v)
    # The heat nap result shows the alarm clock being used; nothing after it shows it.
    heat = rs[3]
    assert look["alarm clock"] in heat["videos"][0]["prompt"]
    assert all(look["alarm clock"] not in p for _, p in all_prompts(rs[3:])[1:])
    assert s.items_used == ["alarm clock", "red bull"] and s.items == ["flashlight"]


def test_llm_is_told_what_is_held_and_what_is_spent():
    s, rs, calls = play(["alarm clock", "flashlight", "mystery bag"], [0] * 6,
                        ["alarm clock", "flashlight", None])
    held = [c["items"] for c in calls]
    spent = [c["items_used"] for c in calls]
    assert held[:2] == [["alarm clock", "flashlight", "mystery bag"]] * 2
    assert held[2] == ["flashlight", "mystery bag"] and spent[2] == ["alarm clock"]
    assert held[3] == ["mystery bag"] and spent[3] == ["alarm clock", "flashlight"]
    assert held[4] == ["mystery bag"]


def test_choice_that_uses_an_item():
    s = state_at("Campus", items=["alarm clock", "flashlight", "mystery bag"])
    using = dict(raw_choice(5, 0), uses_item="flashlight")
    set_pending(s, [using, raw_choice(3, 2)])
    seen = {}
    r = game.choose(s, 0, lambda **kw: seen.update(kw) or stub_turn(**kw))
    assert seen["just_used"] == "flashlight"
    assert seen["items"] == ["alarm clock", "mystery bag"]
    assert seen["items_used"] == ["flashlight"]
    outcome, quest = r["videos"]
    assert game.ITEM_LOOK["flashlight"] in outcome["prompt"]  # used during this clip
    assert game.ITEM_LOOK["flashlight"] not in quest["prompt"]
    assert r["items"] == ["alarm clock", "mystery bag"]


def test_last_decision_shows_the_chosen_action_then_the_engines_dying():
    for pick in (0, 1):
        _, rs, _ = play(["alarm clock", "flashlight", "mystery bag"], [0] * 5 + [pick],
                        ["alarm clock", "flashlight", "pass"])
        chosen = FALLBACK["final approach"]["choices"][pick]
        outcome = rs[-1]["videos"][0]
        assert outcome["kind"] == "outcome"
        assert outcome["prompt"].endswith(chosen["outcome_visko_prompt"] + " "
                                          + game.FINAL_OUTCOME_TAIL)
        assert outcome["narration"] == (chosen["outcome_narration"] + " "
                                        + game.FINAL_OUTCOME_NARRATION_TAIL)
        assert rs[-1]["outcome_narration"] == outcome["narration"]


def test_each_decision_shows_its_outcome_then_the_next_scene():
    """Before every decision the player has seen the last choice's outcome clip and,
    last of all, the clip of the scene they now decide on."""
    _, rs, _ = play(["alarm clock", "flashlight", "mystery bag"], [0] * 6,
                    ["alarm clock", "flashlight", "pass"])
    since_last_decision = []
    for r in rs[1:]:
        since_last_decision += kinds(r)
        if r["mode"] == "decision":
            assert since_last_decision[0] == "outcome" and since_last_decision[-1] == "scene"
            assert r["videos"][-1]["narration"] == r["next_scene"]
            since_last_decision = []
    assert kinds(rs[-1]) == ["outcome", "ending"]


def test_fallbacks_name_no_items():
    text = json.dumps(FALLBACK)
    assert "duct" not in text.lower()
    for beat, t in FALLBACK.items():
        texts = [t[f] for f in gm.TURN_FIELDS if f != "choices"]
        for c in t["choices"]:
            texts += [c["label"], c["score_note"], c.get("outcome_narration"),
                      c.get("outcome_visko_prompt")]
        for x in texts:
            assert not gm.items_named(x), (beat, x)
    # The final fallback validates as a last-decision turn.
    gm.validate_turn(FALLBACK["final approach"], [], choice_outcomes=True)


# ---------- validation: items and the last decision ----------

@pytest.mark.parametrize("field,value", [
    ("setting", "The student carries a yellow flashlight."),
    ("setting", "The student hugs a thick textbook."),
    ("next_scene", "A rival grabs your energy drink. How do you get it back?"),
    ("scene_visko_prompt", "From the hatch, a rival waves a paper bag."),
    ("outcome_narration", "You smash your alarm clock on the hatch."),
])
def test_validation_rejects_items_not_held(field, value):
    t = good_turn()
    t[field] = value
    with pytest.raises(ValueError):
        gm.validate_turn(t, ["cs textbook"] if field != "setting" else ["flashlight",
                                                                        "cs textbook"])


def test_items_held_and_just_used_may_be_mentioned():
    t = good_turn()
    t["next_scene"] = "A stowaway eyes your flashlight. How do you get past him?"
    t["choices"][0]["label"] = "Blind him with the flashlight"
    gm.validate_turn(t, ["flashlight"])
    with pytest.raises(ValueError):  # might be spent on the quest first
        gm.validate_turn(t, ["flashlight"], ["flashlight"])
    t = good_turn()
    t["outcome_narration"] = "Your alarm clock wakes the whole quad."
    gm.validate_turn(t, [], just_used="alarm clock")
    t["next_scene"] = "Your alarm clock is ringing. How do you silence it?"
    with pytest.raises(ValueError):  # gone after the outcome
        gm.validate_turn(t, [], just_used="alarm clock")


def final_turn():
    t = good_turn()
    for c, n in zip(t["choices"], ("help", "rob")):
        c["outcome_narration"] = f"You {n} the stowaway and keep flying."
        c["outcome_visko_prompt"] = f"The student turns to {n} the stowaway."
    return t


def test_last_decision_turn_needs_an_outcome_per_choice():
    out = gm.validate_turn(final_turn(), [], choice_outcomes=True)
    assert out["choices"][0]["outcome_visko_prompt"] == "The student turns to help the stowaway."
    with pytest.raises(ValueError):
        gm.validate_turn(good_turn(), [], choice_outcomes=True)
    # Other turns ignore these fields.
    assert "outcome_narration" not in gm.validate_turn(final_turn(), [])["choices"][0]


@pytest.mark.parametrize("mutate", [
    lambda c: c.update(outcome_narration="You land softly in a crater."),
    lambda c: c.update(outcome_visko_prompt="The student touches down on the Moon."),
    lambda c: c.update(outcome_visko_prompt="You steer toward the pod."),
    lambda c: c.update(outcome_narration=" ".join(["word"] * 15) + "."),
    lambda c: c.update(outcome_visko_prompt="The student sips a Red Bull."),
])
def test_last_decision_outcomes_follow_the_rules(mutate):
    t = final_turn()
    mutate(t["choices"][1])
    with pytest.raises(ValueError):
        gm.validate_turn(t, ["red bull"], choice_outcomes=True)


def test_get_turn_sends_items_and_final_instructions(monkeypatch):
    seen = []
    monkeypatch.setattr(gm, "_ask_llm",
                        lambda p, s, i, m: seen.append((p, i, m)) or final_turn())
    gm.get_turn("final approach", "h", False, "x", ["flashlight"], "on track", [],
                items_used=["alarm clock"], just_used="alarm clock", choice_outcomes=True)
    payload, instructions, max_tokens = seen[0]
    assert payload["items_held"] == ["flashlight"]
    assert payload["items_used"] == ["alarm clock"] and payload["just_used"] == "alarm clock"
    assert payload["choice_outcomes"] is True and max_tokens == gm.MAX_TOKENS_FINAL
    assert '"outcome_visko_prompt": "..."}' in instructions


def test_final_fallback_keeps_choice_outcomes(monkeypatch):
    monkeypatch.setattr(gm, "_ask_llm", lambda *a, **k: (_ for _ in ()).throw(ValueError()))
    out = gm.get_turn("final approach", "h", False, "x", [], "on track", [],
                      choice_outcomes=True)
    assert all(c["outcome_visko_prompt"] for c in out["choices"])


def test_verdict_retries_once_and_rejects_spent_items(monkeypatch):
    bad = {"traveler_type": "The Snack Diplomat", "traveler_blurb": "You did that.",
           "verdict_visko_prompt": "The student lands holding a thick textbook."}
    calls = []
    monkeypatch.setattr(gm, "_ask_llm", lambda *a, **k: calls.append(1) or dict(bad))
    assert gm.get_verdict("Moon Citizen", "e", 0, 90, [], {}, items=["flashlight"]) is None
    assert len(calls) == 2
    calls.clear()
    assert gm.get_verdict("Moon Citizen", "e", 0, 90, [], {}, items=["cs textbook"])
    assert len(calls) == 1


def test_play_labels_each_clip_and_its_visko_prompt(capsys):
    import play

    s, r = game.new_game(["alarm clock", "flashlight", "mystery bag"])
    play.show(r)
    out = capsys.readouterr().out
    assert "[VIDEO 1 of 1: what's in front of you now (scene)]" in out
    assert "Visko prompt: Claymation style" in out and "Narration: Kyle is asleep" in out


# ---------- review fixes: continuity, reserved items, item inference, final turn ----------

def test_llm_sees_the_scene_the_player_decided_on():
    s, rs, calls = play(["alarm clock", "flashlight", "mystery bag"], [0] * 6,
                        ["alarm clock", "pass", "pass"])
    assert calls[0]["previous_scene"] is None  # the roommate outcome is fixed
    # Campus: the scene shown at Campus is what the Launch Site turn continues from.
    campus = gm.validate_turn(FALLBACK["escape campus"], [])
    assert calls[1]["previous_scene"] == {"next_scene": campus["next_scene"],
                                          "scene_visko_prompt": campus["scene_visko_prompt"]}
    # After a quest, the held scene is the one the player decided on.
    for i, beat in ((2, "build the rocket"), (3, "in space"), (4, "deep space")):
        t = gm.validate_turn(FALLBACK[beat], [])
        assert calls[i]["previous_scene"]["next_scene"] == t["next_scene"], beat
    assert all(c["kyle_came_along"] is True for c in calls)


def test_payload_hides_items_saved_for_the_quest(monkeypatch):
    seen = []
    monkeypatch.setattr(gm, "_ask_llm", lambda p, *a: seen.append(p) or good_turn())
    gm.get_turn("build the rocket", "h", False, "x", ["alarm clock", "flashlight"],
                "on track", [], quest_follows={"name": "heat nap", "outcome_ends_on": "x",
                                                     "may_use_up": ["alarm clock"]},
                previous_scene={"next_scene": "n", "scene_visko_prompt": "v"},
                kyle_came_along=True)
    p = seen[0]
    assert p["items_held"] == ["flashlight"]
    assert p["items_saved_for_quest"] == ["alarm clock"]
    assert p["quest_follows"] == {"name": "heat nap", "outcome_ends_on": "x"}
    assert p["previous_scene"]["scene_visko_prompt"] == "v" and p["kyle_came_along"] is True


def test_label_that_uses_an_item_spends_it():
    t = good_turn()
    t["choices"][0]["label"] = "Throw the alarm clock"
    out = gm.validate_turn(t, ["alarm clock", "flashlight"])
    assert out["choices"][0]["uses_item"] == "alarm clock"
    t["choices"][0]["uses_item"] = "flashlight"  # names one item, spends another
    with pytest.raises(ValueError):
        gm.validate_turn(t, ["alarm clock", "flashlight"])
    # ...and the game removes it when chosen.
    s = state_at("Deep Space", items=["alarm clock", "flashlight", "mystery bag"])
    s.pending_choices = game.score_choices(s, "deep space", out["choices"])
    r = game.choose(s, 0, stub_turn)
    assert "alarm clock" not in r["items"] and s.items_used == ["alarm clock"]


@pytest.mark.parametrize("text", [
    "The student flips on the landing lights and eyes the pod.",
    "A rescue ship arrives beside the rocket.",
    "The student swerves around the pod's crashing debris.",
])
def test_final_outcome_allows_harmless_words(text):
    t = final_turn()
    t["choices"][0]["outcome_visko_prompt"] = text
    gm.validate_turn(t, [], choice_outcomes=True)


@pytest.mark.parametrize("text", [
    "The rocket settles onto the lunar surface.",
    "The student reaches the Moon at last.",
    "The student opens the hatch and floats toward the pod.",
    "The student climbs out to grab the cable.",
])
def test_final_outcome_may_not_end_the_journey_or_leave_the_cabin(text):
    t = final_turn()
    t["choices"][0]["outcome_visko_prompt"] = text
    with pytest.raises(ValueError):
        gm.validate_turn(t, [], choice_outcomes=True)


def test_final_choice_outcome_shows_only_its_own_item():
    t = final_turn()
    t["choices"][0]["outcome_visko_prompt"] = "The student waves a yellow flashlight at the pod."
    with pytest.raises(ValueError):  # held, but this choice does not use it
        gm.validate_turn(t, ["flashlight"], choice_outcomes=True)
    t["choices"][0].update(uses_item="flashlight", label="Signal with the flashlight")
    gm.validate_turn(t, ["flashlight"], choice_outcomes=True)


def test_final_outcome_drops_its_own_ending_image():
    s = state_at("Moon Approach", distance=20000, wellness=90, last_setting="S.")
    c = dict(raw_choice(5, 0), outcome_narration="You tow it.",
             outcome_visko_prompt="The student tows the pod, ending on the pod's window.")
    set_pending(s, [c, raw_choice(1, 1)])
    r = game.choose(s, 0, lambda **_: None)
    p = r["videos"][0]["prompt"]
    assert p.endswith("The student tows the pod. " + game.FINAL_OUTCOME_TAIL)
    assert p.count("ending on") == 1


def test_fixed_text_the_player_reads_is_short():
    assert gm.word_count(game.OPENING) <= 35
    assert gm.word_count(game.ROOMMATE_SCENE) <= 12 and game.ROOMMATE_SCENE.endswith("?")
    for c in game.ROOMMATE_CHOICES:
        assert gm.word_count(c["outcome_narration"]) <= gm.MAX_OUTCOME_WORDS
    for title, text in game.ENDING_TEXT.items():
        assert gm.word_count(text) <= 15, title
    for line in game.KYLE_LINES.values():
        assert gm.word_count(line) <= 12
    for blurb in (b for _, b in game.TRAVELER_FALLBACK.values()):
        assert gm.word_count(blurb) <= gm.MAX_BLURB_WORDS
    assert (gm.MAX_OUTCOME_WORDS, gm.MAX_SCENE_WORDS, gm.MAX_LABEL_WORDS) == (14, 22, 6)


def test_play_skips_quests_in_the_terminal(capsys):
    import play

    s, r = at_quest("heat nap", ["alarm clock", "flashlight", "mystery bag"])
    out = play.handle_quest(r, lambda sid, ok: game.quest_result(s, ok))
    assert out["mode"] == "decision" and out["score_note"] == "quest passed"
    assert out["items"] == ["alarm clock", "flashlight", "mystery bag"]  # nothing used
    assert "skipped here; quests are played in Unity" in capsys.readouterr().out


def test_a_choice_that_uses_an_item_shows_it():
    t = good_turn()
    t["choices"][0]["uses_item"] = "cs textbook"  # "Help the stowaway" hides the cost
    out = gm.validate_turn(t, ["cs textbook"])["choices"][0]
    assert out["label"] == "Help the stowaway (uses CS textbook)"
    t["choices"][0]["label"] = "Help him with the textbook"
    out = gm.validate_turn(t, ["cs textbook"])["choices"][0]
    assert out["label"] == "Help him with the textbook" and out["uses_item"] == "cs textbook"
    # The response tells Unity which item each choice would use up.
    s = state_at("Deep Space", items=["cs textbook", "flashlight", "mystery bag"])
    s.pending_choices = game.score_choices(s, "deep space",
                                           gm.validate_turn(t, ["cs textbook"])["choices"])
    assert game._response(s, "", [], None, "")["choice_items"] == ["cs textbook", None]


def test_naming_an_item_the_other_choice_uses_is_rejected_not_spent():
    t = good_turn()
    t["choices"][0].update(label="Bribe him with Red Bull", uses_item="red bull")
    t["choices"][1]["label"] = "Keep your Red Bull"
    with pytest.raises(ValueError):
        gm.validate_turn(t, ["red bull"])


@pytest.mark.parametrize("quest,text", [
    ("heat nap", "You shove Brad and race toward the launch pad."),
    ("heat nap", "You're almost off campus."),
    ("blackout", "Priya takes off her goggles and shakes your hand."),
    ("blackout", "Kyle launches into a victory dance."),
    ("blackout", "Your rocket is built and ready for orbit."),
    ("blackout", "The pad lights flicker before liftoff."),
    ("engine stall", "You ditch Cart and aim for the Moon."),
    ("engine stall", "You fling the junk out toward deep space."),
])
def test_outcome_may_head_toward_the_next_place(quest, text):
    t = good_turn()
    t["outcome_narration"] = text
    gm.validate_turn(t, [], quest=quest)


def test_lead_ins_and_instructions_pass_their_own_checks():
    for name, d in game.QUESTS.items():
        assert not gm.TOO_FAR_BEFORE[name].search(d["lead_in"]), name
        assert not gm.TOO_FAR_BEFORE[name].search(gm.TOO_FAR_HINT[name]), name


def test_kyle_rides_along_in_every_clip_once_woken():
    _, rs, _ = play(["alarm clock", "flashlight", "mystery bag"], [0] * 6,
                    ["alarm clock", "pass", "pass"])
    for r in rs[2:]:
        for v in r["videos"]:
            assert "roommate" in v["prompt"], v
    _, rs, _ = play(["alarm clock", "flashlight", "mystery bag"], [1] + [0] * 5,
                    ["alarm clock", "pass", "pass"])
    assert not any("rides along" in v["prompt"] for r in rs for v in r["videos"])


@pytest.mark.parametrize("quest,text", [
    ("heat nap", "You make it off campus, sweaty but alive."),
    ("heat nap", "The student runs onto the launch pad."),
    ("blackout", "Your rocket lifts off in a cloud of smoke."),
    ("blackout", "You build and launch the rocket toward the Moon."),
    ("engine stall", "You reach the Moon at last."),
    ("blackout", "Your clever controls lift the rocket into low orbit."),
    ("engine stall", "You coast out into deep space."),
])
def test_outcome_stays_before_the_coming_quest(quest, text):
    t = good_turn()
    t["outcome_narration"] = text
    gm.validate_turn(t, [])  # fine when no quest follows
    with pytest.raises(ValueError):
        gm.validate_turn(t, [], quest=quest)


def test_fallbacks_stay_before_their_quests():
    for c_prev, c in zip(game.CHECKPOINTS, game.CHECKPOINTS[1:]):
        gm.validate_turn(FALLBACK[c["beat"]], [], quest=c_prev["after"],
                         choice_outcomes=c is game.CHECKPOINTS[-1])


def test_item_names_are_capitalised_for_the_player():
    t = good_turn()
    t["outcome_narration"] = "Your cs textbook and red bull save you."
    t["choices"][0].update(label="Read the cs textbook", uses_item="cs textbook")
    out = gm.validate_turn(t, ["cs textbook", "red bull"], just_used=None)
    assert out["outcome_narration"] == "Your CS textbook and Red Bull save you."
    assert out["choices"][0]["label"] == "Read the CS textbook"



@pytest.mark.parametrize("items", [[], ["flashlight"], ["red bull", "mystery bag"],
                                   ["flashlight", "flashlight", "red bull"],
                                   ["jetpack", "flashlight", "red bull"],
                                   ["alarm clock", "flashlight", "red bull", "cs textbook"]])
def test_start_needs_exactly_three_items(items):
    with pytest.raises(game.GameError):
        game.new_game(items)


def test_start_does_not_repeat_the_prerecorded_opening():
    _, r = game.new_game(["flashlight", "cs textbook", "mystery bag"])
    assert r["outcome_narration"] == ""
    assert game.OPENING not in json.dumps(r)
    assert "jolts awake" not in r["videos"][0]["prompt"]  # already awake grabbing items



def test_used_items_are_discarded_and_marked_used():
    s, rs, calls = play(["alarm clock", "flashlight", "red bull"], [0] * 6,
                        ["alarm clock", None, "red bull"])
    assert rs[3]["items"] == ["flashlight", "red bull"]
    assert rs[3]["items_used"] == ["alarm clock"]
    assert rs[-1]["items_used"] == ["alarm clock", "red bull"]
    assert calls[2]["items_used"] == ["alarm clock"]
    assert "used the alarm clock" not in " ".join(s.history)  # skipped, not chosen
    assert any("(used, discarded)" in h for h in s.history)
