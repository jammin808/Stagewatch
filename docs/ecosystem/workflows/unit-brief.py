#!/usr/bin/env python3
"""unit-brief.py <out-dir> <unit-id>...  — one short brief per unit of master-plan.md, built from the plan of record alone.

A brief holds the unit's plan section, its longer text (plan.md), its train's goal, rules R1-R4, the owner's answers it
waits on, its gaps, and from each source design the decisions, this repo's changes and tests and the lines naming its
source unit. It is built by text extraction only: nothing in it is written anew, so it cannot disagree with the plan.
"""
import json, os, re, sys

S = os.path.dirname(os.path.dirname(os.path.abspath(__file__))) if os.path.basename(os.path.dirname(os.path.abspath(__file__))) == 'workflows' else '/home/user/Stagewatch/docs/ecosystem'
DESIGNS = ['cross-app-proposals', 'ecosystem-lane', 'playout-links', 'peers-not-screens', 'patterns-playmap-side',
           'windows-for-peers', 'spec-1-3', 'agmessage']
ALIASES = {'cross-app': 'cross-app-proposals', 'ecosystem': 'ecosystem-lane', 'pps': 'patterns-playmap-side',
           'eye': 'eye', 'the profile': 'peerlink', 'peerlink': 'peerlink', 'plan critique': 'plan-critique',
           'peerlink-adoption': 'peerlink-adoption'}

def read(p):
    with open(os.path.join(S, p), encoding='utf-8') as f:
        return f.read()

def section(text, uid, level='###'):
    m = re.search(r'^' + re.escape(level) + r' ' + re.escape(uid) + r' — .*$', text, re.M)
    if not m:
        return None
    n = re.search(r'^#{2,3} ', text[m.end():], re.M)
    return text[m.start(): m.end() + (n.start() if n else len(text) - m.end())].rstrip() + '\n'

def train_goal(text, pos):
    heads = [m for m in re.finditer(r'^## Train \d+ — .*$', text, re.M) if m.start() < pos]
    if not heads:
        return ''
    h = heads[-1]
    rest = text[h.start():]
    cut = re.search(r'^\| Unit \|', rest, re.M)
    return rest[: cut.start() if cut else 1500].rstrip() + '\n'

def rules(text):
    a = text.index('**Rules the trains run by:**')
    b = text.index('- **R5', a)
    return text[a:b].rstrip() + '\n'

def brief(uid):
    mp = read('master-plan.md')
    sec = section(mp, uid)
    if sec is None:
        raise SystemExit(f'no section for {uid} in master-plan.md')
    out = [f'# Brief for {uid}\n',
           'Built by text extraction from the plan of record (docs/ecosystem in Stagewatch): nothing here is new wording.\n'
           'Patterns: every round of the plan is one higher in the tree (the renumbering note), and so are PLAN § numbers.\n',
           '## The unit (master-plan.md)\n', sec]
    longer = section(read('plan.md'), uid)
    if longer:
        out += ['## Its longer text (plan.md)\n', longer]
    out += ['## Its train\n', train_goal(mp, mp.index(sec[:40])), '## Rules\n', rules(mp)]

    head = sec.split('\n', 3)[2] if len(sec.split('\n')) > 2 else ''
    repo = (re.search(r'^\*([A-Za-z]+)\.\*', head) or [None, ''])[1]
    owners = re.findall(r'owner:([A-Za-z0-9-]+)', head)
    gaps = re.findall(r'\bG\d\d\b', (re.search(r'Gaps: ([^.]*)\.', head) or [None, ''])[1])
    sources = [s.strip() for s in (re.search(r'Sources: (.*?)\. Gaps:', head) or [None, ''])[1].split(';') if s.strip()]

    oa = read('owner-answers.md')
    picked = [ln for ln in oa.split('\n- ') if any(re.match(re.escape(o) + r'\b', ln.strip()) for o in owners)]
    ident = oa[oa.index('# Commit identity'):] if '# Commit identity' in oa else ''
    out += ['## Owner answers this unit waits on (owner-answers.md)\n',
            ''.join('- ' + p.strip() + '\n' for p in picked) or 'none\n', '\n' + ident + '\n']

    if gaps:
        table = {g['id']: g for g in json.load(open(os.path.join(S, 'data/gaps.json'), encoding='utf-8'))}
        out.append('## Gaps (data/gaps.json)\n')
        for g in gaps:
            x = table.get(g)
            if x:
                out.append(f"- **{g}** ({x.get('value','')}) {x.get('title','')}\n  - {x.get('statement','')[:900]}\n  - fix: {x.get('fix','')[:500]}\n  - evidence: {x.get('evidence','')[:400]}\n")

    seen = set()
    for src in sources:
        low = src.lower()
        name = next((d for d in DESIGNS if low.startswith(d)), None) or next((v for k, v in ALIASES.items() if low.startswith(k)), None)
        if not name:
            continue
        tag = src[len(next((d for d in DESIGNS + list(ALIASES) if low.startswith(d)), '')):].strip()
        tags = [t for t in re.findall(r'[A-Z]{2,}[-.][A-Za-z0-9.]+|unit \d+', tag)]
        path = name + '.md'
        if not os.path.exists(os.path.join(S, path)):
            continue
        md = read(path)
        out.append(f'## Source design: {path} ({src})\n')
        hits = []
        for t in tags:
            for i, ln in enumerate(md.split('\n'), 1):
                if re.search(r'(?<![A-Za-z0-9.-])' + re.escape(t) + r'(?![A-Za-z0-9])', ln):
                    hits.append(f'- line {i}: {ln.strip()[:1600]}\n')
        if hits:
            out += ['Lines naming this source unit:\n'] + hits[:24]
        if name in seen:
            continue
        seen.add(name)
        jp = os.path.join(S, 'data/units', name + '.json')
        if os.path.exists(jp):
            r = json.load(open(jp, encoding='utf-8'))['revised']
            out.append('\nDecisions:\n' + ''.join(f"- D{i}: {d['decision']}\n" for i, d in enumerate(r.get('decisions', []), 1)))
            ch = [c for c in r.get('changes', []) if repo.lower() in c.get('repo', '').lower()]
            if ch:
                out.append(f'\nChanges this design names in {repo} (this unit\'s are among them):\n' + ''.join(f"- `{c['file']}`: {c['change']}\n" for c in ch))
            ts = [t for t in r.get('tests', []) if repo.lower() in t.get('repo', '').lower()]
            if ts:
                out.append(f'\nTests this design names in {repo}:\n' + ''.join(f"- {t['where']}: {t['proves']}\n" for t in ts))
        heads = [f'  {i}: {ln}' for i, ln in enumerate(md.split('\n'), 1) if re.match(r'#{1,3} ', ln)]
        out.append(f'\nSections of {S}/{path} by line (open one only when this brief leaves a question open):\n' + '\n'.join(heads[:70]) + '\n')
    return '\n'.join(out)

if __name__ == '__main__':
    od = sys.argv[1]
    os.makedirs(od, exist_ok=True)
    for uid in sys.argv[2:]:
        t = brief(uid)
        with open(os.path.join(od, uid + '.md'), 'w', encoding='utf-8') as f:
            f.write(t)
        print(uid, len(t.encode()), 'bytes')
