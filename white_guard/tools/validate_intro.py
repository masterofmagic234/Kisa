#!/usr/bin/env python3
"""Validate PNG storyboard geometry, timing, preserved narration and media hashes.

This checks deliverability and data contracts. It does not rate the direction,
illustration or animation; those require reviewing the rendered sequence.
"""
import hashlib
import json
import math
import struct
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

    def check_rect(rect, context, bounds=None):
        if (len(rect) != 4 or not all(math.isfinite(n) for n in rect)
                or rect[2] <= 0 or rect[3] <= 0):
            errors.append('Invalid rectangle: ' + context)
        elif bounds and (min(rect[:2]) < 0 or rect[0] + rect[2] > bounds[0] + .001
                         or rect[1] + rect[3] > bounds[1] + .001):
            errors.append('Rectangle extends outside its image: ' + context)

    def check_track(keys, fields, context, duration, bounded=()):
        previous_time = -1
        for key in keys:
            if (len(key) != fields + 1 or not all(math.isfinite(n) for n in key)
                    or key[0] <= previous_time or not 0 <= key[0] <= duration + .3
                    or any(not 0 <= key[i] <= 1 for i in bounded)):
                errors.append('Invalid motion key: ' + context)
                return
            previous_time = key[0]
        if not keys or keys[0][0] != 0:
            errors.append('Motion track must define its opening state: ' + context)

    if visuals.get('schema') != 3 or abs(visuals['duration'] - book['duration']) > .001:
        errors.append('Unexpected PNG storyboard version or duration')
    art = visuals['art']
    poses = visuals['poses']
    formations = visuals['formations']
    image_sizes = {}
    for name, (uri, *region) in art.items():
        path = ROOT / uri.removeprefix('res://')
        if not uri.startswith('res://assets/intro/') or path.suffix != '.png':
            errors.append('Invalid PNG reference: ' + name)
            continue
        if uri not in image_sizes:
            with path.open('rb') as png:
                header = png.read(24)
            if header[:8] != b'\x89PNG\r\n\x1a\n' or header[12:16] != b'IHDR':
                errors.append('Invalid PNG header: ' + uri)
                continue
            image_sizes[uri] = struct.unpack('>II', header[16:24])
        check_rect(region, name, image_sizes[uri])
        used.add(uri)
    used.update(block['atlas'] for block in book['blocks'])
    stats.update(raster_regions=len(art), formations=len(formations))

    def check_art(name, context):
        if name not in art:
            errors.append('Missing raster region in ' + context + ': ' + str(name))

    for name, frames in poses.items():
        if not frames:
            errors.append('Empty raster pose: ' + name)
        for frame in frames:
            check_art(frame, name)
    for name, wheels in visuals['wheels'].items():
        check_art(name, 'wheel parent')
        for pose, x, y in wheels:
            if pose not in poses or min(x, y) < 0:
                errors.append('Invalid wheel reference: ' + name)
    for name, fleet in formations.items():
        projection = fleet['projection']
        if projection not in ('avenue', 'side') or not fleet['vehicles']:
            errors.append('Invalid vehicle formation: ' + name)
        for sprite, x, depth, width, ax, ay in fleet['vehicles']:
            check_art(sprite, name)
            if (not all(map(math.isfinite, (x, depth, width, ax, ay))) or width <= 0
                    or not 0 <= ax <= 1 or not 0 <= ay <= 1 or depth <= 0):
                errors.append('Invalid vehicle ground contact: ' + name)
        if projection == 'avenue':
            depths = [vehicle[2] for vehicle in fleet['vehicles']]
            if depths != sorted(depths, reverse=True):
                errors.append('Vehicles are out of depth order: ' + name)

    previous = 0
    ids = set()
    covered_blocks = set()
    for shot in visuals['shots']:
        context = shot['id']
        duration = shot['end'] - shot['start']
        if context in ids or abs(shot['start'] - previous) > .001 or duration <= 0:
            errors.append('Duplicate, missing or inverted visual beat: ' + context)
        ids.add(context)
        previous = shot['end']
        index = shot['block']
        if not 0 <= index < len(book['blocks']):
            errors.append('Invalid narration block: ' + context)
        else:
            covered_blocks.add(index)
            before = sum(b['duration'] for b in book['blocks'][:index])
            if shot['start'] < before - .001 or shot['end'] > before + book['blocks'][index]['duration'] + .001:
                errors.append('Visual beat crosses its narration block: ' + context)
        for panel in shot['panels']:
            reveal = panel.get('reveal', 0)
            if not 0 <= reveal < duration:
                errors.append('Panel reveal lies outside the shot: ' + context)
            for field, bound in [('crop', None), ('rect', (768, 432))]:
                if field in panel:
                    check_rect(panel[field], context + ' ' + field, bound)
            if 'camera' in panel:
                check_track(panel['camera'], 2, context, duration - reveal)
            if 'art' in panel:
                check_art(panel['art'], context)
                if panel['art'] in art:
                    check_rect(panel['crop'], context + ' crop', art[panel['art']][3:])
            if 'traffic' in panel:
                traffic = panel['traffic']
                if traffic['fleet'] not in formations or not math.isfinite(traffic['rate']) or traffic['rate'] == 0:
                    errors.append('Invalid traffic motion: ' + context)
            for layer in panel.get('behind', []) + panel.get('layers', []):
                if 'pose' in layer:
                    if layer['pose'] not in poses:
                        errors.append('Missing pose: ' + context)
                    check_track(layer['phase'], 1, context, duration - reveal, (1,))
                else:
                    check_art(layer.get('art'), context)
                for field, count, bounds in [('motion', 3, (3,)), ('wipe', 1, (1,))]:
                    if field in layer:
                        check_track(layer[field], count, context, duration - reveal, bounds)
    if abs(previous - book['duration']) > .001 or covered_blocks != set(range(len(book['blocks']))):
        errors.append('Picture does not cover the full monologue')
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
