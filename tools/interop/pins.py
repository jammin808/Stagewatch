#!/usr/bin/env python3
"""The interop lane's pins: the one reader of tools/interop/siblings.json, for tools/interop/build.sh and CI's two interop
workflows alike, so a pin that one of them takes the others take too, and one that one refuses they all refuse.

  pins.py [siblings.json]     one line per sibling, "<app> <repo> <ref> <build>", in the order patterns, mapper, agmessage

siblings.json holds exactly "v":1 (the integer: neither true nor 1.0), "patterns", "mapper" and "agmessage"; each holds exactly
"repo" (owner/name), "ref" (a full 40-hex commit, never a branch or tag name: a pin names one commit for good) and "build" (a
relative path inside the repository, no "..": the project or folder the lane builds). A key said twice in one object is
refused, at every level, so no second entry can stand behind the first a reader sees. Anything else is refused with exit 2 and
the reason on standard error, and nothing is printed.
"""
import json
import re
import sys
from pathlib import Path

APPS = ('patterns', 'mapper', 'agmessage')


def refuse(words):
    print(f'siblings.json refused: {words}', file=sys.stderr)
    sys.exit(2)


class Repeated(ValueError):
    """A key said twice in one object."""


def once(pairs):
    """json's object hook: the object, or Repeated when a key is said twice in it."""
    doc = {}
    for key, value in pairs:
        if key in doc:
            raise Repeated(key)
        doc[key] = value
    return doc


def read(path):
    try:
        doc = json.loads(Path(path).read_text(encoding='utf-8'), object_pairs_hook=once)
    except Repeated as e:
        refuse(f'the key "{e}" is said twice in one object')
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as e:
        refuse(f'{path} cannot be read as JSON ({e})')
    if not isinstance(doc, dict) or set(doc) != {'v', *APPS} or type(doc['v']) is not int or doc['v'] != 1:
        refuse('it takes exactly "v":1, "patterns", "mapper" and "agmessage"')
    pins = []
    for app in APPS:
        pin = doc[app]
        if not isinstance(pin, dict) or set(pin) != {'repo', 'ref', 'build'} or not all(isinstance(v, str) for v in pin.values()):
            refuse(f'{app} takes exactly "repo", "ref" and "build", each a string')
        if not re.fullmatch(r'[A-Za-z0-9-]+/[A-Za-z0-9._-]+', pin['repo']):
            refuse(f'{app}.repo is owner/name')
        if not re.fullmatch(r'[0-9a-f]{40}', pin['ref']):
            refuse(f'{app}.ref is a full commit (40 hex), never a branch or tag name')
        if not re.fullmatch(r'[A-Za-z0-9._/-]+', pin['build']) or '..' in pin['build'] or pin['build'].startswith('/'):
            refuse(f'{app}.build is a path inside the repository')
        pins.append((app, pin['repo'], pin['ref'], pin['build']))
    return pins


def main(argv):
    if len(argv) > 2:
        print('pins.py takes one argument at most: the siblings.json to read', file=sys.stderr)
        return 2
    path = argv[1] if len(argv) == 2 else Path(__file__).resolve().parent / 'siblings.json'
    for line in read(path):
        print(*line)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
