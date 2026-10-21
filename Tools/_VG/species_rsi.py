#!/usr/bin/env python3
"""
Builds the per-limb species RSI SS14's humanoid sprites want (head_m, torso_m, l_arm, ...) from a vgstation13 species
.dmi (r_human.dmi, r_lizard.dmi, ...), which has the same limbs as states.

    species_rsi.py ../vgstation13/icons/mob/human_races/r_human.dmi Resources/Textures/_VG/mob/human_races/human.rsi \
        --neutral human

vg13's torso and groin are separate states; SS14 has no groin layer, so the groin is drawn onto the torso. The
sprites are also divided by the tint SS14 will multiply them by (--neutral human, or an #rrggbb colour, or "mean" for
the average colour of the torso, or "none"), so that a mob with the default skin looks like it does in vg13.
"""

import argparse
import colorsys
import json
import os
import shutil
import subprocess
import sys
import tempfile

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))

LIMBS = ["l_arm", "r_arm", "l_hand", "r_hand", "l_leg", "r_leg", "l_foot", "r_foot"]
# SS14's default HumanToned skin (SkinColor.ValidHumanSkinTone).
HUMAN_TONE = colorsys.hsv_to_rgb(0.07, 0.2, 1.0)


def mean_color(img: Image.Image):
    px = [p for p in img.getdata() if p[3] > 200]
    if not px:
        return (1.0, 1.0, 1.0)
    return tuple(sum(p[i] for p in px) / len(px) / 255 for i in range(3))


def strip_eye_placeholders(img: Image.Image) -> Image.Image:
    """vg13 heads have bright green pixels where the eyes go (the eyes are drawn over them). SS14 draws its own eyes
    layer, so paint them over with the head's skin colour."""
    img = img.convert("RGBA")
    px = img.load()
    skin = {}
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a > 200 and not (g > r + 30 and g > b + 30):
                skin[(r, g, b)] = skin.get((r, g, b), 0) + 1
    if not skin:
        return img
    fill = max(skin, key=skin.get)
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a > 200 and g > r + 30 and g > b + 30:
                px[x, y] = (*fill, a)
    return img


def neutralize(img: Image.Image, tint) -> Image.Image:
    img = img.convert("RGBA")
    out = img.copy()
    src = img.load()
    dst = out.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = src[x, y]
            if a == 0:
                continue
            dst[x, y] = (
                min(255, round(r / max(tint[0], 0.05))),
                min(255, round(g / max(tint[1], 0.05))),
                min(255, round(b / max(tint[2], 0.05))),
                a,
            )
    return out


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("dmi")
    parser.add_argument("out")
    parser.add_argument("--neutral", default="none", help="human, mean, none or #rrggbb")
    args = parser.parse_args()

    with tempfile.TemporaryDirectory() as tmp:
        raw = os.path.join(tmp, "raw.rsi")
        subprocess.check_call([sys.executable, os.path.join(HERE, "dmi2rsi.py"), args.dmi, raw], stdout=subprocess.DEVNULL)
        meta = json.load(open(os.path.join(raw, "meta.json")))
        states = {s["name"] for s in meta["states"]}

        def load(name):
            return Image.open(os.path.join(raw, name + ".png")).convert("RGBA")

        sheets = {}
        for sex in ("m", "f"):
            if f"torso_{sex}" not in states:
                continue
            torso = load(f"torso_{sex}")
            if f"groin_{sex}" in states:
                torso = Image.alpha_composite(torso, load(f"groin_{sex}"))
            sheets[f"torso_{sex}"] = torso
            sheets[f"head_{sex}"] = strip_eye_placeholders(load(f"head_{sex}"))
        for limb in LIMBS:
            if limb in states:
                sheets[limb] = load(limb)

        if args.neutral == "human":
            tint = HUMAN_TONE
        elif args.neutral == "mean":
            tint = mean_color(next(iter(v for k, v in sheets.items() if k.startswith("torso"))))
        elif args.neutral.startswith("#"):
            tint = tuple(int(args.neutral[i:i + 2], 16) / 255 for i in (1, 3, 5))
        else:
            tint = None

        os.makedirs(args.out, exist_ok=True)
        out_states = [{"name": "full"}]
        for name, img in sorted(sheets.items()):
            if tint is not None:
                img = neutralize(img, tint)
            img.save(os.path.join(args.out, name + ".png"))
            out_states.append({"name": name, "directions": 4})

        # The icon of the whole body: the south frame of every part, back to front.
        full = Image.new("RGBA", (32, 32))
        order = ["torso_m", "head_m", "l_leg", "r_leg", "l_foot", "r_foot", "l_arm", "r_arm", "l_hand", "r_hand"]
        for name in order:
            if name in sheets:
                img = neutralize(sheets[name], tint) if tint is not None else sheets[name]
                full = Image.alpha_composite(full, img.crop((0, 0, 32, 32)))
        full.save(os.path.join(args.out, "full.png"))

        out_meta = {k: meta[k] for k in ("version", "license", "copyright", "size")}
        out_meta["states"] = out_states
        with open(os.path.join(args.out, "meta.json"), "w") as f:
            json.dump(out_meta, f, indent=2)
            f.write("\n")


if __name__ == "__main__":
    main()
