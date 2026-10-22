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


def write_map(dst, tilemap, tiles, placed, station=None):
    """Writes an SS14 map: tilemap {name: id}, tiles {(x, y): id}, placed {proto: [(x, y, rot)]}."""
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
        for lx in range(CHUNK):
            for ly in range(CHUNK):
                raw += struct.pack("<iBB", tl.get((lx, ly), 0), 0, 0)
        out += [f"        {cx},{cy}:", f"          ind: {cx},{cy}", f"          tiles: {base64.b64encode(bytes(raw)).decode()}",
                "          version: 6"]
    out += ["    - type: Broadphase", "    - type: Physics", "      bodyStatus: InAir", "      angularDamping: 0.05",
            "      linearDamping: 0.05", "      fixedRotation: False", "      bodyType: Dynamic", "    - type: Fixtures",
            "      fixtures: {}", "    - type: Gravity", "      enabled: true", "    - type: DecalGrid", "    - type: OccluderTree",
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
        for gx, gy, rot in placed[proto]:
            out += [f"  - uid: {uid}", "    components:", "    - type: Transform"]
            if rot:
                out.append(f"      rot: {rot} rad")
            out += [f"      pos: {gx + 0.5},{gy + 0.5}", f"      parent: {grid_uid}"]
            uid += 1

    os.makedirs(os.path.dirname(os.path.abspath(dst)), exist_ok=True)
    with open(dst, "w", encoding="utf-8") as f:
        f.write("\n".join(out) + "\n")
    return uid - 3



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
                    placed[rule["proto"]].append((gx, gy, 0.0))
            elif a.path.startswith("/area"):
                continue
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
                rot = DIR_ROT.get(dir_of(a), 0.0) if rule.get("rot") == "dir" else 0.0
                placed[rule["proto"]].append((gx, gy, rot))

    for gx, gy, name in lattice:
        if tiles.get((gx, gy), 0) == 0:
            tiles[(gx, gy)] = tile_id(name)

    entities = write_map(dst, tilemap, tiles, placed)

    report = os.path.splitext(dst)[0] + ".unmapped.txt"
    with open(report, "w", encoding="utf-8") as f:
        for path, n in unmapped.most_common():
            f.write(f"{n}\t{path}\n")
    print(f"{dst}: {len(tiles)} tiles, {entities} entities, {sum(unmapped.values())} dropped atoms of "
          f"{len(unmapped)} types (see {report})")


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
