#!/usr/bin/env python3
"""Check the complete, slightly shortened intro and every production media hash."""
import hashlib
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]

def main():
    errors=[]
    book=json.loads((ROOT/'data/intro.json').read_text())
    manifest=json.loads((ROOT/'provenance/intro-media.json').read_text())
    words=sum(len(b['text'].split()) for b in book['blocks'])
    original=book['source']['original_words']
    if words!=book['source']['adapted_words'] or not .9<=words/original<=1.03:errors.append('Monologue was materially shortened or its word count drifted')
    if book['point_of_view']!='first_person_internal_monologue':errors.append('Interior monologue format lost')
    total=0;art=set();used=set()
    for block in book['blocks']:
        total+=block['duration'];art.add((block['atlas'],block['quadrant']))
        used.update([block['atlas'],block['audio']])
        if ' '.join(c['text'] for c in block['timed_captions'])!=block['text']:errors.append('A caption does not cover its spoken text: '+block['id'])
        previous=0
        for cue in block['timed_captions']:
            if abs(cue['start']-previous)>.001 or cue['end']<=cue['start']:errors.append('Gap or inverted caption: '+block['id'])
            previous=cue['end']
        if abs(previous-block['duration'])>.001:errors.append('Caption track ends before the voice: '+block['id'])
        if block['alignment']!='tts_word_boundaries':errors.append('Caption timing is estimated rather than word aligned')
    if len(art)!=8 or len(book['blocks'])!=12:errors.append('Intro lost an illustration or opening paragraph')
    if abs(total-book['duration'])>.001:errors.append('Voice duration sum drifted')
    for entry in manifest['files']:
        p=ROOT/entry['path']
        if not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest()!=entry['sha256']:errors.append('Intro asset drift: '+entry['path'])
        used.discard('res://'+entry['path'])
    if used:errors.append('Untracked production media: '+repr(used))
    adapted='\n\n'.join(b['text'] for b in book['blocks'])
    if hashlib.sha256(adapted.encode()).hexdigest()!=book['source']['adaptation_sha256']:errors.append('Monologue provenance drift')
    campaign=json.loads((ROOT/'data/campaign_plan.json').read_text())
    if any(m['playable'] for m in campaign['missions']):errors.append('Intro change marked unfinished campaign levels playable')
    report={'status':'PASS' if not errors else 'FAIL','scope':'intro_only','drawings':len(art),'spoken_paragraphs':len(book['blocks']),'adapted_words':words,'word_reduction_percent':book['source']['word_reduction_percent'],'voice_seconds':book['duration'],'errors':errors}
    print(json.dumps(report,ensure_ascii=False,indent=2));return bool(errors)

if __name__=='__main__':raise SystemExit(main())
