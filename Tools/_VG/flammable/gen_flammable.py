#!/usr/bin/env python3
"""Generates Resources/Prototypes/_VG/Fire/flammable.yml: which SS14 entities burn, as vg13's would.

    python3 Tools/_VG/flammable/gen_flammable.py

vg13 marks objects flammable by type (code/modules/ZAS/Fire.dm, `flammable = TRUE`), takes the material from the
recycling type (w_type) and the thermal mass from the item size (w_class: tiny and small 0.1, medium 1, large 5,
huge 20, giant 50). SS14's entities are sorted into vg13's categories by their parents, clothing slot and components:

- Clothing burns (fabric), except what vg13 makes fireproof: glasses, helmets, hardhats, welding gear, EVA suits,
  hardsuits, armor, fire suits and knuckles. Belts are leather (biological); headsets and ID cards plastic.
- Food is biological. Trash is plastic with vg13's mass of 1.
- Books, folders, paper bins, boxes, planks and cash are wood; plushies, bedsheets and cloth fabric; dice, medkits,
  flashlights, encryption keys and health analyzers plastic; soap biological.
- Wooden furniture and curtains burn with the mass vg13's size gives them.

Every concrete entity gets an entry (burns or not); new ones inherit from their nearest listed parent at runtime.
Entities that already burn some other way (VgBurnable in their prototype) are left out.
"""
import glob
import os
import yaml

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT = os.path.join(ROOT, "Resources", "Prototypes", "_VG", "Fire", "flammable.yml")


class Loader(yaml.SafeLoader):
    pass


Loader.add_multi_constructor("!", lambda l, s, n: l.construct_mapping(n) if isinstance(n, yaml.MappingNode) else None)

protos = {}
for f in glob.glob(os.path.join(ROOT, "Resources", "Prototypes", "**", "*.yml"), recursive=True):
    if "/_VG/" in f:
        continue
    try:
        docs = yaml.load(open(f, encoding="utf-8"), Loader=Loader)
    except Exception:
        continue
    for e in docs or []:
        if isinstance(e, dict) and e.get("type") == "entity" and "id" in e:
            protos[e["id"]] = e


def parents(e):
    p = e.get("parent")
    return [] if p is None else ([p] if isinstance(p, str) else list(p))


def ancestors(id_):
    """The entity and all its parents, nearest first."""
    out, queue = [], [id_]
    while queue:
        cur = queue.pop(0)
        if cur in out or cur not in protos:
            continue
        out.append(cur)
        queue.extend(parents(protos[cur]))
    return out


def comp(id_, name):
    for a in ancestors(id_):
        for c in protos[a].get("components") or []:
            if isinstance(c, dict) and c.get("type") == name:
                return c
    return None


def field(id_, name, key):
    """A component field from the nearest ancestor that sets it (children often override only part of one)."""
    for a in ancestors(id_):
        for c in protos[a].get("components") or []:
            if isinstance(c, dict) and c.get("type") == name and key in c:
                return c[key]
    return None


def tags(id_):
    out = set()
    for a in ancestors(id_):
        for c in protos[a].get("components") or []:
            if isinstance(c, dict) and c.get("type") == "Tag":
                out.update(c.get("tags") or [])
    return out


# vg13 w_class -> thermal_mass, by SS14 item size.
SIZE_MASS = {"Tiny": 0.1, "Small": 0.1, "Normal": 1, "Large": 5, "Huge": 20, "Ginormous": 50}

# Items and structures by parent: material and, for things without an item size, mass.
BY_PARENT = [
    ("BookBase", "Wood", None), ("BoxFolderBase", "Wood", None), ("PaperBin", "Wood", 1), ("BoxBase", "Wood", 1),
    ("BasePlushie", "Fabric", None), ("BaseDice", "Plastic", None), ("BedsheetBase", "Fabric", 0.1),
    ("MaterialCloth", "Fabric", None), ("MaterialWoodPlank", "Wood", None), ("SpaceCash", "Wood", None),
    ("Medkit", "Plastic", 1), ("FlashlightLantern", "Plastic", None), ("EncryptionKey", "Plastic", None),
    ("HandheldHealthAnalyzer", "Plastic", None), ("Soap", "Biological", None), ("IDCardStandard", "Plastic", None),
    # vg13 wooden chair and table (large: 5), bookcase, cabinet and coffin (huge: 20), curtain (medium: 1).
    ("ChairWood", "Wood", 5), ("TableWood", "Wood", 5), ("Bookshelf", "Wood", 20), ("Dresser", "Wood", 20),
    ("CrateCoffin", "Wood", 20), ("BaseCurtains", "Plastic", 1),
]

FIREPROOF_HEAD = ("Helmet", "Hardhat", "Welding", "EVA", "Hardsuit")
FIREPROOF_OUTER = ("Hardsuit", "EVA", "Armor", "Fire", "Ninja")


def classify(id_):
    """(material, mass) if it burns, None if it doesn't, or "skip" if vg13 has no say."""
    anc = ancestors(id_)
    # Mobs burn through SS14's fire stacks, and some things already burn their own way.
    if comp(id_, "VgBurnable") is not None or comp(id_, "MobState") is not None:
        return "skip"

    def contains(*parts):
        return any(p in a for a in anc for p in parts)

    if "BaseCigar" in anc:
        return None

    clothing = comp(id_, "Clothing")
    item = comp(id_, "Item")
    if clothing is not None and item is not None:
        raw = field(id_, "Clothing", "slots") or []
        slots = [str(x).lower() for x in ([raw] if isinstance(raw, str) else raw)]
        if "eyes" in slots:
            return None
        if "head" in slots and contains(*FIREPROOF_HEAD):
            return None
        # vg13's winter coats burn, security ones included.
        if "outerclothing" in slots and contains(*FIREPROOF_OUTER) and not contains("Winter"):
            return None
        if "mask" in slots and contains("Welding"):
            return None
        if "gloves" in slots and contains("Knuckle"):
            return None
        if "ears" in slots or "idcard" in slots or "id" in slots:
            return ("Plastic", None)
        # SS14 sizes storage by what it holds; vg13 belts are medium (1) and backpacks large (5).
        if "belt" in slots:
            return ("Biological", 1)
        if "back" in slots:
            return ("Fabric", 5)
        return ("Fabric", None)

    for parent, material, mass in BY_PARENT:
        if parent in anc:
            return (material, mass)

    if comp(id_, "Food") is not None and comp(id_, "Drink") is None:
        return ("Biological", None)

    if "Trash" in tags(id_) and item is not None:
        return ("Plastic", 1)

    return "skip"


def mass_of(id_, mass):
    if mass is not None:
        return mass
    return SIZE_MASS.get(str(field(id_, "Item", "size") or "Small"), 0.1)


lines = [
    "# Generated by Tools/_VG/flammable/gen_flammable.py: which SS14 entities burn, sorted into vg13's categories.",
    "# Do not edit by hand; change the rules in the script and regenerate.",
    "",
]
burning = fireproof = 0
for id_ in sorted(protos):
    e = protos[id_]
    if e.get("abstract"):
        continue
    result = classify(id_)
    if result == "skip":
        continue
    lines.append("- type: vgFlammable")
    lines.append(f"  id: {id_}")
    if result is None:
        lines.append("  burns: false")
        fireproof += 1
    else:
        material, mass = result
        lines.append(f"  material: {material}")
        lines.append(f"  thermalMass: {mass_of(id_, mass):g}")
        burning += 1
    lines.append("")

open(OUT, "w", encoding="utf-8").write("\n".join(lines))
print(f"{OUT}: {burning} burn, {fireproof} fireproof")
