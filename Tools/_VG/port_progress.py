#!/usr/bin/env python3
"""
Measures how much of vgstation13's content has a counterpart in vgstation14.

    port_progress.py [--vg13 ../vgstation13] [--json out.json] [--list CATEGORY] [--limit N]

For each content category (items, machinery, reagents, jobs, ...) it counts vg13's concrete types (leaves of the DM type
tree, or types with an `id` for reagents and reactions) and sorts each into one of three states:

  ported    a prototype defined under Resources/Prototypes/_VG (or a vgClothing/vgReagent entry) matches it
  in SS14   an upstream prototype of the matching kind has the same id or name, but no vg13 version yet
  missing   neither

Matching is heuristic: the last path segment (or the `id`/`name` var) is lowercased and stripped to letters and digits,
then compared with prototype ids and, for entities, prototype names. It will miss renamed things and can match
unrelated ones that share a name, so read the numbers as an estimate. `--list CATEGORY` prints the unmatched types.
"""
import argparse
import json
import re
import sys
from pathlib import Path

import yaml

REPO = Path(__file__).resolve().parents[2]

# (label, DM path prefix, SS14 prototype types it can match, how to count)
CATEGORIES = [
    ("Reagents", "/datum/reagent", {"reagent"}, "id"),
    ("Chemical reactions", "/datum/chemical_reaction", {"reaction"}, "id"),
    ("Clothing", "/obj/item/clothing", {"entity"}, "leaf"),
    ("Guns", "/obj/item/weapon/gun", {"entity"}, "leaf"),
    ("Other items", "/obj/item", {"entity"}, "leaf"),
    ("Machinery", "/obj/machinery", {"entity"}, "leaf"),
    ("Structures", "/obj/structure", {"entity"}, "leaf"),
    ("Mechs", "/obj/mecha", {"entity"}, "leaf"),
    ("Mobs", "/mob/living", {"entity"}, "leaf"),
    ("Species", "/datum/species", {"species"}, "leaf"),
    ("Jobs", "/datum/job", {"job"}, "leaf"),
    ("Antag roles", "/datum/role", {"antag"}, "leaf"),
    ("Dynamic rulesets", "/datum/dynamic_ruleset", {"gameRule"}, "leaf"),
    ("Research designs", "/datum/design", {"latheRecipe"}, "leaf"),
    ("Food recipes", "/datum/recipe", {"microwaveMealRecipe"}, "leaf"),
    ("Spells", "/spell", {"entity", "instantAction"}, "leaf"),
    ("Disease effects", "/datum/disease2/effect", {"entity"}, "leaf"),
]
# Kinds of _VG prototype that count as "ported" for a given SS14 kind.
VG_KINDS = {"entity": {"entity", "vgClothing", "stack"}, "reagent": {"reagent", "vgReagent"},
            "reaction": {"reaction"}, "species": {"species", "vgSpecies"}}
SKIP_SEGMENTS = {"var", "proc", "verb", "static", "global", "const", "tmp", "datum_ref"}
DECL = re.compile(r"^/?[A-Za-z_]\w*(?:/[A-Za-z_]\w*)*$")
ASSIGN = re.compile(r'^(name|id)\s*=\s*(?:"([^"]*)"|([A-Z][A-Z0-9_]*))')


def norm(s):
    return re.sub(r"[^a-z0-9]", "", s.lower())


def parse_dm(root):
    """Returns {path: {"name": str, "id": str}} for every type declared in the tree."""
    types = {}
    for f in sorted(root.rglob("*.dm")):
        try:
            text = f.read_text(errors="replace")
        except OSError:
            continue
        stack = []  # (indent, path)
        in_block = False
        for raw in text.splitlines():
            line = raw.rstrip()
            if in_block:
                if "*/" in line:
                    in_block = False
                continue
            if line.lstrip().startswith("/*"):
                in_block = "*/" not in line
                continue
            code = line.split("//")[0].rstrip()
            s = code.strip()
            if not s or s.startswith("#"):
                continue
            indent = len(code) - len(code.lstrip("\t "))
            indent = code[:indent].count("\t") + code[:indent].count(" ") // 4
            m = ASSIGN.match(s)
            if m:
                if stack and stack[-1][0] + 1 == indent:
                    types[stack[-1][1]][m.group(1)] = m.group(2) or m.group(3)
                continue
            if DECL.match(s) and not (set(s.split("/")) & SKIP_SEGMENTS):
                while stack and stack[-1][0] >= indent:
                    stack.pop()
                path = s if s.startswith("/") else ((stack[-1][1] if stack else "") + "/" + s)
                types.setdefault(path, {})
                stack.append((indent, path))
            else:
                while stack and stack[-1][0] >= indent:
                    stack.pop()
    return types


class Loader(yaml.SafeLoader):
    pass


Loader.add_multi_constructor("!", lambda loader, suffix, node: None)


