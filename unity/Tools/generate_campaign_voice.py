#!/usr/bin/env python3
"""Generate unchanged campaign card text with the approved intro TTS preset.

Install campaign-voice-requirements.txt into a venv outside the repository.
The first missing card is a service probe; a failed probe stops the batch.
Existing intro audio is never read, rewritten or replaced by this tool.
"""
from __future__ import annotations

import argparse
import asyncio
import hashlib
from importlib.metadata import version
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
RESOURCE_ROOT = ROOT / 'unity/WhiteGuard/Assets/Resources'
DEFAULT_CAMPAIGN = RESOURCE_ROOT / 'WhiteGuardCampaign/campaign.json'
DEFAULT_OUTPUT = RESOURCE_ROOT / 'WhiteGuardCampaign/Voice'
PRESET = {'voice': 'ru-RU-DmitryNeural', 'rate': '-5%', 'pitch': '-4Hz'}


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def cards_from(path: Path) -> list[dict]:
    campaign = json.loads(path.read_text(encoding='utf-8'))
    cards = []
    seen = set()
    for level in campaign['levels']:
        for side in ('before', 'after'):
            for card in level.get(side, []):
                ident = card['id']
                if not re.fullmatch(r'[A-Za-z0-9_-]+', ident) or ident in seen:
                    raise ValueError('Invalid or duplicate card ID: ' + ident)
                text = card['text']
                if not isinstance(text, str) or not text.strip():
                    raise ValueError('An audio card needs nonempty source text: ' + ident)
                seen.add(ident)
                cards.append({'cardId': ident, 'levelId': level['id'], 'text': text,
                              'textSHA256': sha(text.encode('utf-8'))})
    if not cards:
        raise ValueError('Campaign has no cards.')
    return cards


def inspect_mp3(path: Path, ffprobe: str) -> dict:
    if not path.is_file() or path.stat().st_size < 1024:
        raise ValueError('Missing or empty synthesized MP3.')
    result = subprocess.run([ffprobe, '-v', 'error', '-show_entries',
                             'format=duration:stream=codec_name,sample_rate,channels',
                             '-of', 'json', str(path)], capture_output=True, text=True, check=True)
    data = json.loads(result.stdout)
    stream = next((s for s in data.get('streams', []) if s.get('codec_name') == 'mp3'), None)
    duration = float(data.get('format', {}).get('duration', '0'))
    if stream is None or not 0.2 < duration < 600 or int(stream.get('channels', 0)) < 1:
        raise ValueError('Synthesized file is not a usable nonempty MP3.')
    return {'durationSeconds': round(duration, 6), 'sampleRate': int(stream['sample_rate']),
            'channels': int(stream['channels']), 'bytes': path.stat().st_size,
            'audioSHA256': sha(path.read_bytes())}


def error_record(error: Exception) -> dict:
    # Network exception URLs may contain the SDK's public client query token.
    # Report useful error classes/status without logging URLs or proxy credentials.
    result = {'type': type(error).__name__}
    status = getattr(error, 'status', None)
    if status is not None:
        result['httpStatus'] = int(status)
    if isinstance(error, (ValueError, FileNotFoundError)):
        result['message'] = str(error)
    return result


