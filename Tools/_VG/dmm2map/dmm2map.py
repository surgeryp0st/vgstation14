#!/usr/bin/env python3
"""Converts a vgstation13 .dmm map level into an SS14 map file.

    python3 Tools/_VG/dmm2map/dmm2map.py ../vgstation13/maps/bagelstation.dmm Resources/Maps/_VG/bagel.yml [--z 1]

Types are translated through mapping.yml (longest path prefix wins). Anything without a rule is dropped and counted in
the report printed at the end (and written next to the map as <name>.unmapped.txt), so mapping.yml can grow by
coverage. `--check` validates every prototype in mapping.yml against Resources/Prototypes and exits.
"""
import argparse
import base64
import collections
import glob
import math
import os
import struct
import sys

import yaml

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dmm  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
CHUNK = 16

# BYOND dir -> SS14 rotation (radians, counterclockwise from facing south).
DIR_ROT = {1: math.pi, 2: 0.0, 4: math.pi / 2, 8: 3 * math.pi / 2}


class _Loader(yaml.SafeLoader):
    pass


_Loader.add_multi_constructor("!", lambda loader, suffix, node: None)


def load_prototypes():
    entities, tiles = set(), set()
    for f in glob.glob(os.path.join(ROOT, "Resources", "Prototypes", "**", "*.yml"), recursive=True):
        try:
            data = yaml.load(open(f, encoding="utf-8"), Loader=_Loader)
        except yaml.YAMLError:
            continue
        for p in data or []:
            if not isinstance(p, dict):
                continue
            if p.get("type") == "entity" and not p.get("abstract"):
                entities.add(p["id"])
            elif p.get("type") == "tile":
                tiles.add(p["id"])
    return entities, tiles


class Rules:
    def __init__(self, path):
        data = yaml.safe_load(open(path, encoding="utf-8"))
        self.tiles = data["tiles"]
        self.atoms = data["atoms"]

    @staticmethod
    def _find(table, path):
        best = None
        for prefix in table:
            if path == prefix or path.startswith(prefix + "/"):
                if best is None or len(prefix) > len(best):
                    best = prefix
        return table[best] if best else None

    def tile(self, path):
        return self._find(self.tiles, path)

    def atom(self, path):
        return self._find(self.atoms, path)

    def check(self):
        entities, tiles = load_prototypes()
        bad = []
        for table in (self.tiles, self.atoms):
            for k, v in table.items():
                if "disposal" in v and v["disposal"] not in ("segment", "junction", "sort", "trunk"):
                    bad.append(f"{k}: unknown disposal kind {v['disposal']}")
                if "pipe" in v and v["pipe"] not in ("simple", "manifold", "manifold4w", "unary"):
                    bad.append(f"{k}: unknown pipe kind {v['pipe']}")
                if "proto" in v and v["proto"] not in entities:
                    bad.append(f"{k}: unknown entity {v['proto']}")
                if "tile" in v and v["tile"] not in tiles:
                    bad.append(f"{k}: unknown tile {v['tile']}")
        return bad


def dir_of(atom):
    try:
        return int(atom.vars.get("dir", "2"))
    except ValueError:
        return 2


# SS14 PipeDirection bits, and the BYOND dir bits (1 north, 2 south, 4 east, 8 west) they come from.
P_N, P_S, P_W, P_E = 1, 2, 4, 8
BYOND_TO_PIPE = {1: P_N, 2: P_S, 4: P_E, 8: P_W}
# One counterclockwise quarter turn (what a positive entity rotation does): south -> east -> north -> west -> south.
TURN = {P_S: P_E, P_E: P_N, P_N: P_W, P_W: P_S}
# Open sides of each pipe prototype at rotation 0, which faces south.
PIPE_SHAPES = {
    1: ("GasPipeHalf", P_S),
    "straight": ("GasPipeStraight", P_N | P_S),
    "bend": ("GasPipeBend", P_S | P_W),
    3: ("GasPipeTJunction", P_S | P_W | P_E),
    4: ("GasPipeFourway", P_N | P_S | P_W | P_E),
}