def load_prototypes():
    """Returns (upstream, vg): {kind: {normalized id/name: id}}."""
    out = [{}, {}]
    for path in (REPO / "Resources/Prototypes").rglob("*.yml"):
        vg = "_VG" in path.parts
        try:
            docs = yaml.load(path.read_text(), Loader=Loader)
        except Exception:
            continue
        for p in docs if isinstance(docs, list) else []:
            if not isinstance(p, dict) or "id" not in p:
                continue
            kind = p.get("type")
            idx = out[1 if vg else 0].setdefault(kind, {})
            idx.setdefault(norm(str(p["id"])), str(p["id"]))
            if vg and re.match(r"Vg[A-Z]", str(p["id"])):  # VgInaprovaline is vg13's inaprovaline
                idx.setdefault(norm(str(p["id"])[2:]), str(p["id"]))
            if kind == "entity" and isinstance(p.get("name"), str):
                idx.setdefault(norm(p["name"]), str(p["id"]))
    return out


def candidates(path, attrs, mode):
    names = []
    if mode == "id" and attrs.get("id"):
        names.append(attrs["id"])
    names.append(path.rsplit("/", 1)[-1])
    if attrs.get("name"):
        names.append(attrs["name"])
    return [n for n in dict.fromkeys(norm(x) for x in names) if n]


def classify(cands, kinds, upstream, vg):
    vg_kinds = set().union(*(VG_KINDS.get(k, {k}) for k in kinds))
    if any(c in vg.get(k, {}) for c in cands for k in vg_kinds):
        return "ported"
    if any(c in upstream.get(k, {}) for c in cands for k in kinds):
        return "in SS14"
    return "missing"


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--vg13", default=str(REPO.parent / "vgstation13"))
    ap.add_argument("--json")
    ap.add_argument("--list", metavar="CATEGORY", help="print the unmatched vg13 types of a category")
    ap.add_argument("--limit", type=int, default=60)
    args = ap.parse_args()

    vg13 = Path(args.vg13)
    types = parse_dm(vg13 / "code")
    upstream, vg = load_prototypes()
    paths = sorted(types)

    prefix_children = set()
    for p in paths:
        parent = p.rsplit("/", 1)[0]
        prefix_children.add(parent)

    claimed = set()
    rows, unmatched = [], {}
    for label, prefix, kinds, mode in CATEGORIES:
        counts = {"ported": 0, "in SS14": 0, "missing": 0}
        miss = []
        for p in paths:
            if p in claimed or not (p == prefix or p.startswith(prefix + "/")) or p == prefix:
                continue
            attrs = types[p]
            if mode == "id":
                if not attrs.get("id") and p in prefix_children:
                    continue
            elif p in prefix_children:
                continue
            claimed.add(p)
            state = classify(candidates(p, attrs, mode), kinds, upstream, vg)
            counts[state] += 1
            if state == "missing":
                miss.append(p)
        unmatched[label] = miss
        rows.append((label, counts))

    # Maps: vg13's station and away-mission levels vs. what dmm2map has converted.
    vg_maps = sorted({norm(p.stem) for p in (vg13 / "maps").glob("*.dmm")})
    done = {norm(p.stem) for p in (REPO / "Resources/Maps/_VG").glob("*.yml")}
    ported_maps = sum(1 for m in vg_maps if m in done)
    rows.append(("Station maps (converted, not necessarily playable)",
                 {"ported": ported_maps, "in SS14": 0, "missing": len(vg_maps) - ported_maps}))

    w = max(len(r[0]) for r in rows)
    print(f"{'Category':<{w}}  {'vg13':>6}  {'ported':>7}  {'in SS14':>8}  {'missing':>8}  {'ported %':>8}  {'+SS14 %':>8}")
    tot = {"ported": 0, "in SS14": 0, "missing": 0}
    for label, c in rows:
        n = sum(c.values())
        for k in tot:
            tot[k] += c[k]
        pct = lambda x: f"{100 * x / n:.1f}" if n else "-"
        print(f"{label:<{w}}  {n:>6}  {c['ported']:>7}  {c['in SS14']:>8}  {c['missing']:>8}  "
              f"{pct(c['ported']):>8}  {pct(c['ported'] + c['in SS14']):>8}")
    n = sum(tot.values())
    print(f"{'Total':<{w}}  {n:>6}  {tot['ported']:>7}  {tot['in SS14']:>8}  {tot['missing']:>8}  "
          f"{100 * tot['ported'] / n:>8.1f}  {100 * (tot['ported'] + tot['in SS14']) / n:>8.1f}")
    print(f"\n{len(types)} vg13 types parsed; {sum(len(v) for v in upstream.values())} upstream and "
          f"{sum(len(v) for v in vg.values())} _VG prototype keys indexed.")

    if args.json:
        Path(args.json).write_text(json.dumps({l: c for l, c in rows}, indent=2))
    if args.list:
        for label, miss in unmatched.items():
            if label.lower().startswith(args.list.lower()):
                print(f"\nUnmatched in {label} ({len(miss)}):")
                for p in miss[: args.limit]:
                    print("  " + p)
                return
        sys.exit(f"no category starting with {args.list!r}")


if __name__ == "__main__":
    main()
