#!/usr/bin/env python3
"""
Converts BYOND .dmi icon files into RSIs for vgstation14.

    dmi2rsi.py icons/obj/items.dmi Resources/Textures/_VG/obj/items.rsi
    dmi2rsi.py --batch ../vgstation13/icons Resources/Textures/_VG
    dmi2rsi.py icons/obj/items.dmi out.rsi --states gift gift-large

DMI notes:
  - Frames are stored frame-major ([frame][dir]); RSIs want dir-major ([dir][frame]).
  - BYOND and RSI share the same direction order (S, N, E, W, SE, SW, NE, NW).
  - Delays are in ticks (1/10 s); RSI delays are in seconds.
  - rewind=1 is baked in by appending the reversed frames; loop=N is lost (RSIs always loop).
  - Movement states (movement=1) get a "_move" suffix since RSIs need unique state names.
"""

import argparse
import json
import math
import os
import re
import subprocess
import sys
from typing import Dict, List, Optional

from PIL import Image

LICENSE = "CC-BY-SA-3.0"
REPO_URL = "https://github.com/vgstation-coders/vgstation13"
INVALID_NAME_CHARS = re.compile(r'[\\/:*?"<>|\x00-\x1f]')
WINDOWS_RESERVED = {"CON", "PRN", "AUX", "NUL"} | {f"COM{i}" for i in range(1, 10)} | {f"LPT{i}" for i in range(1, 10)}


class DmiState:
    def __init__(self, name: str):
        self.name = name
        self.dirs = 1
        self.frames = 1
        self.delays: Optional[List[float]] = None
        self.rewind = False
        self.movement = False
        self.loop = 0


def parse_description(desc: str):
    width = height = 32
    states: List[DmiState] = []
    current: Optional[DmiState] = None

    for line in desc.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        key, _, value = (part.strip() for part in line.partition("="))

        if key == "state":
            current = DmiState(parse_string(value))
            states.append(current)
        elif current is None:
            if key == "width":
                width = int(value)
            elif key == "height":
                height = int(value)
        elif key == "dirs":
            current.dirs = int(value)
        elif key == "frames":
            current.frames = int(value)
        elif key == "delay":
            current.delays = [float(d) for d in value.split(",")]
        elif key == "rewind":
            current.rewind = value == "1"
        elif key == "movement":
            current.movement = value == "1"
        elif key == "loop":
            current.loop = int(value)

    return width, height, states


def parse_string(value: str) -> str:
    # DMI state names are DM string literals.
    if len(value) >= 2 and value[0] == '"' and value[-1] == '"':
        value = value[1:-1]
    return re.sub(r'\\(.)', r'\1', value)


def rsi_name(state: DmiState, used: Dict[str, str]) -> str:
    # State names become filenames: no leading/trailing dots or spaces, no Windows device names.
    name = INVALID_NAME_CHARS.sub("_", state.name)
    name = re.sub(r"^[. ]+|[. ]+$", lambda m: "_" * len(m.group()), name) or "_blank"
    if name.split(".")[0].upper() in WINDOWS_RESERVED:
        name = "_" + name
    if state.movement:
        name += "_move"

    # Compare case-insensitively so RSIs still work on Windows/macOS filesystems.
    candidate, i = name, 2
    while candidate.lower() in used:
        candidate = f"{name}_{i}"
        i += 1
    used[candidate.lower()] = candidate
    return candidate


def copyright_for(dmi_path: str) -> str:
    try:
        root = subprocess.run(["git", "-C", os.path.dirname(os.path.abspath(dmi_path)), "rev-parse", "--show-toplevel"],
                              capture_output=True, text=True, check=True).stdout.strip()
        commit = subprocess.run(["git", "-C", root, "rev-parse", "HEAD"],
                                capture_output=True, text=True, check=True).stdout.strip()
        rel = os.path.relpath(os.path.abspath(dmi_path), root).replace(os.sep, "/")
        return f"Taken from vgstation13 at {REPO_URL}/blob/{commit}/{rel}"
    except (subprocess.CalledProcessError, FileNotFoundError):
        return f"Taken from vgstation13 ({REPO_URL})"


