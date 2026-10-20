"""Extracts vg13 clothing armor: path, name, armor, armor_absorb, body_parts_covered (with inheritance)."""
import re, glob, json, sys

defs = {}  # path -> dict of raw var strings
order = []
for f in glob.glob('../vgstation13/code/**/*.dm', recursive=True):
    cur = None
    for line in open(f, errors='ignore'):
        m = re.match(r'^(/obj/item/[\w/]+)\s*$', line.rstrip())
        if m:
            cur = m.group(1)
            defs.setdefault(cur, {'_file': f})
            order.append(cur)
            continue
        if line.startswith('/') or (line.strip() and not line[0].isspace()):
            cur = None
            continue
        if cur is None:
            continue
        m = re.match(r'^\t(\w+)\s*=\s*(.+?)\s*(//.*)?$', line.rstrip('\n'))
        if m:
            defs[cur][m.group(1)] = m.group(2)

def get(path, var):
    while path:
        if path in defs and var in defs[path]:
            return defs[path][var]
        path = path.rsplit('/', 1)[0]
    return None

def parse_armor(s):
    if not s or not s.startswith('list('):
        return None
    return {k: float(v) for k, v in re.findall(r'(\w+)\s*=\s*(-?[\d.]+)', s)}

out = []
for path in defs:
    if not path.startswith('/obj/item/clothing'):
        continue
    armor = parse_armor(get(path, 'armor'))
    if not armor or not any(armor.values()):
        continue
    name = get(path, 'name')
    out.append({'path': path, 'name': name.strip('"') if name else None, 'armor': armor,
                'absorb': parse_armor(get(path, 'armor_absorb')), 'covered': get(path, 'body_parts_covered'),
                'pen': get(path, 'armor_penetration')})
json.dump(out, open(sys.argv[1], 'w'), indent=1)
print(len(out))
