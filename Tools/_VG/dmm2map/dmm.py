"""Parser for BYOND .dmm map files (TGM and plain format).

A parsed map is a dict key -> list of (path, vars) atoms (objs, then mob/turf/area last, as in the file), and a dict
(x, y, z) -> key. Variable values are kept as the raw DM text; only the ones the converter needs get interpreted.
"""
import re
from collections import namedtuple

Atom = namedtuple("Atom", "path vars")

_DEF = re.compile(r'^"([^"]+)" = \(\s*$')
_BLOCK = re.compile(r'^\((\d+),(\d+),(\d+)\) = \{"\s*$')


def _split_top(s, sep):
    """Split on sep outside of strings, parentheses, braces and brackets."""
    out, depth, cur, in_str, i = [], 0, [], False, 0
    while i < len(s):
        c = s[i]
        if in_str:
            cur.append(c)
            if c == "\\" and i + 1 < len(s):
                i += 1
                cur.append(s[i])
            elif c == '"':
                in_str = False
        elif c == '"':
            in_str = True
            cur.append(c)
        elif c in "({[":
            depth += 1
            cur.append(c)
        elif c in ")}]":
            depth -= 1
            cur.append(c)
        elif c == sep and depth == 0:
            out.append("".join(cur))
            cur = []
        else:
            cur.append(c)
        i += 1
    out.append("".join(cur))
    return out


def _brace_delta(line):
    d, in_str, i = 0, False, 0
    while i < len(line):
        c = line[i]
        if in_str:
            if c == "\\":
                i += 1
            elif c == '"':
                in_str = False
        elif c == '"':
            in_str = True
        elif c == "{":
            d += 1
        elif c == "}":
            d -= 1
        i += 1
    return d


def _parse_atom(text):
    text = text.strip()
    if not text:
        return None
    if text.endswith("}") and "{" in text:
        path, rest = text.split("{", 1)
        body = rest[:-1]
        vars_ = {}
        for item in _split_top(body, ";"):
            item = item.strip()
            if not item or "=" not in item:
                continue
            k, v = item.split("=", 1)
            vars_[k.strip()] = v.strip()
        return Atom(path.strip(), vars_)
    return Atom(text, {})


def parse(path):
    """Returns (defs, cells). cells maps (x, y, z) -> key; y counts up from the bottom like BYOND."""
    defs, cells = {}, {}
    with open(path, encoding="utf-8", errors="replace") as f:
        lines = f.read().split("\n")

    i, n = 0, len(lines)
    while i < n:
        line = lines[i]
        m = _DEF.match(line)
        if m:
            key, atoms = m.group(1), []
            i += 1
            buf = []
            depth = 0
            while i < n:
                l = lines[i]
                buf.append(l)
                depth += _brace_delta(l)
                i += 1
                if depth <= 0 and l.endswith(")"):
                    break
            text = " ".join(buf).replace("\t", " ")
            text = text[:text.rindex(")")]
            for part in _split_top(text, ","):
                a = _parse_atom(part)
                if a:
                    atoms.append(a)
            defs[key] = atoms
            continue
        else:
            m = _BLOCK.match(line)
            if m:
                x0, y0, z = (int(g) for g in m.groups())
                rows = []
                i += 1
                while i < n and lines[i] != '"}':
                    rows.append(lines[i])
                    i += 1
                # TGM: one column per block, top row first. Plain: a rectangle starting at (x0, y0).
                if rows and len(rows[0]) > 0:
                    keylen = len(next(iter(defs))) if defs else len(rows[0])
                    total = len(rows)
                    for ri, row in enumerate(rows):
                        for ci in range(0, len(row), keylen):
                            cells[(x0 + ci // keylen, y0 + (total - 1 - ri), z)] = row[ci:ci + keylen]
        i += 1
    return defs, cells
