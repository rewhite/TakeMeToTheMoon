"""Local bridge between Unity and Reactor's Visko Orbis model.

Unity has no Reactor SDK, so this process holds the Reactor session (via the
Python `reactor-sdk`) and relays it to Unity over a localhost TCP socket:

  * video frames  -> Unity (raw RGB24, rows already flipped bottom-up for Texture2D)
  * audio samples -> Unity (int16 PCM)
  * model events  -> Unity (JSON, forwarded unchanged: state, chunk_complete, ...)
  * commands      <- Unity (JSON {"command": ..., "data": {...}})
  * start image   <- Unity (PNG/JPG bytes, uploaded and sent as set_image)

A Reactor session is opened when Unity connects and closed when Unity
disconnects (e.g. leaving Play mode), so you're only billed while playing.

Wire format, little-endian, every message: [u8 type][u32 length][payload]
  0x01 VIDEO  bridge->unity  u16 width, u16 height, RGB24 bytes (bottom row first)
  0x02 AUDIO  bridge->unity  u32 sample_rate, u16 channels, int16 interleaved PCM
  0x03 JSON   both ways      UTF-8 JSON
  0x04 IMAGE  unity->bridge  encoded image bytes (PNG or JPG)

Usage:
  set REACTOR_API_KEY (or put it in ReactorBridge/.env), then
  .venv\\Scripts\\python visko_bridge.py [--port 7777] [--model reactor/visko-orbis-stable]
"""

from __future__ import annotations

import argparse
import asyncio
import json
import logging
import os
import struct
from pathlib import Path

import numpy as np
from reactor_sdk import Reactor, ReactorError, ReactorStatus

MSG_VIDEO, MSG_AUDIO, MSG_JSON, MSG_IMAGE = 1, 2, 3, 4
HEADER = struct.Struct("<BI")
MAX_INBOUND = 32 * 1024 * 1024
# Drop video frames instead of queueing them if Unity falls behind.
MAX_VIDEO_BACKLOG_BYTES = 16 * 1024 * 1024

log = logging.getLogger("visko_bridge")


def load_api_key() -> str:
    key = os.environ.get("REACTOR_API_KEY")
    env_file = Path(__file__).with_name(".env")
    if not key and env_file.exists():
        for line in env_file.read_text(encoding="utf-8").splitlines():
            name, _, value = line.partition("=")
            if name.strip() == "REACTOR_API_KEY":
                key = value.strip().strip('"').strip("'")
    if not key:
        raise SystemExit(
            "REACTOR_API_KEY is not set. Set the environment variable or add "
            "REACTOR_API_KEY=rk_... to ReactorBridge/.env"
        )
    return key