def pipe_mask(kind, byond_dir):
    """The sides a vg13 pipe is open on. Simple pipes face their dir (or are bent by it), manifolds lack it."""
    bits = 0
    for b, p in BYOND_TO_PIPE.items():
        if byond_dir & b:
            bits |= p
    if kind == "simple":
        return bits if bin(bits).count("1") == 2 else (P_E | P_W if bits & (P_E | P_W) else P_N | P_S)
    if kind == "manifold":
        return (P_N | P_S | P_W | P_E) & ~(bits & -bits if bits else 0)
    if kind == "manifold4w":
        return P_N | P_S | P_W | P_E
    return bits & -bits if bits else P_S  # unary (caps): the one side it opens on


def pipe_proto(mask):
    """(prototype, rotation in radians) for a pipe open on `mask`."""
    n = bin(mask).count("1")
    key = ("straight" if mask in (P_N | P_S, P_E | P_W) else "bend") if n == 2 else n
    proto, base = PIPE_SHAPES[key]
    for k in range(4):
        if base == mask:
            return proto, k * math.pi / 2
        base = sum(TURN[b] for b in (P_N, P_S, P_W, P_E) if base & b)
    raise ValueError(mask)


def disposal_proto(kind, atom):
    """SS14 disposal prototype for a vg13 disposal pipe. Both games name the sides relative to the dir the same way: a bend
    joins dir and the side clockwise of it, junctions are dir plus two more (j1: clockwise and back, j2: counterclockwise and
    back, y: both sides), and a sort junction is a junction that sends tagged things out of its side."""
    icon = atom.vars.get("icon_state", "").strip('"')
    if kind == "segment":
        return "DisposalBend" if icon == "pipe-c" else "DisposalPipe"
    if kind == "junction":
        return {"pipe-j2": "DisposalJunctionFlipped", "pipe-y": "DisposalYJunction"}.get(icon, "DisposalJunction")
    if kind == "sort":
        return "DisposalRouterFlipped" if icon == "pipe-j2s" or atom.path.endswith("/mirrored") else "DisposalRouter"
    return "DisposalTrunk"


def var_flag(atom, name):
    return atom.vars.get(name, "0").strip() in ("1", "TRUE")


def device_state(rule, atom):
    """Extra components setting a pump's or valve's initial state from the vg13 vars (vg13 starts them off/closed)."""
    if "enabled" in rule:
        return ["    - type: " + rule["enabled"], f"      enabled: {str(var_flag(atom, 'on')).lower()}"]
    if rule.get("valve"):
        is_open = atom.path.endswith("/open") or var_flag(atom, "openDuringInit") or var_flag(atom, "open")
        return ["    - type: GasValve", f"      open: {str(is_open).lower()}"]
    return None


