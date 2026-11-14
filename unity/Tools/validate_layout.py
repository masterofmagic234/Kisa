#!/usr/bin/env python3
"""Check project/package wiring and byte-identical source media; no Editor claim."""
import hashlib
import json
import re
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
U=ROOT/'unity'; P=U/'WhiteGuard'; errors=[]
manifest=json.loads((P/'Packages/manifest.json').read_text())
dependency=manifest['dependencies']['com.arkon.runtime']
resolved=(P/'Packages'/dependency.removeprefix('file:')).resolve()
if resolved != (U/'Packages/com.arkon.runtime').resolve():errors.append('Local shared package path does not resolve correctly.')
timing=re.search(r'Fixed Timestep:\s*([0-9.eE+-]+)',(P/'ProjectSettings/TimeManager.asset').read_text())
if timing is None or abs(float(timing.group(1))-1/60)>1e-7:errors.append('Physics timestep does not match the Godot source default60Hz.')
media=json.loads((P/'Assets/original-media-manifest.json').read_text())
for entry in media['files']:
    a=(ROOT/entry['source']).read_bytes();b=(ROOT/entry['target']).read_bytes()
    if a!=b or hashlib.sha256(b).hexdigest()!=entry['sha256']:errors.append('Media drift: '+entry['target'])
book=json.loads((P/'Assets/Resources/WhiteGuardIntro/intro.json').read_text())
if len(book['blocks'])!=12 or sum(len(b['text'].split()) for b in book['blocks'])!=1018:errors.append('Monologue changed.')
guids={}
for path in list((U/'Packages').rglob('*.meta'))+list((P/'Assets').rglob('*.meta')):
    value=next((line[6:].strip() for line in path.read_text().splitlines() if line.startswith('guid: ')),None)
    if not value or len(value)!=32:errors.append('Invalid meta: '+str(path))
    elif value in guids:errors.append('Duplicate GUID: '+str(path))
    else:guids[value]=path
scene=(P/'Assets/Scenes/Opening.unity').read_text()
script=U/'Packages/com.arkon.runtime/Runtime/Unity/WhiteGuardBootstrap.cs.meta'
script_guid=next(line[6:].strip() for line in script.read_text().splitlines() if line.startswith('guid: '))
if script_guid not in scene:errors.append('Opening scene does not reference its bootstrap script.')
for path in (U/'Packages/com.arkon.runtime/Runtime/Core').rglob('*.cs'):
    if 'using UnityEngine' in path.read_text():errors.append('Core acquired an engine dependency: '+str(path))
report=dict(status='PASS' if not errors else 'FAIL',unchanged_media_files=len(media['files']),unique_guids=len(guids),spoken_words=1018,unity_editor_verified=False,errors=errors)
print(json.dumps(report,ensure_ascii=False,indent=2));raise SystemExit(bool(errors))