def convert(dmi_path: str, out_path: str, only_states: Optional[List[str]] = None, quiet: bool = False) -> int:
    """Returns the number of problems found (states skipped or renamed)."""
    sheet = Image.open(dmi_path)
    desc = sheet.info.get("Description")
    if desc is None:
        print(f"{dmi_path}: not a DMI (no Description metadata)", file=sys.stderr)
        return 1

    sheet = sheet.convert("RGBA")
    width, height, states = parse_description(desc)
    columns = sheet.width // width
    problems = 0

    os.makedirs(out_path, exist_ok=True)
    meta_states = []
    used: Dict[str, str] = {}
    index = 0

    for state in states:
        count = state.dirs * state.frames
        start = index
        index += count

        if only_states is not None and state.name not in only_states:
            continue
        if state.dirs not in (1, 4, 8):
            print(f"{dmi_path}: state '{state.name}' has {state.dirs} dirs, skipping", file=sys.stderr)
            problems += 1
            continue

        def frame(i):
            x, y = (i % columns) * width, (i // columns) * height
            return sheet.crop((x, y, x + width, y + height))

        frame_order = list(range(state.frames))
        delays = state.delays or [1.0] * state.frames
        delays = (delays + [delays[-1]] * state.frames)[:state.frames]
        if state.rewind and state.frames > 2:
            frame_order += frame_order[-2:0:-1]
            delays += delays[-2:0:-1]

        # Dir-major output: all frames of dir 0, then dir 1, ...
        images = [frame(start + f * state.dirs + d) for d in range(state.dirs) for f in frame_order]

        cols = math.ceil(math.sqrt(len(images)))
        rows = math.ceil(len(images) / cols)
        out = Image.new("RGBA", (cols * width, rows * height))
        for i, img in enumerate(images):
            out.paste(img, ((i % cols) * width, (i // cols) * height))

        name = rsi_name(state, used)
        if name != state.name:
            problems += 1
            if not quiet:
                print(f"{dmi_path}: state '{state.name}' renamed to '{name}'", file=sys.stderr)
        out.save(os.path.join(out_path, name + ".png"), optimize=True)

        meta = {"name": name}
        if state.dirs != 1:
            meta["directions"] = state.dirs
        if len(frame_order) > 1:
            meta["delays"] = [[round(d / 10, 3) for d in delays] for _ in range(state.dirs)]
        meta_states.append(meta)

    meta = {
        "version": 1,
        "license": LICENSE,
        "copyright": copyright_for(dmi_path),
        "size": {"x": width, "y": height},
        "states": meta_states,
    }
    with open(os.path.join(out_path, "meta.json"), "w", newline="\n") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")

    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("input", help=".dmi file, or a directory with --batch")
    parser.add_argument("output", help=".rsi directory, or an output root with --batch")
    parser.add_argument("--batch", action="store_true", help="Convert every .dmi under input, mirroring the folder layout")
    parser.add_argument("--states", nargs="+", help="Only convert these DMI states")
    parser.add_argument("--quiet", action="store_true", help="Don't print renamed states")
    args = parser.parse_args()

    if not args.batch:
        return 1 if convert(args.input, args.output, args.states, args.quiet) else 0

    total = failed = problems = 0
    for root, _, files in os.walk(args.input):
        for file in sorted(files):
            if not file.endswith(".dmi"):
                continue
            total += 1
            src = os.path.join(root, file)
            dst = os.path.join(args.output, os.path.relpath(src, args.input))[:-4] + ".rsi"
            try:
                problems += convert(src, dst, args.states, args.quiet)
            except Exception as e:
                failed += 1
                print(f"{src}: failed: {e}", file=sys.stderr)

    print(f"Converted {total - failed}/{total} DMIs ({problems} renamed/skipped states)")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
