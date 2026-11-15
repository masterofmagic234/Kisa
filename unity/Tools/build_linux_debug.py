#!/usr/bin/env python3
"""Validate, build and package a genuine Linux x86_64 debug Player using an activated Unity Editor."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import struct
import subprocess
import sys
import tarfile
import uuid

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'unity/WhiteGuard'
EXPECTED_VERSION = '6000.3.28f1'
EXPECTED_MAPS = ['p00', 'm01', 'm02', 'm03', 'm04', 'm05', 'm06', 'm07', 'e00']


class BuildFailure(Exception):
    def __init__(self, message, exit_code=1):
        super().__init__(message)
        self.exit_code = exit_code if 0 < exit_code < 256 else 1


def utc():
    return datetime.now(timezone.utc).isoformat()


def sha_file(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        while True:
            block = stream.read(1024 * 1024)
            if not block:
                break
            digest.update(block)
    return digest.hexdigest()


def git_value(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args], text=True).strip()


def source_snapshot():
    # Native import is allowed to enrich .meta/settings. Authored source/data may not change during this build.
    selected = {}
    for directory in [ROOT/'unity/Packages/com.arkon.runtime', PROJECT/'Assets', PROJECT/'Packages']:
        for path in sorted(directory.rglob('*')):
            if path.is_file() and path.suffix.lower() in {'.cs', '.asmdef', '.json', '.png', '.mp3', '.ogg', '.wav', '.ttf', '.unity'}:
                selected[path.relative_to(ROOT).as_posix()] = sha_file(path)
    return selected


def require_elf_x64(path, label):
    if not path.is_file() or path.stat().st_size < 64:
        raise BuildFailure(label + ' is missing or empty: ' + path.name)
    with path.open('rb') as stream:
        header = stream.read(64)
    if header[:4] != b'\x7fELF' or header[4] != 2 or header[5] != 1 or struct.unpack_from('<H', header, 18)[0] != 62:
        raise BuildFailure(label + ' is not a genuine Linux x86_64 ELF file: ' + path.name)


def run_stage(editor, method, name, quit_editor, environment, logs, report, timeout, marker):
    log = logs / (name + '.log')
    arguments = [str(editor), '-batchmode', '-nographics']
    if quit_editor:
        arguments.append('-quit')
    arguments += ['-projectPath', str(PROJECT), '-executeMethod', method, '-logFile', str(log)]
    record = {'name': name, 'method': method, 'startedUtc': utc(), 'log': 'logs/' + log.name,
              'quitArgument': quit_editor, 'status': 'running'}
    report['stages'].append(record)
    print('UNITY_STAGE ' + name + ': ' + method, flush=True)
    try:
        result = subprocess.run(arguments, cwd=ROOT, env=environment, timeout=timeout)
    except subprocess.TimeoutExpired as error:
        record.update(status='timeout', finishedUtc=utc())
        raise BuildFailure('Unity stage timed out: ' + name, 124) from error
    record.update(exitCode=result.returncode, finishedUtc=utc())
    text = log.read_text(errors='replace') if log.is_file() else ''
    version = re.search(r'Unity Editor version:\s*([^\r\n]+)', text)
    if version:
        record['editorVersionReported'] = version.group(1).strip()
    if result.returncode != 0:
        record['status'] = 'failed'
        if 'No valid Unity Editor license found' in text:
            report['status'] = 'BLOCKED_ACTIVATION'
            raise BuildFailure('Unity has no valid activated Editor license. No Player archive was created.', result.returncode)
        raise BuildFailure('Unity stage failed: ' + name + ' (exit ' + str(result.returncode) + '). See ' + str(log), result.returncode)
    if not version or not version.group(1).strip().startswith(EXPECTED_VERSION + ' '):
        raise BuildFailure('Unity did not report the pinned Editor version in ' + name)
    if marker not in text:
        raise BuildFailure('Unity exited without the expected successful stage marker: ' + name)
    record['status'] = 'passed'


def package_filter(info):
    if info.isdir():
        info.mode |= 0o555
    # Make Linux launch permissions explicit even if the build was produced on Windows.
    elif info.isfile() and (info.name.endswith('/WhiteGuard') or info.name.endswith('.so') or info.name.endswith('/UnityCrashHandler64')):
        info.mode |= 0o111
    return info


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', required=True, type=Path, help='Exact Unity 6000.3.28f1 Editor executable, not Unity CLI/Hub.')
    parser.add_argument('--timeout', type=int, default=1800, help='Maximum seconds per genuine Editor stage (default: 1800).')
    args = parser.parse_args()
    editor = args.unity.expanduser().resolve()
    if not editor.is_file() or (os.name != 'nt' and not os.access(editor, os.X_OK)):
        parser.error('--unity must point to an executable Unity Editor.')
    if args.timeout <= 0:
        parser.error('--timeout must be positive.')
    version = (PROJECT/'ProjectSettings/ProjectVersion.txt').read_text()
    if 'm_EditorVersion: ' + EXPECTED_VERSION not in version:
        parser.error('The project no longer pins the expected Unity version; review the build wrapper first.')
    commit = git_value('rev-parse', 'HEAD')
    dirty = bool(git_value('status', '--porcelain', '--', 'unity', '.gitattributes'))
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    # Every attempt gets a new export directory: a failed Editor cannot package an older Player.
    run = PROJECT/'Builds/LinuxDebug'/('attempt-' + stamp + '-' + uuid.uuid4().hex[:8])
    logs = run/'logs'; player = run/'player'; logs.mkdir(parents=True); player.mkdir()
    output = player/'WhiteGuard'
    source_before = source_snapshot()
    report = {'schema': 1, 'status': 'RUNNING', 'startedUtc': utc(), 'sourceCommit': commit, 'sourceDirty': dirty,
              'unityVersionExpected': EXPECTED_VERSION, 'target': 'StandaloneLinux64', 'architecture': 'x86_64',
              'developmentBuildRequested': True, 'debugLevelMenuRequested': True, 'stages': [],
              'fullCampaignPlaythroughVerified': False, 'graphicsAndListeningReviewVerified': False,
              'note': 'Experimental debug build. Native startup checks do not complete the campaign or provide visual/audio acceptance.'}
    environment = os.environ.copy()
    environment.update(WHITEGUARD_DEVELOPMENT='1', WHITEGUARD_BUILD=str(output),
                       WHITEGUARD_NATIVE_REPORT=str(logs/'CampaignMapStartup.json'), WHITEGUARD_TESTED_COMMIT=commit)
    try:
        run_stage(editor, 'WhiteGuard.Editor.WhiteGuardGameBuild.ValidateData', '01-data-import', True,
                  environment, logs, report, args.timeout, 'WHITE GUARD DATA IMPORT CHECK PASSED:')
        run_stage(editor, 'WhiteGuard.Editor.CampaignGameChecks.Run', '02-native-startup', False,
                  environment, logs, report, args.timeout, 'WHITE GUARD NATIVE MAP STARTUP CHECK PASSED.')
        native_file = logs/'CampaignMapStartup.json'
        if not native_file.is_file():
            raise BuildFailure('The actual PlayMode probe did not write its native report.')
        native = json.loads(native_file.read_text())
        if (native.get('status') != 'PASS_MAP_STARTUP' or native.get('testedCommit') != commit
                or native.get('debugSaveIsolated') is not True
                or [m.get('id') for m in native.get('maps', [])] != EXPECTED_MAPS):
            raise BuildFailure('The native report does not confirm this source commit, debug save isolation and all nine authored map entries.')
        run_stage(editor, 'WhiteGuard.Editor.WhiteGuardGameBuild.BuildLinuxGame', '03-linux-player', True,
                  environment, logs, report, args.timeout, 'WHITE GUARD PLAYER BUILD SUCCEEDED:')
        if source_before != source_snapshot():
            raise BuildFailure('Authored source/data changed while Unity was validating/building. Freeze changes and retry.')
        data = player/'WhiteGuard_Data'
        require_elf_x64(output, 'Player executable'); require_elf_x64(player/'UnityPlayer.so', 'UnityPlayer library')
        if not data.is_dir() or not (data/'globalgamemanagers').is_file():
            raise BuildFailure('BuildPlayer did not export the complete WhiteGuard_Data directory.')
        if os.name != 'nt':
            output.chmod(output.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
        for path in player.rglob('*'):
            if path.is_symlink() and not path.resolve().is_relative_to(player.resolve()):
                raise BuildFailure('The exported Player depends on a symlink outside its bundle: ' + path.relative_to(player).as_posix())
        (player/'README_RU.txt').write_text(
            'БЕЛАЯ ГВАРДИЯ — экспериментальная Linux x86_64 debug-сборка\n\n'
            'Распакуйте ВЕСЬ архив в один каталог. Запуск: ./WhiteGuard\n'
            'Если распаковщик потерял права: chmod +x WhiteGuard\n'
            'Не отделяйте исполняемый файл от UnityPlayer.so, WhiteGuard_Data и остальных файлов.\n\n'
            'F8 — открыть/закрыть отладочный выбор всех девяти эпизодов.\n'
            'Выбор уровня также есть в главном меню. Он использует отдельное от обычной кампании сохранение.\n'
            'WASD — движение; мышь — прицел; ЛКМ — огонь; Shift — бег.\n'
            'E — контекстное действие; Q — бросок; 1/2/3 — оружие.\n'
            'J — журнал; Esc — пауза; Space/Enter — следующая сюжетная карточка.\n'
            'R после поражения — повтор с контрольной точки.\n\n'
            'Сборка прошла проверки импорта и native-загрузки карт, описанные в NATIVE_VALIDATION.json.\n'
            'Это НЕ доказательство полного прохождения кампании, качества изображения или прослушивания.\n'
            'Требуется полноценная ручная приёмка.\n', encoding='utf-8', newline='\n')
        report.update(status='BUILT_EXPERIMENTAL', finishedUtc=utc(), sourceInputsSha256=source_before,
                      nativeStartupReportSha256=sha_file(native_file), nativeMapStartupVerified=True,
                      standalonePlayerLaunchVerified=False)
        (player/'BUILD_INFO.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')
        (player/'NATIVE_VALIDATION.json').write_text(json.dumps(native, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')
        archive = run/('WhiteGuard-Linux-x86_64-debug-' + commit[:8] + '.tar.gz')
        partial = Path(str(archive)+'.part')
        try:
            with tarfile.open(partial, 'w:gz', compresslevel=6, dereference=True) as bundle:
                bundle.add(player, arcname='WhiteGuard-Linux-x86_64-debug', filter=package_filter)
            partial.replace(archive)
        finally:
            if partial.exists():
                partial.unlink()
        digest = sha_file(archive)
        Path(str(archive)+'.sha256').write_text(digest+'  '+archive.name+'\n', encoding='ascii', newline='\n')
        report.update(archive=archive.name, archiveSha256=digest, archiveBytes=archive.stat().st_size, archiveCreated=True)
        print('BUILT_PLAYER_ARCHIVE ' + str(archive), flush=True)
        return 0
    except BuildFailure as error:
        if report['status'] != 'BLOCKED_ACTIVATION':
            report['status'] = 'FAILED'
        report.update(finishedUtc=utc(), failure=str(error), exitCode=error.exit_code, archiveCreated=False)
        print('BUILD_STOPPED ' + str(error), file=sys.stderr, flush=True)
        return error.exit_code
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        report.update(status='FAILED', finishedUtc=utc(), failure=str(error), exitCode=1, archiveCreated=False)
        print('BUILD_STOPPED ' + str(error), file=sys.stderr, flush=True)
        return 1
    finally:
        (run/'build-attempt.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')
        print('BUILD_ATTEMPT_REPORT ' + str(run/'build-attempt.json'), flush=True)


if __name__ == '__main__':
    raise SystemExit(main())