async def generate(args: argparse.Namespace) -> int:
    import edge_tts
    import edge_tts.communicate

    # edge-tts pins its own certifi context. Add, rather than replace, the
    # inherited trusted CA bundle. Certificate/hostname verification stays on.
    ca = os.environ.get('SSL_CERT_FILE') or os.environ.get('REQUESTS_CA_BUNDLE')
    if ca and Path(ca).is_file():
        edge_tts.communicate._SSL_CTX.load_verify_locations(cafile=ca)
    proxy = (os.environ.get('HTTPS_PROXY') or os.environ.get('https_proxy')
             or os.environ.get('HTTP_PROXY') or os.environ.get('http_proxy'))
    cards = cards_from(args.campaign)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    manifest_path = output / 'voice-manifest.json'
    old = json.loads(manifest_path.read_text(encoding='utf-8')) if manifest_path.is_file() else {}
    previous = {c['cardId']: c for c in old.get('cards', [])}
    ready: dict[str, dict] = {}
    failures: dict[str, dict] = {}
    source_sha = sha(args.campaign.read_bytes())
    library_version = version('edge-tts')

    def record(card: dict, audio: dict) -> dict:
        return {'cardId': card['cardId'], 'levelId': card['levelId'],
                'textSHA256': card['textSHA256'], **PRESET, **audio,
                'resource': 'WhiteGuardCampaign/Voice/' + card['cardId']}

    def write_manifest(status: str) -> None:
        report = {'schema': 1, 'status': status,
                  'service': 'Microsoft online TTS via edge-tts',
                  'edgeTtsVersion': library_version, **PRESET,
                  'campaignSHA256': source_sha, 'totalCards': len(cards),
                  'generatedCards': len(ready),
                  'cards': [ready[c['cardId']] for c in cards if c['cardId'] in ready],
                  'pendingCardIds': [c['cardId'] for c in cards if c['cardId'] not in ready],
                  'failures': [{'cardId': k, **v} for k, v in failures.items()],
                  'introAudioChanged': False, 'runwayCreditsUsed': 0,
                  'unityPlaybackVerified': False,
                  'validation': 'Nonempty MP3, codec, duration and source/audio SHA; native playback still needs review.'}
        temporary = manifest_path.with_suffix('.json.tmp')
        temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')
        temporary.replace(manifest_path)

    pending = []
    for card in cards:
        old_card = previous.get(card['cardId'])
        path = output / (card['cardId'] + '.mp3')
        if old_card and all(old_card.get(k) == v for k, v in PRESET.items()) and old_card.get('textSHA256') == card['textSHA256']:
            try:
                audio = inspect_mp3(path, args.ffprobe)
                if audio['audioSHA256'] == old_card.get('audioSHA256'):
                    ready[card['cardId']] = record(card, audio)
                    continue
            except (ValueError, OSError, subprocess.SubprocessError):
                pass
        if path.exists():
            raise ValueError('Unverified/stale card MP3 exists: ' + card['cardId'] +
                             '. Preserve it outside Resources before regeneration; do not play stale voice against new text.')
        pending.append(card)
    write_manifest('complete' if not pending else 'pending')

    async def synthesize(card: dict) -> bool:
        path = output / (card['cardId'] + '.mp3')
        temporary = output / (card['cardId'] + '.mp3.part')
        for attempt in range(args.attempts):
            try:
                communication = edge_tts.Communicate(card['text'], **PRESET, proxy=proxy)
                await asyncio.wait_for(communication.save(str(temporary)), timeout=args.timeout)
                audio = inspect_mp3(temporary, args.ffprobe)
                temporary.replace(path)
                ready[card['cardId']] = record(card, audio)
                failures.pop(card['cardId'], None)
                print(json.dumps({'cardId': card['cardId'], 'status': 'ready',
                                  'durationSeconds': audio['durationSeconds']}, ensure_ascii=False), flush=True)
                write_manifest('generating')
                return True
            except Exception as error:
                if temporary.exists():
                    temporary.unlink()
                failures[card['cardId']] = error_record(error)
                print(json.dumps({'cardId': card['cardId'], 'attempt': attempt + 1,
                                  'status': 'failed', **failures[card['cardId']]}, ensure_ascii=False), flush=True)
                # Authorization, certificate and route errors need a real fix,
                # not repeated requests or disabled TLS validation.
                fatal = (getattr(error, 'status', None) in (401, 403)
                         or type(error).__name__ in ('ClientConnectorCertificateError', 'ClientConnectorError'))
                if fatal or attempt + 1 == args.attempts:
                    return False
                await asyncio.sleep(min(8, 2 ** (attempt + 1)))
        return False

    if not pending:
        return 0
    # One actual service probe, before scheduling the other 87 requests.
    if not await synthesize(pending[0]):
        write_manifest('blocked' if not ready else 'partial')
        return 1
    selected = pending[1:args.limit] if args.limit else pending[1:]
    cursor = iter(selected)
    stop = asyncio.Event()

    async def worker() -> None:
        while not stop.is_set():
            card = next(cursor, None)
            if card is None:
                break
            if not await synthesize(card):
                stop.set()
                break
            if args.spacing:
                await asyncio.sleep(args.spacing)

    await asyncio.gather(*(worker() for _ in range(args.concurrency)))
    complete = len(ready) == len(cards)
    write_manifest('complete' if complete else 'partial')
    return 0 if complete else 2


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--campaign', type=Path, default=DEFAULT_CAMPAIGN)
    parser.add_argument('--output', type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument('--ffprobe', default=shutil.which('ffprobe'))
    parser.add_argument('--concurrency', type=int, choices=(1, 2), default=1)
    parser.add_argument('--attempts', type=int, choices=(1, 2, 3), default=3)
    parser.add_argument('--timeout', type=float, default=60)
    parser.add_argument('--spacing', type=float, default=2, help='Seconds between successful requests per worker.')
    parser.add_argument('--limit', type=int, default=0, help='Probe a limited number of missing cards; 0 means all.')
    args = parser.parse_args()
    if not args.ffprobe:
        parser.error('Install ffprobe before synthesis.')
    if args.limit < 0 or args.timeout <= 0 or args.spacing < 0:
        parser.error('limit/spacing must be nonnegative and timeout positive.')
    try:
        return asyncio.run(generate(args))
    except (OSError, ValueError, ImportError) as error:
        print(json.dumps({'status': 'failed', **error_record(error)}, ensure_ascii=False), file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
