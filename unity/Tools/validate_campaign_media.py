#!/usr/bin/env python3
"""Validate real campaign media and directing data without Unity or Pillow.

Default checks use PNG headers/CRC, exact source/audio SHA and manifests.
--probe additionally checks all MP3 codec/duration with the ffprobe executable.
Neither mode is a native Unity, GPU, audio-listening or artistic review.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import struct
import subprocess
import zlib

ROOT = Path(__file__).resolve().parents[2]
RESOURCES = ROOT / 'unity/WhiteGuard/Assets/Resources'
CAMPAIGN = RESOURCES / 'WhiteGuardCampaign'


def finite(value: object) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def integer(value: object) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


class Validator:
    def __init__(self, args: argparse.Namespace):
        self.args = args
        self.errors: list[str] = []
        self.inputs: dict[str, str] = {}
        self.checks = 0
        self.stats: dict[str, int | float] = {}
        self.png_cache: dict[Path, tuple[int, int, str]] = {}

    def check(self, condition: bool, message: str) -> bool:
        self.checks += 1
        if not condition:
            self.errors.append(message)
        return condition

    def path_label(self, path: Path) -> str:
        try:
            return path.resolve().relative_to(ROOT).as_posix()
        except ValueError:
            return str(path.resolve())

    def raw(self, path: Path) -> bytes:
        data = path.read_bytes()
        self.inputs[self.path_label(path)] = hashlib.sha256(data).hexdigest()
        return data

    def load(self, path: Path) -> dict:
        def reject_constant(value: str):
            raise ValueError('Nonstandard JSON number: ' + value)

        data = json.loads(self.raw(path).decode('utf-8'), parse_constant=reject_constant)
        if not isinstance(data, dict):
            raise ValueError('Expected JSON object: ' + self.path_label(path))
        return data

    def resource_path(self, key: str, suffix: str) -> Path:
        if not isinstance(key, str) or not key or any(not re.fullmatch(r'[A-Za-z0-9_-]+', part) for part in key.split('/')):
            raise ValueError('Invalid Resources key: ' + repr(key))
        return self.args.resources.joinpath(*key.split('/')).with_suffix(suffix)

    def png(self, path: Path) -> tuple[int, int, str]:
        path = path.resolve()
        if path in self.png_cache:
            return self.png_cache[path]
        raw = self.raw(path)
        label = self.path_label(path)
        if len(raw) < 45 or raw[:8] != b'\x89PNG\r\n\x1a\n' or raw[8:16] != b'\x00\x00\x00\rIHDR':
            raise ValueError('Invalid PNG signature/IHDR: ' + label)
        if struct.unpack('>I', raw[29:33])[0] != zlib.crc32(raw[12:29]) & 0xffffffff:
            raise ValueError('Invalid PNG IHDR CRC: ' + label)
        width, height, depth, color, compression, filtering, interlace = struct.unpack('>IIBBBBB', raw[16:29])
        if width <= 0 or height <= 0 or depth not in (1, 2, 4, 8, 16) or color not in (0, 2, 3, 4, 6) or compression != 0 or filtering != 0 or interlace not in (0, 1):
            raise ValueError('Unsupported/invalid PNG IHDR values: ' + label)
        self.check(raw[-12:] == b'\x00\x00\x00\x00IEND\xaeB`\x82', 'Missing final PNG IEND: ' + label)
        value = (width, height, hashlib.sha256(raw).hexdigest())
        self.png_cache[path] = value
        return value

    def pixel_rect(self, rect: list, width: int, height: int, label: str) -> bool:
        if not self.check(isinstance(rect, list) and len(rect) == 4 and all(integer(v) for v in rect), 'Invalid pixel rectangle: ' + label):
            return False
        x, y, w, h = rect
        return self.check(x >= 0 and y >= 0 and w > 0 and h > 0 and x + w <= width and y + h <= height, 'Pixel rectangle exceeds image: ' + label)

    def normalized_rect(self, rect: list, label: str) -> bool:
        if not self.check(isinstance(rect, list) and len(rect) == 4 and all(finite(v) for v in rect), 'Invalid/nonfinite normalized rectangle: ' + label):
            return False
        x, y, w, h = rect
        return self.check(x >= 0 and y >= 0 and w > 0 and h > 0 and x + w <= 1.00001 and y + h <= 1.00001, 'Normalized rectangle exceeds its picture: ' + label)

    def art(self) -> tuple[dict, set[str], dict[str, tuple[int, int]]]:
        data = self.load(self.args.art_manifest)
        self.check(data.get('schema') == 1 and data.get('coordinates') == 'top_left', 'Unsupported art manifest schema/coordinates.')
        entries = {}
        resources = set()
        dimensions = {}
        atlases = data.get('atlases', [])
        self.check(bool(atlases), 'Art manifest has no atlases.')
        for atlas in atlases:
            resource = atlas['resource']
            self.check(resource not in resources, 'Duplicate art atlas resource: ' + resource)
            resources.add(resource)
            width, height, digest = self.png(self.resource_path(resource, '.png'))
            dimensions[resource] = (width, height)
            self.check((width, height) == (atlas.get('width'), atlas.get('height')), 'PNG/manifest dimensions differ: ' + resource)
            self.check(digest == atlas.get('sha256'), 'PNG SHA mismatch: ' + resource)
            self.check(integer(atlas.get('columns')) and atlas['columns'] > 0 and integer(atlas.get('rows')) and atlas['rows'] > 0, 'Invalid art grid: ' + resource)
            for entry in atlas.get('entries', []):
                ident = entry['id']
                self.check(isinstance(ident, str) and bool(ident) and ident not in entries, 'Empty/duplicate sprite region: ' + str(ident))
                entries[ident] = resource
                self.pixel_rect([entry.get(k) for k in ('x', 'y', 'w', 'h')], width, height, ident)
                for field in ('cell', 'inkBounds'):
                    if field in entry:
                        self.pixel_rect(entry[field], width, height, ident + '.' + field)
                if 'facingClockwiseDegrees' in entry:
                    self.check(finite(entry['facingClockwiseDegrees']), 'Nonfinite sprite facing: ' + ident)
        alias_count = 0
        for category, aliases in data.get('styleMappings', {}).items():
            seen = set()
            for alias in aliases:
                style = alias['style']
                self.check(style and style not in seen, 'Duplicate/empty style alias: ' + category + '/' + style)
                seen.add(style)
                self.check(alias.get('entryId') in entries, 'Unknown style region: ' + category + '/' + style)
                alias_count += 1
        seen_actors = set()
        for alias in data.get('actorMappings', []):
            actor = alias['actor']
            self.check(actor and actor not in seen_actors, 'Duplicate/empty actor alias: ' + actor)
            seen_actors.add(actor)
            self.check(alias.get('prefix', '') + '_idle' in entries, 'Unknown actor idle region: ' + actor)
            if 'physicalScale' in alias:
                self.check(finite(alias['physicalScale']) and alias['physicalScale'] > 0, 'Invalid actor scale: ' + actor)
        self.stats.update(pngAtlases=len(atlases), spriteRegions=len(entries), styleAliases=alias_count, actorAliases=len(seen_actors))
        return data, resources, dimensions

    def approved_art(self, resources: set[str], dimensions: dict) -> None:
        path = self.args.resources.parent / 'original-media-manifest.json'
        data = self.load(path)
        for item in data.get('files', []):
            target = ROOT / item['target']
            if target.suffix != '.png':
                continue
            key = target.relative_to(self.args.resources).with_suffix('').as_posix()
            width, height, digest = self.png(target)
            self.check(digest == item.get('sha256'), 'Approved original PNG SHA mismatch: ' + key)
            resources.add(key)
            dimensions[key] = (width, height)

    def picture(self, resource: str, quadrant: int, resources: set[str], label: str) -> None:
        self.check(resource in resources, 'Unknown story Resources image: ' + label + ' -> ' + str(resource))
        self.check(integer(quadrant) and -1 <= quadrant <= 3, 'Invalid story quadrant: ' + label)

    def voice(self, cards: dict) -> dict[str, float]:
        data = self.load(self.args.voice_manifest)
        self.check(data.get('schema') == 1 and data.get('status') == 'complete', 'Voice manifest is not complete schema1.')
        self.check(data.get('campaignSHA256') == hashlib.sha256(self.args.campaign.read_bytes()).hexdigest(), 'Voice manifest campaign SHA mismatch.')
        self.check(data.get('totalCards') == len(cards) and data.get('generatedCards') == len(cards), 'Voice manifest count mismatch.')
        self.check(not data.get('pendingCardIds') and not data.get('failures'), 'Voice manifest still has pending/failed cards.')
        durations = {}
        total_bytes = 0
        probed = 0
        actual_ids = {p.stem for p in self.args.voice_manifest.parent.glob('*.mp3')}
        self.check(actual_ids == set(cards), 'MP3 filenames do not exactly match current campaign cards.')
        self.check(not list(self.args.voice_manifest.parent.glob('*.part')), 'Partial synthesis files remain in Voice directory.')
        expected_preset = {'voice': 'ru-RU-DmitryNeural', 'rate': '-5%', 'pitch': '-4Hz'}
        for key, value in expected_preset.items():
            self.check(data.get(key) == value, 'Unexpected global voice preset: ' + key)
        if self.args.probe and not self.args.ffprobe:
            self.check(False, '--probe requested but ffprobe was not found.')
        for entry in data.get('cards', []):
            ident = entry['cardId']
            if not self.check(ident in cards and ident not in durations, 'Unknown/duplicate voice card: ' + ident):
                continue
            card = cards[ident]
            self.check(entry.get('textSHA256') == hashlib.sha256(card['text'].encode('utf-8')).hexdigest(), 'Voice text SHA mismatch: ' + ident)
            for key, value in expected_preset.items():
                self.check(entry.get(key) == value, 'Unexpected card voice preset: ' + ident + '/' + key)
            resource = 'WhiteGuardCampaign/Voice/' + ident
            self.check(entry.get('resource') == resource, 'Voice resource differs from card ID: ' + ident)
            path = self.resource_path(resource, '.mp3')
            raw = self.raw(path)
            total_bytes += len(raw)
            self.check(len(raw) >= 1024 and len(raw) == entry.get('bytes'), 'MP3 empty/byte-count mismatch: ' + ident)
            self.check(hashlib.sha256(raw).hexdigest() == entry.get('audioSHA256'), 'MP3 audio SHA mismatch: ' + ident)
            duration = entry.get('durationSeconds')
            if not self.check(finite(duration) and duration > 0, 'Invalid voice duration: ' + ident):
                continue
            if self.args.probe and self.args.ffprobe:
                result = subprocess.run([self.args.ffprobe, '-v', 'error', '-show_entries',
                                         'format=duration:stream=codec_name,sample_rate,channels', '-of', 'json', str(path)],
                                        capture_output=True, text=True, check=True, timeout=30)
                info = json.loads(result.stdout)
                stream = next((s for s in info.get('streams', []) if s.get('codec_name') == 'mp3'), None)
                self.check(stream is not None, 'ffprobe did not find MP3 audio: ' + ident)
                measured = float(info['format']['duration'])
                self.check(math.isfinite(measured) and measured > 0 and abs(measured - duration) <= .002, 'ffprobe duration differs from manifest: ' + ident)
                if stream:
                    self.check(int(stream['sample_rate']) == entry.get('sampleRate') and int(stream['channels']) == entry.get('channels'), 'ffprobe format differs from manifest: ' + ident)
                duration = measured
                probed += 1
            durations[ident] = duration
        self.check(set(durations) == set(cards), 'Voice manifest does not cover all campaign cards exactly.')
        self.stats.update(voiceCards=len(durations), mp3Bytes=total_bytes, voiceDurationSeconds=round(sum(durations.values()), 6), ffprobeFiles=probed)
        return durations

    def run(self) -> None:
        campaign = self.load(self.args.campaign)
        cards = {}
        for level in campaign['levels']:
            for side in ('before', 'after'):
                for card in level.get(side, []):
                    ident = card['id']
                    self.check(isinstance(ident, str) and bool(re.fullmatch(r'[A-Za-z0-9_-]+', ident)) and ident not in cards, 'Invalid/duplicate campaign card: ' + str(ident))
                    self.check(isinstance(card.get('text'), str) and bool(card['text'].strip()), 'Empty campaign card text: ' + ident)
                    cards[ident] = card
        self.check(len(cards) == self.args.expected_cards, 'Expected full ' + str(self.args.expected_cards) + '-card campaign coverage.')
        art, resources, dimensions = self.art()
        self.approved_art(resources, dimensions)
        entries = {entry['id']: atlas['resource'] for atlas in art['atlases'] for entry in atlas['entries']}
        themes = set()
        for item in art.get('storyMappings', []):
            self.check(item['theme'] and item['theme'] not in themes, 'Duplicate/empty story theme: ' + item['theme'])
            themes.add(item['theme'])
            self.picture(item.get('resource'), item.get('quadrant'), resources, 'theme:' + item['theme'])
            if item.get('entryId') in entries:
                self.check(entries[item['entryId']] == item['resource'], 'Story theme region/atlas mismatch: ' + item['theme'])
            else:
                self.check(item.get('entryId') == 'approved_intro_convoy' and item.get('resource') == 'WhiteGuardIntro/opening-atlas-a' and item.get('quadrant') == 0, 'Unknown story region: ' + item['theme'])
        for ident, card in cards.items():
            self.picture(card.get('art'), card.get('quadrant'), resources, ident)
        durations = self.voice(cards)
        direction = self.load(self.args.direction)
        self.check(direction.get('schema') == 1, 'Unsupported story-direction schema.')
        seen = set()
        beats_count = 0
        insets = 0
        for shot in direction.get('shots', []):
            ident = shot['cardId']
            if not self.check(ident in cards and ident not in seen, 'Unknown/duplicate directed card: ' + ident):
                continue
            seen.add(ident)
            beats = shot.get('beats', [])
            self.check(bool(beats), 'Directed card has no beats: ' + ident)
            previous = -1.0
            for index, beat in enumerate(beats):
                label = ident + '/beat' + str(index)
                seconds = beat.get('at')
                valid_time = finite(seconds) and seconds >= 0
                self.check(valid_time, 'Invalid/nonfinite cut time: ' + label)
                if valid_time:
                    self.check(seconds > previous, 'Story cut times are not strictly increasing: ' + label)
                    if index == 0:
                        self.check(seconds == 0, 'First story beat must start at zero: ' + ident)
                    if ident in durations:
                        self.check(seconds <= durations[ident] + .001, 'Story cut exceeds actual voice duration: ' + label)
                    previous = seconds
                self.normalized_rect(beat.get('view'), label + '/view')
                if 'inset' in beat:
                    self.normalized_rect(beat['inset'], label + '/inset')
                if beat.get('art'):
                    self.picture(beat['art'], beat.get('quadrant', 0), resources, label)
                if beat.get('insetArt'):
                    self.check(beat['insetArt'] in entries, 'Unknown directed inset region: ' + label + '/' + beat['insetArt'])
                    self.check('inset' in beat, 'Inset art has no normalized placement: ' + label)
                    insets += 1
                beats_count += 1
        self.check(seen == set(cards), 'Story direction must cover every campaign card exactly once.')
        self.stats.update(campaignCards=len(cards), directedCards=len(seen), directionBeats=beats_count, detailInsets=insets, storyThemes=len(themes), knownPngResources=len(resources))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--resources', type=Path, default=RESOURCES)
    parser.add_argument('--campaign', type=Path, default=CAMPAIGN / 'campaign.json')
    parser.add_argument('--art-manifest', type=Path, default=RESOURCES / 'WhiteGuardArt/art-manifest.json')
    parser.add_argument('--voice-manifest', type=Path, default=CAMPAIGN / 'Voice/voice-manifest.json')
    parser.add_argument('--direction', type=Path, default=CAMPAIGN / 'story-direction.json')
    parser.add_argument('--expected-cards', type=int, default=88)
    parser.add_argument('--probe', action='store_true')
    parser.add_argument('--ffprobe', default=shutil.which('ffprobe'))
    parser.add_argument('--report', type=Path, default=ROOT / 'unity/Validation/Campaign/media-integrity.json')
    args = parser.parse_args()
    check = Validator(args)
    try:
        check.run()
    except (OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError) as error:
        check.errors.append(type(error).__name__ + ': ' + str(error))
    report = {'schema': 1, 'status': 'PASS' if not check.errors else 'FAIL',
              'checks': check.checks, **check.stats,
              'ffprobeRequested': args.probe, 'unityEditorExecuted': False,
              'nativeGpuVerified': False, 'auditoryReviewVerified': False,
              'pngDecodedPixelsVerified': False,
              'scope': 'File/header/CRC/hash, declared region/alias and exact card/voice/directing integrity only. No Unity, GPU, decoded-image or listening verification.',
              'durationEvidence': 'ffprobe MP3 measurement' if args.probe and check.stats.get('ffprobeFiles') == args.expected_cards else 'Manifest durations tied to verified audio SHA; ffprobe not completed.',
              'inputs': [{'path': path, 'sha256': digest} for path, digest in sorted(check.inputs.items())],
              'errors': check.errors}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps({key: value for key, value in report.items() if key != 'inputs'}, ensure_ascii=False, indent=2))
    return 0 if not check.errors else 1


if __name__ == '__main__':
    raise SystemExit(main())
