"""Extracts vg13 clothing: path, name, armor, armor_absorb, body_parts_covered, slowdown, heat_conductivity and
max_heat_protection_temperature (with inheritance)."""
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

# Slowdown defines (__DEFINES/game.dm).
DEFINES = {}
for line in open('../vgstation13/__DEFINES/game.dm', errors='ignore'):
    m = re.match(r'#define\s+(\w*SLOWDOWN\w*)\s+([\w.]+)', line)
    if m:
        DEFINES[m.group(1)] = m.group(2)

# Numeric defines (heat conductivities, heat protection temperatures).
NUMBERS = {}
for f in glob.glob('../vgstation13/__DEFINES/*.dm'):
    for line in open(f, errors='ignore'):
        m = re.match(r'#define\s+(\w+)\s+(-?[\d.]+)\b', line)
        if m:
            NUMBERS[m.group(1)] = float(m.group(2))

def number(path, var):
    v = get(path, var)
    if v is None:
        return None
    v = v.strip()
    if v in NUMBERS:
        return NUMBERS[v]
    try:
        return float(v)
    except ValueError:
        return None

def slowdown(path):
    v = get(path, 'slowdown')
    for _ in range(5):
        if v in DEFINES:
            v = DEFINES[v]
    try:
        return float(v) if v else 1.0
    except ValueError:
        return 1.0

out = []
for path in defs:
    if not path.startswith('/obj/item/clothing'):
        continue
    armor = parse_armor(get(path, 'armor')) or {}
    slow = slowdown(path)
    name = get(path, 'name')
    out.append({'path': path, 'name': name.strip('"') if name else None, 'armor': armor,
                'absorb': parse_armor(get(path, 'armor_absorb')), 'covered': get(path, 'body_parts_covered'),
                'pen': get(path, 'armor_penetration'), 'slowdown': slow,
                'conductivity': number(path, 'heat_conductivity'), 'maxheat': number(path, 'max_heat_protection_temperature')})
json.dump(out, open(sys.argv[1], 'w'), indent=1)
print(len(out))
