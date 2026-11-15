"""Terminal client: play the whole game in text.

  python play.py                         # talks to http://localhost:8000
  python play.py --url http://host:8000  # another server
  python play.py --local                 # no server, runs the engine in-process
  python play.py --debug                 # in-process, shows hidden scores before you pick

--debug always runs in-process: the server never sends choice scores to clients.
"""

import argparse
import json
import sys
import textwrap
import urllib.error
import urllib.request



def http_client(base_url):
    def post(path, body):
        req = urllib.request.Request(
            base_url.rstrip("/") + path,
            data=json.dumps(body).encode(),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(req, timeout=90) as r:
                return json.loads(r.read())
        except urllib.error.HTTPError as e:
            sys.exit(f"Server error {e.code}: {e.read().decode()}")
        except urllib.error.URLError as e:
            sys.exit(f"Can't reach {base_url} ({e.reason}). Is uvicorn running? "
                     "Or try --local.")

    def get(path):
        try:
            with urllib.request.urlopen(base_url.rstrip("/") + path, timeout=30) as r:
                return json.loads(r.read())
        except urllib.error.URLError as e:
            sys.exit(f"Can't reach {base_url} ({getattr(e, 'reason', e)}). Is uvicorn "
                     "running? Or try --local.")

    intro = lambda: get("/intro")  # noqa: E731
    start = lambda items: post("/start", {"items": items})  # noqa: E731
    choose = lambda sid, i: post("/choose", {"session_id": sid, "choice_index": i})  # noqa: E731
    use = lambda sid, item: post("/use_item", {"session_id": sid, "item": item})  # noqa: E731
    quest = lambda sid, ok: post("/quest_result", {"session_id": sid, "passed": ok})  # noqa: E731
    return intro, start, choose, use, quest, None


def local_client():
    from pathlib import Path

    from dotenv import load_dotenv
    load_dotenv(Path(__file__).parent / ".env")
    import game
    import gm

    sessions = {}

    def start(items):
        state, resp = game.new_game(items)
        sessions[state.session_id] = state
        return resp

    def choose(sid, i):
        return game.choose(sessions[sid], i, gm.get_turn, gm.get_verdict)

    def use(sid, item):
        return game.use_item(sessions[sid], item)

    def quest(sid, passed):
        return game.quest_result(sessions[sid], passed)

    def peek(sid):
        return sessions[sid].pending_choices

    return game.intro, start, choose, use, quest, peek


def wrap(text):
    return textwrap.fill(text, width=78)


def pick_items(intro):
    """The pre-recorded opening video plays first; its countdown starts partway in, and
    the player must grab three items before it ends. (Not timed in the terminal.)"""
    print(f"[OPENING VIDEO, pre-recorded, {intro['opening_video_seconds']}s]")
    print(wrap(intro["opening"]))
    print(f"\n  Distance to the Moon: {intro['distance']:,} mi | "
          f"Wellness: {intro['wellness']}%")
    items, count = intro["items"], intro["grab_count"]
    print(f"\n{intro['countdown_starts_at_second']}s in, the countdown starts. You have "
          f"{intro['timer_seconds']} seconds to grab {count} items:")
    for n, item in enumerate(items, 1):
        print(f"  {n}. {item}")
    while True:
        raw = input(f"Pick {count} numbers (e.g. 1 3 5): ")
        try:
            nums = sorted({int(x) for x in raw.replace(",", " ").split()})
        except ValueError:
            continue
        if len(nums) == count and all(1 <= n <= len(items) for n in nums):
            return [items[n - 1] for n in nums]
        print(f"Exactly {count} different numbers from 1 to {len(items)}.")


VIDEO_TITLES = {
    "outcome": "what your choice caused",
    "scene": "what's in front of you now",
    "quest": "a quest begins",
    "quest_result": "how the quest turned out",
    "ending": "the ending",
}


def show(resp, prev=None):
    """prev is the previous response, or None for the opening screen."""
    print("\n" + "=" * 78)
    print(f"Checkpoint {resp['checkpoint_number']} of {resp['checkpoint_total']}: "
          f"{resp['checkpoint_name']}")
    videos = resp["videos"]
    for n, v in enumerate(videos, 1):
        title = VIDEO_TITLES.get(v["kind"], v["kind"])
        print(f"\n[VIDEO {n} of {len(videos)}: {title} ({v['kind']})]")
        print(textwrap.fill(v["prompt"], width=78, initial_indent="  Visko prompt: ",
                            subsequent_indent="    "))
        if v["kind"] != "ending":  # the verdict block prints the ending text
            print(textwrap.fill(v["narration"], width=78, initial_indent="  Narration: ",
                                subsequent_indent="    "))
    if prev and resp["videos"][0]["kind"] == "quest_result":
        note = f"  ({resp['score_note']})" if resp.get("score_note") else ""
        print(f"\n  Quest over: {resp['distance_change']:+,} mi | "
              f"{resp['wellness_change']:+d} wellness{note}")
    elif prev:
        note = f"  ({resp['score_note']})" if resp.get("score_note") else ""
        print(f"\n  Miles gained: {-resp['distance_change']:,} | "
              f"Wellness change: {resp['wellness_change']:+d}{note}")
    light = (resp.get("light") or "-").upper()
    used = f" | Used: {', '.join(resp['items_used'])}" if resp.get("items_used") else ""
    print(f"  Distance: {resp['distance']:,} mi | Wellness: {resp['wellness']}% | "
          f"Light: {light} | Items: {', '.join(resp['items']) or 'none'}{used}")


def handle_quest(resp, quest):
    """Quests are played in Unity, so the terminal skips them: it reports the quest as
    passed without using any item."""
    d = resp["quest"]
    skip = f" (skip item: {d['skip_item']})" if d["has_skip_item"] else ""
    print(f"\nQUEST: {d['name']}{skip}. Task in Unity: {d['task']['instructions']}")
    print("  ...skipped here; quests are played in Unity.")
    return quest(resp["session_id"], True)


def show_verdict(v):
    print("\n" + "*" * 78)
    print(v["ending_title"].upper())
    print(wrap(v["ending"]))
    print(f"\nFinal distance: {v['final_distance']:,} mi | "
          f"Final wellness: {v['final_wellness']}%")
    if v.get("traveler_type"):
        print(f"\nYou are: {v['traveler_type']}")
        print(wrap(v["traveler_blurb"]))
        if v.get("traveler_detail"):
            print(wrap(v["traveler_detail"]))
    print("\nHow you got here:")
    for moment in v["key_moments"]:
        print(f"- {moment}")
    print("*" * 78)


def show_debug(pending):
    for n, c in enumerate(pending, 1):
        speed = c.get("speed", "-")
        payoff = " payoff" if c.get("payoff") else ""
        print(f"     [debug {n}] speed={speed} wellness_change={c['wellness_change']:+d} "
              f"miles={c['miles']:,} light={c['light']}{payoff}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", default="http://localhost:8000")
    ap.add_argument("--local", action="store_true")
    ap.add_argument("--debug", action="store_true",
                    help="show both choices' speed and wellness_change (runs in-process)")
    args = ap.parse_args()
    if args.debug and not args.local:
        print("(--debug runs the engine in-process; the server never exposes scores)")
    intro, start, choose, use, quest, peek = (local_client() if args.local or args.debug
                                              else http_client(args.url))

    print("\nFLY ME TO THE MOON\n")
    resp = start(pick_items(intro()))
    prev = None
    while True:
        show(resp, prev)
        if resp["game_over"]:
            show_verdict(resp["verdict"])
            break
        if resp["mode"] == "quest":
            prev, resp = resp, handle_quest(resp, quest)
            continue
        print()
        for n, label in enumerate(resp["choices"], 1):
            print(f"  {n}. {label}")
        if args.debug:
            show_debug(peek(resp["session_id"]))
        choice = ""
        while choice not in ("1", "2"):
            choice = input("Choose 1 or 2: ").strip()
        print("  ...the game master is thinking...")
        prev, resp = resp, choose(resp["session_id"], int(choice) - 1)


if __name__ == "__main__":
    try:
        main()
    except (KeyboardInterrupt, EOFError):
        print("\nYou stayed on Earth. It's 451 degrees. Bye.")
