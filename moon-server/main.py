"""FastAPI game master server for "Fly Me to the Moon".

Run: uvicorn main:app --reload
"""

import logging
import threading
from typing import Optional

from dotenv import load_dotenv

load_dotenv()

from fastapi import FastAPI, HTTPException  # noqa: E402
from fastapi.middleware.cors import CORSMiddleware  # noqa: E402
from pydantic import BaseModel  # noqa: E402

import game  # noqa: E402
import gm  # noqa: E402

logging.basicConfig(level=logging.INFO)

app = FastAPI(title="Fly Me to the Moon - Game Master")
app.add_middleware(
    CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"]
)

SESSIONS: dict = {}  # session_id -> game.GameState (in memory)
LOCKS: dict = {}  # session_id -> threading.Lock (one request per session at a time)


class StartBody(BaseModel):
    items: list[str]  # exactly three names from /intro's items


class ChooseBody(BaseModel):
    session_id: str
    choice_index: int  # 0 or 1


class UseItemBody(BaseModel):
    session_id: str
    item: Optional[str] = None  # the quest's skip item (response.quest.skip_item)


class QuestBody(BaseModel):
    session_id: str
    # Players retry until they pass, so this is always true; anything else gets 400.
    passed: Optional[bool] = True


# Plain `def` endpoints: FastAPI runs them in a threadpool, so the blocking
# LLM call doesn't stall other requests.
@app.get("/intro")
def intro():
    """The opening broadcast and the items to grab; show this before /start."""
    return game.intro()


@app.post("/start")
def start(body: StartBody):
    try:
        state, resp = game.new_game(body.items)
    except game.GameError as e:
        raise HTTPException(status_code=400, detail=str(e))
    SESSIONS[state.session_id] = state
    LOCKS[state.session_id] = threading.Lock()
    return resp


def _locked(session_id: str, step):
    """Run one step for a session, one request at a time."""
    state = SESSIONS.get(session_id)
    if state is None:
        raise HTTPException(status_code=404, detail="Unknown session_id.")
    lock = LOCKS[session_id]
    if not lock.acquire(blocking=False):
        raise HTTPException(status_code=409, detail="A request for this session is already "
                            "being processed.")
    try:
        return step(state)
    except game.GameError as e:
        raise HTTPException(status_code=400, detail=str(e))
    finally:
        lock.release()


@app.post("/choose")
def choose(body: ChooseBody):
    return _locked(body.session_id, lambda s: game.choose(s, body.choice_index, gm.get_turn,
                                                           gm.get_verdict))


@app.post("/use_item")
def use_item(body: UseItemBody):
    """Skip the waiting quest with its skip item, which is used up."""
    return _locked(body.session_id, lambda s: game.use_item(s, body.item))


@app.post("/quest_result")
def quest_result(body: QuestBody):
    """Finish the waiting quest once the player has passed the Unity quest."""
    return _locked(body.session_id, lambda s: game.quest_result(s, body.passed))