class UnitySession:
    """One connected Unity client and the Reactor session that serves it."""

    def __init__(self, reader, writer, model: str, api_key: str):
        self.reader = reader
        self.writer = writer
        self.model = model
        self.api_key = api_key
        self.loop = asyncio.get_running_loop()
        self.ready = asyncio.Event()
        self.reactor: Reactor | None = None
        self.closed = False

    # ---- sending to Unity (loop thread only) ----

    def _write(self, msg_type: int, payload: bytes) -> None:
        if self.closed or self.writer.is_closing():
            return
        self.writer.write(HEADER.pack(msg_type, len(payload)))
        self.writer.write(payload)

    def send_json(self, obj) -> None:
        self._write(MSG_JSON, json.dumps(obj).encode("utf-8"))

    def _send_video(self, payload: bytes) -> None:
        transport = self.writer.transport
        if transport is not None and transport.get_write_buffer_size() > MAX_VIDEO_BACKLOG_BYTES:
            return  # Unity is behind; skip this frame
        self._write(MSG_VIDEO, payload)

    # ---- Reactor callbacks ----
    # Media handlers run on the SDK's native delivery thread, so they only do
    # numpy work there and hand the bytes to the event loop.

    def on_video(self, frame) -> None:
        h, w = frame.shape[0], frame.shape[1]
        flipped = np.ascontiguousarray(frame[::-1])  # Unity textures start at the bottom row
        payload = struct.pack("<HH", w, h) + flipped.tobytes()
        self.loop.call_soon_threadsafe(self._send_video, payload)

    def on_audio(self, frame, sample_rate, num_channels) -> None:
        pcm = np.ascontiguousarray(frame, dtype=np.int16).tobytes()
        payload = struct.pack("<IH", sample_rate, num_channels) + pcm
        self.loop.call_soon_threadsafe(self._write, MSG_AUDIO, payload)

    def on_message(self, msg) -> None:
        if isinstance(msg, dict):
            if msg.get("type") == "command_error":
                log.warning("command_error: %s", msg.get("data"))
            self.send_json(msg)

    def on_status(self, status) -> None:
        name = str(getattr(status, "value", status)).lower()  # "ready", not "ReactorStatus.READY"
        log.info("Reactor status: %s", name)
        self.send_json({"type": "bridge_status", "data": {"status": name}})
        if status == ReactorStatus.READY:
            self.ready.set()
        else:
            self.ready.clear()

    def on_error(self, err) -> None:
        log.error("Reactor error: %s", err)
        self.send_json({"type": "bridge_error", "data": {"reason": str(err)}})

    # ---- commands from Unity ----

    async def run_command(self, command: str, data: dict) -> None:
        await self.ready.wait()
        try:
            reply = await self.reactor.send_command(command, data)
            self.send_json({"type": "command_reply", "data": {"command": command, "reply": reply}})
        except ReactorError as e:
            log.warning("%s refused: %s", command, e)
            self.send_json({"type": "command_error", "data": {"command": command, "reason": str(e)}})

    async def set_image(self, image: bytes) -> None:
        await self.ready.wait()
        mime = "image/png" if image[:8] == b"\x89PNG\r\n\x1a\n" else "image/jpeg"
        name = "start.png" if mime == "image/png" else "start.jpg"
        try:
            ref = await self.reactor.upload_file(image, name=name, mime_type=mime)
            reply = await self.reactor.send_command("set_image", {"image": ref})
            self.send_json({"type": "command_reply", "data": {"command": "set_image", "reply": reply}})
        except ReactorError as e:
            log.warning("set_image refused: %s", e)
            self.send_json({"type": "command_error", "data": {"command": "set_image", "reason": str(e)}})

    # ---- lifecycle ----

    async def run(self) -> None:
        self.reactor = Reactor(model_name=self.model, api_key=self.api_key)
        self.reactor.track("main_video").on_frame(self.on_video)
        self.reactor.track("main_audio").on_frame(self.on_audio)
        self.reactor.on("message", self.on_message)
        self.reactor.on("status_changed", self.on_status)
        self.reactor.on("error", self.on_error)

        self.send_json({"type": "bridge_status", "data": {"status": "connecting"}})
        connect_task = asyncio.create_task(self.reactor.connect())
        connect_task.add_done_callback(self._on_connect_done)
        # Commands run one at a time, in the order Unity sent them
        # (e.g. set_image before set_prompt before start).
        commands: asyncio.Queue = asyncio.Queue()
        worker = asyncio.create_task(self._command_worker(commands))
        try:
            while True:
                header = await self.reader.readexactly(HEADER.size)
                msg_type, length = HEADER.unpack(header)
                if length > MAX_INBOUND:
                    raise ValueError(f"message too large: {length} bytes")
                payload = await self.reader.readexactly(length)
                if msg_type == MSG_JSON:
                    msg = json.loads(payload.decode("utf-8"))
                    log.info("Unity -> %s", msg.get("command"))
                    await commands.put(self.run_command(msg["command"], msg.get("data") or {}))
                elif msg_type == MSG_IMAGE:
                    log.info("Unity -> set_image (%d bytes)", length)
                    await commands.put(self.set_image(payload))
        except (asyncio.IncompleteReadError, ConnectionError):
            log.info("Unity disconnected")
        finally:
            self.closed = True
            worker.cancel()
            if not connect_task.done():
                connect_task.cancel()
            try:
                await self.reactor.disconnect()
            except Exception:
                log.exception("disconnect failed")
            self.writer.close()
            log.info("Reactor session closed")

    def _on_connect_done(self, task: asyncio.Task) -> None:
        if task.cancelled() or task.exception() is None:
            return
        err = task.exception()
        log.error("Reactor connect failed: %s", err)
        self.send_json({"type": "bridge_error", "data": {"reason": f"connect failed: {err}"}})
        self.writer.close()  # Unity sees the drop and retries

    async def _command_worker(self, commands: asyncio.Queue) -> None:
        while True:
            coro = await commands.get()
            await coro


async def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=7777)
    parser.add_argument("--model", default="reactor/visko-orbis-stable",
                        help="reactor/visko-orbis-stable or reactor/visko-orbis-dynamic")
    args = parser.parse_args()
    api_key = load_api_key()

    busy = asyncio.Lock()

    async def handle(reader, writer):
        if busy.locked():
            log.warning("Rejecting second Unity client; only one session at a time")
            writer.close()
            return
        async with busy:
            log.info("Unity connected from %s", writer.get_extra_info("peername"))
            await UnitySession(reader, writer, args.model, api_key).run()

    server = await asyncio.start_server(handle, args.host, args.port)
    log.info("Visko bridge listening on %s:%d (model %s). Press Play in Unity.", args.host, args.port, args.model)
    async with server:
        await server.serve_forever()


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
