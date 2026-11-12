#!/usr/bin/env python3
"""Validate the transported snapshot without requiring access to private Arkon."""
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def main():
    errors = []
    manifest = json.loads((ROOT/'provenance/runtime-extraction.json').read_text())
    for record in manifest['files']:
        path = ROOT/record['target']
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest()!=record['target_sha256']:
            errors.append('Snapshot drift: '+record['target'])
    adapters=json.loads((ROOT/'provenance/adapters.json').read_text())
    for record in adapters['files']:
        path=ROOT/record['target']
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest()!=record['target_sha256']:
            errors.append('Adapter provenance drift: '+record['target'])
    for path in ROOT.rglob('*'):
        if any(part in {'.godot','test-results'} for part in path.parts) or not path.is_file():continue
        if path.suffix not in {'.gd','.tscn','.tres','.gdshader','.gdshaderinc','.godot'}:continue
        for match in re.finditer(r'res://([^"\n]+)',path.read_text()):
            target=match.group(1)
            if target == 'test-results' or target.startswith('test-results/'):continue
            if not (ROOT/target).exists():errors.append(f'Dangling resource: {path.relative_to(ROOT)} -> {target}')
    campaign=json.loads((ROOT/'data/campaign_plan.json').read_text())
    dialogues=json.loads((ROOT/'data/dialogue_selection.json').read_text())
    scenes={d['id']:d for d in dialogues['scenes']}
    ids=[m['id'] for m in campaign['missions']]
    if len(ids)!=len(set(ids)):errors.append('Duplicate mission ID')
    covered=set()
    used=set()
    for mission in campaign['missions']:
        covered.update(mission['source_chapters'])
        used.update(mission['dialogue_ids'])
        if mission['playable'] or mission['status']!='proposal_pending_user_review':errors.append('Story production was marked approved without review')
        for key in ['goal','player_activity','canonical_beats','compression','implementation','invariant']:
            if not mission.get(key):errors.append('Incomplete adaptation decision: '+mission['id']+'/'+key)
        for ident in mission['dialogue_ids']:
            if ident not in scenes:errors.append('Unknown dialogue: '+ident)
    if covered!=set(range(1,12)):errors.append('The adaptation does not account for all 11 chapters')
    for dialogue in scenes.values():
        if dialogue['required'] and dialogue['id'] not in used:errors.append('Required dialogue is unreachable: '+dialogue['id'])
    if list(ROOT.rglob('*.keystore')):errors.append('Signing material must not be transported')
    if list(ROOT.rglob('source-private*')):errors.append('Full source book must stay outside the public repository')
    report={'status':'PASS' if not errors else 'FAIL','snapshot_files':len(manifest['files']),'missions':len(ids),'source_chapters':len(covered),'dialogues':len(scenes),'errors':errors}
    print(json.dumps(report,ensure_ascii=False,indent=2))
    return bool(errors)

if __name__=='__main__':raise SystemExit(main())
