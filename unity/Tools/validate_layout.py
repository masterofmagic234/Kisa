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
source=ROOT/'white_guard'
expected_sources={p.relative_to(ROOT).as_posix() for p in (source/'assets/intro').glob('*.png')}
expected_sources.update(p.relative_to(ROOT).as_posix() for p in (source/'assets/intro/audio').glob('*.mp3'))
expected_sources.update('white_guard/'+p for p in [
    'data/intro.json','data/intro_shots.json','assets/intro/road-ambience.ogg',
    *['assets/reused/fonts/'+name for name in ['Manrope-Variable.ttf','Forum-Regular.ttf','Manrope-OFL.txt','Forum-OFL.txt','FONT_NOTICE.md']]])
if {entry['source'] for entry in media['files']}!=expected_sources:
    errors.append('Media manifest does not cover the current complete intro source set; rerun prepare_white_guard.py.')
for entry in media['files']:
    a=(ROOT/entry['source']).read_bytes();b=(ROOT/entry['target']).read_bytes()
    if a!=b or hashlib.sha256(b).hexdigest()!=entry['sha256']:errors.append('Media drift: '+entry['target'])
stage=json.loads((source/'data/intro_shots.json').read_text())
if media.get('source_stage_schema')!=stage['schema'] or media.get('source_shot_count')!=len(stage['shots']):
    errors.append('Generated stage metadata is stale; rerun prepare_white_guard.py.')
source_textures={p.relative_to(source).as_posix() for p in (source/'assets/intro').glob('*.png')}
for art_id,art in stage.get('art',{}).items():
    if art[0].removeprefix('res://') not in source_textures:
        errors.append('Missing schema3 artwork: '+art_id)
book=json.loads((P/'Assets/Resources/WhiteGuardIntro/intro.json').read_text())
if len(book['blocks'])!=12 or sum(len(b['text'].split()) for b in book['blocks'])!=1018:errors.append('Monologue changed.')
guids={}
for root in [U/'Packages/com.arkon.runtime', P/'Assets']:
    for path in root.rglob('*'):
        if path.suffix!='.meta' and '__pycache__' not in path.parts and not Path(str(path)+'.meta').is_file():
            errors.append('Missing stable Unity meta: '+str(path.relative_to(ROOT)))
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
report=dict(status='PASS' if not errors else 'FAIL',unchanged_media_files=len(media['files']),source_stage_schema=stage['schema'],source_shot_count=len(stage['shots']),unique_guids=len(guids),spoken_words=1018,unity_editor_verified=False,errors=errors)
print(json.dumps(report,ensure_ascii=False,indent=2));raise SystemExit(bool(errors))
