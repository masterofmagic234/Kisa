#!/usr/bin/env python3
"""Validate intro timing, vector references, preserved narration and media hashes.

This checks deliverability and data contracts. It does not rate the direction,
illustration or animation; those require reviewing the rendered sequence.
"""
import hashlib
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    errors = []
    book = json.loads((ROOT / 'data/intro.json').read_text())
    manifest = json.loads((ROOT / 'provenance/intro-media.json').read_text())
    words = sum(len(b['text'].split()) for b in book['blocks'])
    original = book['source']['original_words']
    if words != book['source']['adapted_words'] or not .9 <= words / original <= 1.03:
        errors.append('Monologue was materially shortened or its word count drifted')
    if book['point_of_view'] != 'first_person_internal_monologue':
        errors.append('Interior monologue format lost')
    total = 0
    used = set()
    for block in book['blocks']:
        total += block['duration']
        used.add(block['audio'])
        if ' '.join(c['text'] for c in block['timed_captions']) != block['text']:
            errors.append('A caption does not cover its spoken text: ' + block['id'])
        previous = 0
        for cue in block['timed_captions']:
            if abs(cue['start'] - previous) > .001 or cue['end'] <= cue['start']:
                errors.append('Gap or inverted caption: ' + block['id'])
            previous = cue['end']
        if abs(previous - block['duration']) > .001:
            errors.append('Caption track ends before the voice: ' + block['id'])
        if block['alignment'] != 'tts_word_boundaries':
            errors.append('Caption timing is estimated rather than word aligned')
    if len(book['blocks']) != 12:
        errors.append('Intro lost an opening paragraph')
    visuals = json.loads((ROOT / book['storyboard'].removeprefix('res://')).read_text())
    used.add('res://assets/intro/road-ambience.ogg')
    stats = {'shots': len(visuals['shots'])}

    def check_view(view, context):
        if (len(view) != 4 or not all(isinstance(x, (int, float)) and math.isfinite(x) for x in view)
                or min(view) < 0 or min(view[2:]) <= 0
                or view[0] + view[2] > 1.001 or view[1] + view[3] > 1.001):
            errors.append('Invalid camera crop: ' + context)

    vector = visuals.get('schema') == 2
    scenes = {}
    if vector:
        art_path = visuals.get('art_path', 'res://data/intro_art.json')
        used.add(art_path)
        art = json.loads((ROOT / art_path.removeprefix('res://')).read_text())
        scenes = art.get('scenes', {})
        if art.get('schema') != 1 or art.get('width', 0) <= 0 or art.get('height', 0) <= 0 or not scenes:
            errors.append('Missing or unsupported vector scene library')
        stats.update(vector_scenes=len(scenes), animated_nodes=0, articulated_nodes=0, panel_reveals=0)

        def check_node(node, context):
            kind = node.get('type')
            if kind not in ('polygon', 'ellipse', 'line', 'group'):
                errors.append('Unknown vector primitive: ' + context)
            points = node.get('points', [])
            if kind in ('polygon', 'line'):
                if len(points) < (3 if kind == 'polygon' else 2):
                    errors.append('Incomplete vector contour: ' + context)
                for point in points:
                    if len(point) != 2 or not all(isinstance(v, (int, float)) and math.isfinite(v) for v in point):
                        errors.append('Invalid vector point: ' + context)
            if kind == 'ellipse':
                rect = node.get('rect', [])
                if len(rect) != 4 or not all(isinstance(v, (int, float)) and math.isfinite(v) for v in rect) or min(rect[2:]) <= 0:
                    errors.append('Invalid ellipse: ' + context)
            keys = node.get('keys', [])
            previous_time = -1
            for key in keys:
                time = key.get('time', -1)
                if not isinstance(time, (int, float)) or not math.isfinite(time) or time < 0 or time <= previous_time:
                    errors.append('Unordered animation keys: ' + context)
                previous_time = time
                for field in ('x', 'y', 'rotation', 'scale_x', 'scale_y', 'alpha'):
                    if field in key and (not isinstance(key[field], (int, float)) or not math.isfinite(key[field])):
                        errors.append('Invalid animated transform: ' + context)
                if 'points' in key and (len(key['points']) != len(points) or any(len(p) != 2 or not all(isinstance(v, (int, float)) and math.isfinite(v) for v in p) for p in key['points'])):
                    errors.append('Contour morph changes its vertex contract: ' + context)
            if keys:
                stats['animated_nodes'] += 1
                if any('rotation' in key or 'points' in key for key in keys):
                    stats['articulated_nodes'] += 1
            if 'loop' in node and node['loop'] <= 0:
                errors.append('Invalid animation loop: ' + context)
            for i, child in enumerate(node.get('children', [])):
                check_node(child, context + '/' + str(i))
        for name, scene in scenes.items():
            if not scene.get('nodes'):
                errors.append('Empty scene: ' + name)
            for i, node in enumerate(scene.get('nodes', [])):
                check_node(node, name + '/' + str(i))
    else:
        used.update(block['atlas'] for block in book['blocks'])
        stats['art_cells'] = 0

    previous = 0
    ids = set()
    covered_blocks = set()
    cells = set()
    for shot in visuals['shots']:
        context = shot.get('id', '<unnamed>')
        if context in ids:
            errors.append('Duplicate shot id: ' + context)
        ids.add(context)
        if abs(shot['start'] - previous) > .001 or shot['end'] <= shot['start']:
            errors.append('Gap or inverted visual beat: ' + context)
        previous = shot['end']
        block_index = shot.get('block', -1)
        if not 0 <= block_index < len(book['blocks']):
            errors.append('Invalid narration block: ' + context)
        else:
            covered_blocks.add(block_index)
            before = sum(b['duration'] for b in book['blocks'][:block_index])
            after = before + book['blocks'][block_index]['duration']
            if shot['start'] < before - .001 or shot['end'] > after + .001:
                errors.append('Visual beat crosses its narration block: ' + context)
        if vector:
            if shot.get('layout', 'single') not in ('single', 'split', 'triptych', 'strip'):
                errors.append('Unsupported panel layout: ' + context)
            panels = shot.get('panels') or [{'scene': shot.get('scene'), 'reveal': 0}]
            if not any(panel.get('reveal', 0) <= 0 for panel in panels):
                errors.append('Composition begins with every panel hidden: ' + context)
            for i, panel in enumerate(panels):
                if panel.get('scene', shot.get('scene')) not in scenes:
                    errors.append('Missing vector scene: ' + context + '/' + str(i))
                reveal = panel.get('reveal', 0)
                if reveal < 0 or reveal >= shot['end'] - shot['start']:
                    errors.append('Panel reveal lies outside composition: ' + context)
                if reveal > 0:
                    stats['panel_reveals'] += 1
                for field in ('view_from', 'view_to'):
                    if field in panel:
                        check_view(panel[field], context + '/' + str(i))
        else:
            for entry in [shot, *shot['layers']]:
                cells.add((entry['asset'], entry['quadrant']))
                used.add(entry['asset'])
            for view in [shot['view_from'], shot['view_to']]:
                check_view(view, context)
    if abs(previous - book['duration']) > .001:
        errors.append('Picture does not cover the full monologue')
    if covered_blocks != set(range(len(book['blocks']))):
        errors.append('A spoken paragraph has no visual sequence')
    if not vector:
        stats['art_cells'] = len(cells)
    if visuals['captions_default']:
        errors.append('The default viewing mode is dominated by text')
    if abs(total - book['duration']) > .001:
        errors.append('Voice duration sum drifted')
    for entry in manifest['files']:
        path = ROOT / entry['path']
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != entry['sha256']:
            errors.append('Intro asset drift: ' + entry['path'])
        used.discard('res://' + entry['path'])
    if used:
        errors.append('Untracked production media: ' + repr(sorted(used)))
    adapted = '\n\n'.join(b['text'] for b in book['blocks'])
    if hashlib.sha256(adapted.encode()).hexdigest() != book['source']['adaptation_sha256']:
        errors.append('Monologue provenance drift')
    campaign = json.loads((ROOT / 'data/campaign_plan.json').read_text())
    if any(m['playable'] for m in campaign['missions']):
        errors.append('Intro change marked unfinished campaign levels playable')
    report = dict(status='PASS' if not errors else 'FAIL', scope='intro_only',
                  **stats, spoken_paragraphs=len(book['blocks']), adapted_words=words,
                  word_reduction_percent=book['source']['word_reduction_percent'],
                  voice_seconds=book['duration'], visual_quality_assessed=False, errors=errors)
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return bool(errors)


if __name__ == '__main__':
    raise SystemExit(main())