def atmosphere_lines(tiles, size=4):
    """Standard air (one unique mix) on every tile that has a floor."""
    flags = collections.defaultdict(int)
    for (x, y), t in tiles.items():
        if t != 0:
            flags[(x // size, y // size)] |= 1 << (x % size + (y % size) * size)
    out = ["      data:", "        tiles:"]
    for (cx, cy), f in sorted(flags.items()):
        out += [f"          {cx},{cy}:", f"            0: {f}"]
    moles = [21.824879, 82.10312] + [0] * 11
    out += ["        uniqueMixes:", "        - volume: 2500", "          temperature: 293.15", "          moles:"]
    out += [f"          - {m}" for m in moles]
    out.append(f"        chunkSize: {size}")
    return out


def write_map(dst, tilemap, tiles, placed, station=None, inherent_gravity=False):
    """Writes an SS14 map: tilemap {name: id}, tiles {(x, y): id}, placed {proto: [(x, y, rot)]}.

    inherent_gravity gives the grid gravity without a gravity generator."""
    out = ["meta:", "  format: 6", "  postmapinit: false", "tilemap:"]
    for name, i in sorted(tilemap.items(), key=lambda kv: kv[1]):
        out.append(f"  {i}: {name}")

    grid_uid, map_uid = 1, 2
    out += ["entities:", '- proto: ""', "  entities:", f"  - uid: {grid_uid}", "    components:", "    - type: MetaData",
            f"      name: {os.path.splitext(os.path.basename(dst))[0]}",
            "    - type: Transform", f"      parent: {map_uid}", "    - type: MapGrid", "      chunks:"]
    chunks = collections.defaultdict(dict)
    for (gx, gy), t in tiles.items():
        chunks[(gx // CHUNK, gy // CHUNK)][(gx % CHUNK, gy % CHUNK)] = t
    for (cx, cy), tl in sorted(chunks.items()):
        raw = bytearray()
        # The engine reads a chunk row by row: y is the outer loop.
        for ly in range(CHUNK):
            for lx in range(CHUNK):
                raw += struct.pack("<iBB", tl.get((lx, ly), 0), 0, 0)
        out += [f"        {cx},{cy}:", f"          ind: {cx},{cy}", f"          tiles: {base64.b64encode(bytes(raw)).decode()}",
                "          version: 6"]
    out += ["    - type: Broadphase", "    - type: Physics", "      bodyStatus: InAir", "      angularDamping: 0.05",
            "      linearDamping: 0.05", "      fixedRotation: False", "      bodyType: Dynamic", "    - type: Fixtures",
            "      fixtures: {}", "    - type: Gravity", "      enabled: true", *(["      inherent: true"] if inherent_gravity else []), "    - type: DecalGrid", "    - type: OccluderTree",
            "    - type: Shuttle", "    - type: RadiationGridResistance", "    - type: GridAtmosphere", "      version: 2", *atmosphere_lines(tiles),
            "    - type: GasTileOverlay", "    - type: SpreaderGrid", "    - type: GridPathfinding", "    - type: NavMap",
            *(["    - type: BecomesStation", f"      id: {station}"] if station else []),
            f"  - uid: {map_uid}", "    components:", "    - type: MetaData", "    - type: Transform", "    - type: Map",
            "      mapPaused: True", "    - type: PhysicsMap", "    - type: GridTree", "    - type: MovedGrids",
            "    - type: Broadphase", "    - type: OccluderTree", "    - type: LoadedMap"]

    uid = 3
    for proto in sorted(placed):
        if not placed[proto]:
            continue
        out += [f"- proto: {proto}", "  entities:"]
        for gx, gy, rot, *extra in placed[proto]:
            out += [f"  - uid: {uid}", "    components:", "    - type: Transform"]
            if rot:
                out.append(f"      rot: {rot} rad")
            out += [f"      pos: {gx + 0.5},{gy + 0.5}", f"      parent: {grid_uid}"]
            if extra:
                out += extra[0]
            uid += 1

    os.makedirs(os.path.dirname(os.path.abspath(dst)), exist_ok=True)
    with open(dst, "w", encoding="utf-8") as f:
        f.write("\n".join(out) + "\n")
    return uid - 3



CABLE_STEP = {1: (0, 1), 2: (0, -1), 4: (1, 0), 8: (-1, 0), 5: (1, 1), 6: (1, -1), 9: (-1, 1), 10: (-1, -1)}


def cable_neighbours(cable, cable_dirs):
    """The cables vg13 joins to `cable` ((x, y, index)): through each end, a cable on the next tile with an end pointing
    back, one on the same tile with the same end, and for diagonal ends the two tiles around the corner (get_connections)."""
    opp = lambda d: ((d & 1) << 1) | ((d & 2) >> 1) | ((d & 4) << 1) | ((d & 8) >> 1)
    x, y, i = cable
    ends = cable_dirs[(x, y)][i]

    def on(tile, end, skip=None):
        for j, pair in enumerate(cable_dirs.get(tile, [])):
            if end in pair and (tile[0], tile[1], j) != skip:
                yield (tile[0], tile[1], j)

    for d in ends:
        if d in CABLE_STEP:  # up/down (multi-z) ends have no neighbour on this level
            dx, dy = CABLE_STEP[d]
            yield from on((x + dx, y + dy), opp(d))
            if d & (d - 1):  # diagonal: the two tiles around the corner
                for part in (d & 3, d & 12):
                    px, py = CABLE_STEP[part]
                    yield from on((x + px, y + py), d ^ (3 if part == d & 3 else 12))
        yield from on((x, y), d, skip=cable)  # same tile, same end (end 0 joins the node cables)


def wire_smes(smes_tiles, terminals, cables, cable_dirs, apc_tiles, placed):
    """Fits vg13's SMES wiring onto SS14's voltage tiers.

    vg13: one network on each side of an SMES (the terminal side charges it, the cable under it is the output). SS14's
    SMES and generators are HV and APCs are MV. For each SMES the side with more APCs is the station: it stays MV and gets
    a substation next to the SMES (or next to its terminal when the terminal side is the station), and the other side
    becomes HV. Returns (HV cables as (x, y, index), substation tiles, a summary).
    """
    step = {1: (0, 1), 2: (0, -1), 4: (1, 0), 8: (-1, 0)}  # BYOND dir -> grid step (y grows north)

    def flood(starts):
        comp, stack = set(starts), list(starts)
        while stack:
            cur = stack.pop()
            for nb in cable_neighbours(cur, cable_dirs):
                if nb not in comp and (nb[0], nb[1]) not in smes_tiles:
                    comp.add(nb)
                    stack.append(nb)
        return comp

    def at(tile):
        return [(tile[0], tile[1], i) for i in range(len(cable_dirs.get(tile, [])))]

    def apcs_in(comp):
        return len({(x, y) for x, y, _ in comp} & apc_tiles)

    substations, hv, notes = set(), set(), []
    paired = looped = 0
    for sx, sy in sorted(smes_tiles):
        term = next(((tx, ty, d) for tx, ty, d in terminals
                     if (tx + step.get(d, (0, 0))[0], ty + step.get(d, (0, 0))[1]) == (sx, sy)), None)
        if term is None:
            continue
        paired += 1
        placed["SMESBasic"].append((sx, sy, 0.0))
        placed["CableTerminal"].append((term[0], term[1], DIR_ROT.get(term[2], 0.0)))
        tt = (term[0], term[1])
        t_side = flood(at(tt))
        out_starts = {nb for c in at((sx, sy)) for nb in cable_neighbours(c, cable_dirs)
                      if (nb[0], nb[1]) not in smes_tiles and (nb[0], nb[1]) != tt}
        o_side = flood(sorted(out_starts))
        if t_side & o_side:
            looped += 1  # both sides are one network (a bypass), so there is nothing to bridge
            continue
        terminal_is_station = (apcs_in(t_side), len(t_side)) > (apcs_in(o_side), len(o_side))
        if terminal_is_station:
            station, gen, anchor = t_side, o_side, tt
            hv |= set(at(tt))
        else:
            station, gen, anchor = o_side, t_side, (sx, sy)
        hv |= gen
        stations = {(x, y) for x, y, _ in station}
        next_to = [(anchor[0] + dx, anchor[1] + dy) for dx, dy in step.values()]
        cands = [c for c in next_to if c in stations and c in cables and c != tt and c not in smes_tiles
                 and not (terminal_is_station is False and c in {(x, y) for x, y, _ in gen})]
        if cands:
            substations.add(cands[0])
    for sub in substations:
        placed["SubstationBasic"].append((sub[0], sub[1], 0.0))
    # A substation tile's own cable is on the station side.
    hv = {c for c in hv if (c[0], c[1]) not in substations}
    summary = (f"{paired} SMES with a terminal, {len(substations)} substations, {looped} bypassed (both sides one network), "
               f"{len(hv)} generator-side cables of {sum(len(v) for v in cable_dirs.values())}")
    return hv, substations, summary


def convert(src, dst, z, margin, rules):
    defs, cells = dmm.parse(src)
    level = {(x, y): k for (x, y, zz), k in cells.items() if zz == z}
    if not level:
        sys.exit(f"no cells on z={z}")

    # Crop to everything that is not plain space.
    def interesting(key):
        for a in defs[key]:
            if a.path.startswith("/turf") and a.path != "/turf/space":
                return True
        return False

    pts = [p for p, k in level.items() if interesting(k)]
    x0, x1 = min(p[0] for p in pts) - margin, max(p[0] for p in pts) + margin
    y0, y1 = min(p[1] for p in pts) - margin, max(p[1] for p in pts) + margin

    tilemap = {"Space": 0}
    tiles = {}
    placed = collections.defaultdict(list)  # proto -> [(x, y, rot)]
    unmapped = collections.Counter()
    lattice = []
    areas = {}  # tile -> area path
    walls = set()
    apc_tiles = set()
    cables = set()
    pipe_tiles = set()
    pipe_conflicts = 0
    smes_tiles = set()
    terminals = []  # (x, y, vg13 dir): the dir points at the machine the terminal feeds
    cable_dirs = collections.defaultdict(list)

    def tile_id(name):
        if name not in tilemap:
            tilemap[name] = len(tilemap)
        return tilemap[name]

    for (x, y), key in level.items():
        if not (x0 <= x <= x1 and y0 <= y <= y1):
            continue
        gx, gy = x - x0, y - y0
        for a in defs[key]:
            if a.path.startswith("/turf"):
                rule = rules.tile(a.path)
                if rule is None:
                    unmapped[a.path] += 1
                    continue
                tiles[(gx, gy)] = tile_id(rule["tile"])
                if "proto" in rule:
                    walls.add((gx, gy))
                    placed[rule["proto"]].append((gx, gy, 0.0))
            elif a.path.startswith("/area"):
                areas[(gx, gy)] = a.path
            else:
                rule = rules.atom(a.path)
                if rule is None:
                    unmapped[a.path] += 1
                    continue
                if rule.get("drop"):
                    continue
                if "tile" in rule:
                    # Lattice and the like are tiles in SS14; they only apply over bare space.
                    lattice.append((gx, gy, rule["tile"]))
                    continue
                if "disposal" in rule:
                    placed[disposal_proto(rule["disposal"], a)].append((gx, gy, DIR_ROT.get(dir_of(a), 0.0)))
                    continue
                if rule.get("cable"):
                    cable_dirs[(gx, gy)].append((int(a.vars.get("d1", "0") or 0), int(a.vars.get("d2", "0") or 0)))
                    # SS14 cables join their neighbours on their own, so vg13's d1/d2 geometry is not kept; one MV cable
                    # per tile that has any.
                    cables.add((gx, gy))
                    continue
                if "pipe" in rule:
                    if (gx, gy) in pipe_tiles and rule["pipe"] != "unary":
                        # vg13 stacks pipes on a tile in different layers; SS14 has none and would join them.
                        pipe_conflicts += 1
                        continue
                    proto, rot = pipe_proto(pipe_mask(rule["pipe"], dir_of(a)))
                    if rule["pipe"] != "unary":
                        pipe_tiles.add((gx, gy))
                    color = ["    - type: AtmosPipeColor", f"      color: '{rule['color']}'"] if "color" in rule else None
                    placed[proto].append((gx, gy, rot, color) if color else (gx, gy, rot))
                    continue
                rot = DIR_ROT.get(dir_of(a), 0.0) if rule.get("rot") == "dir" else 0.0
                if rule["proto"] == "APCBasic":
                    apc_tiles.add((gx, gy))
                if rule.get("smes"):
                    smes_tiles.add((gx, gy))
                    continue
                if rule.get("terminal"):
                    terminals.append((gx, gy, dir_of(a)))
                    continue
                extra = device_state(rule, a)
                placed[rule["proto"]].append((gx, gy, rot, extra) if extra else (gx, gy, rot))

    hv, substations, grid_summary = wire_smes(smes_tiles, terminals, cables, cable_dirs, apc_tiles, placed)
    hv_tiles = {(x, y) for x, y, _ in hv}
    # A tile whose cables are all on the generator side is HV only; one that also has a station cable keeps both.
    all_hv = {t for t in hv_tiles if all((t[0], t[1], i) in hv for i in range(len(cable_dirs[t])))}
    cables -= all_hv
    cables -= smes_tiles
    placed["CableHV"] = [(gx, gy, 0.0) for gx, gy in sorted(hv_tiles | smes_tiles | substations)]
    # A substation tile carries both voltages.
    cables |= substations
    # An APC always sits on the MV side.
    cables |= {t for t in apc_tiles if t in hv_tiles}
    placed["CableMV"] = [(gx, gy, 0.0) for gx, gy in sorted(cables)]
    print(grid_summary + f"; {sum(1 for t in apc_tiles if t in hv_tiles)} of {len(apc_tiles)} APCs sit on the generator side")
    # vg13 powers an APC's whole area. SS14's machines only take power from LV cable, so every floor tile of an area
    # with an APC gets LV, which makes the APC's network its area (the decision in docs/_VG/port-decisions.md).
    apc_areas = {areas[t] for t in apc_tiles if t in areas}
    placed["CableApcExtension"] = [(gx, gy, 0.0) for (gx, gy), ap in sorted(areas.items())
                                   if ap in apc_areas and tiles.get((gx, gy), 0) != 0 and (gx, gy) not in walls
                                   and (gx, gy) not in smes_tiles]

    for gx, gy, name in lattice:
        if tiles.get((gx, gy), 0) == 0:
            tiles[(gx, gy)] = tile_id(name)

    entities = write_map(dst, tilemap, tiles, placed)

    # SS14 splits a grid into pieces that touch only at corners; report them, since cables and pipes do not cross.
    todo = {t for t, i in tiles.items() if i != 0}
    sizes = []
    while todo:
        stack, n = [todo.pop()], 0
        while stack:
            cx, cy = stack.pop()
            n += 1
            for nb in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                if nb in todo:
                    todo.remove(nb)
                    stack.append(nb)
        sizes.append(n)
    sizes.sort(reverse=True)
    print(f"{len(sizes)} separate pieces of floor; largest {sizes[:5]}, {sum(1 for n in sizes if n < 10)} under 10 tiles")

    report = os.path.splitext(dst)[0] + ".unmapped.txt"
    with open(report, "w", encoding="utf-8") as f:
        for path, n in unmapped.most_common():
            f.write(f"{n}\t{path}\n")
    print(f"{dst}: {len(tiles)} tiles, {entities} entities, {sum(unmapped.values())} dropped atoms of "
          f"{len(unmapped)} types (see {report}); {len(cables)} cable tiles, {len(apc_areas)} powered areas, "
          f"{len(pipe_tiles)} pipe tiles, {pipe_conflicts} stacked pipes dropped")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src", nargs="?")
    ap.add_argument("dst", nargs="?")
    ap.add_argument("--z", type=int, default=1)
    ap.add_argument("--margin", type=int, default=3)
    ap.add_argument("--mapping", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "mapping.yml"))
    ap.add_argument("--check", action="store_true", help="validate mapping.yml against the prototypes and exit")
    args = ap.parse_args()

    rules = Rules(args.mapping)
    bad = rules.check()
    for b in bad:
        print("mapping:", b)
    if args.check:
        sys.exit(1 if bad else 0)
    if bad:
        sys.exit("fix mapping.yml first")
    if not args.src or not args.dst:
        ap.error("src and dst are required")
    convert(args.src, args.dst, args.z, args.margin, rules)


if __name__ == "__main__":
    main()
