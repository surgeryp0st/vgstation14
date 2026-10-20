"""Lists SS14 entity prototypes with Armor or ClothingSpeedModifier (resolving parents) and their original values."""
import yaml, glob, json, sys

class L(yaml.SafeLoader): pass
L.add_multi_constructor('!', lambda l, s, n: l.construct_mapping(n) if isinstance(n, yaml.MappingNode) else None)

protos = {}
for f in glob.glob('Resources/Prototypes/**/*.yml', recursive=True):
    if '/_VG/' in f:
        continue
    try:
        docs = yaml.load(open(f), Loader=L)
    except Exception:
        continue
    for e in docs or []:
        if isinstance(e, dict) and e.get('type') == 'entity' and 'id' in e:
            e['_file'] = f
            protos[e['id']] = e

def parents(e):
    p = e.get('parent')
    return [] if p is None else ([p] if isinstance(p, str) else p)

def comp(e, name, seen=None):
    seen = seen or set()
    if e['id'] in seen: return None
    seen.add(e['id'])
    for c in e.get('components') or []:
        if c.get('type') == name:
            return c
    for p in parents(e):
        if p in protos:
            r = comp(protos[p], name, seen)
            if r is not None:
                return r
    return None

def name(e, seen=None):
    seen = seen or set()
    if 'name' in e: return e['name']
    for p in parents(e):
        if p in protos and p not in seen:
            seen.add(p)
            n = name(protos[p], seen)
            if n: return n
    return None

out = []
for id_, e in protos.items():
    if e.get('abstract'):
        continue
    a = comp(e, 'Armor') or {}
    sp = comp(e, 'ClothingSpeedModifier')
    if not a and not sp:
        continue
    c = comp(e, 'Clothing') or {}
    er = comp(e, 'ExplosionResistance') or {}
    out.append({'id': id_, 'name': name(e), 'file': e['_file'], 'slots': c.get('slots'),
                'coef': (a.get('modifiers') or {}).get('coefficients', {}),
                'flat': (a.get('modifiers') or {}).get('flatReductions', {}),
                'explosion': er.get('damageCoefficient'),
                'speed': (sp or {}).get('sprintModifier', 1.0) if sp else None,
                'parents': parents(e)})
json.dump(out, open(sys.argv[1], 'w'), indent=1)
print(len(out))
