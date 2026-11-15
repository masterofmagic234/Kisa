#!/usr/bin/env python3
"""Wire the authored Unity campaign without replacing imported intro media/settings."""
import argparse
import json
from pathlib import Path
import re
import uuid

ROOT = Path(__file__).resolve().parents[2]
UNITY = ROOT / 'unity'
PROJECT = UNITY / 'WhiteGuard'
ASSETS = PROJECT / 'Assets'
BOOTSTRAP = ASSETS / 'Scripts/Presentation/WhiteGuardGameBootstrap.cs'
GAME = ASSETS / 'Scenes/WhiteGuard.unity'
OPENING = ASSETS / 'Scenes/Opening.unity'
SETTINGS = PROJECT / 'ProjectSettings/EditorBuildSettings.asset'
GUID_NAMESPACE = uuid.UUID('148da6b4-dd8f-4fc4-9fac-eaa35141a82e')


def stable_guid(path):
    return uuid.uuid5(GUID_NAMESPACE, path.relative_to(UNITY).as_posix()).hex


def actual_guid(path):
    file = Path(str(path)+'.meta')
    match = re.search(r'^guid: ([0-9a-fA-F]{32})$', file.read_text(), re.M) if file.is_file() else None
    if match is None:
        raise ValueError('Missing or invalid Unity meta: '+file.relative_to(ROOT).as_posix())
    return match.group(1).lower()


def ensure_meta(path):
    file = Path(str(path)+'.meta')
    if file.exists():
        actual_guid(path)
        return 0
    file.write_text('fileFormatVersion: 2\nguid: '+stable_guid(path)+'\n'+('folderAsset: yes\n' if path.is_dir() else ''), newline='\n')
    return 1


def asset_paths():
    return [p for p in sorted(ASSETS.rglob('*')) if p.suffix != '.meta' and '__pycache__' not in p.parts]


def scene_text(bootstrap_guid):
    return '''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &1000
GameObject:
  m_ObjectHideFlags: 0
  m_Component:
  - component: {fileID: 1001}
  - component: {fileID: 1002}
  m_Layer: 0
  m_Name: White Guard Campaign
  m_TagString: Untagged
  m_IsActive: 1
--- !u!4 &1001
Transform:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 1000}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_Children: []
  m_Father: {fileID: 0}
--- !u!114 &1002
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 1000}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: '''+bootstrap_guid+''', type: 3}
  m_Name:
  m_EditorClassIdentifier:
'''


def scene_entries(settings):
    match = re.search(r'^  m_Scenes:[ \t]*\n(.*?)(?=^  [a-zA-Z_]|\Z)', settings, re.M | re.S)
    if match is None:
        raise ValueError('Unsupported EditorBuildSettings: m_Scenes block is missing.')
    body = match.group(1)
    entry = re.compile(r'^  - enabled: ([01])[ \t]*\n    path: (.+)\n    guid: ([0-9a-fA-F]{32})[ \t]*\n?', re.M)
    values = [(m.group(2).strip(), m.group(1), m.group(3).lower()) for m in entry.finditer(body)]
    if entry.sub('', body).strip():
        raise ValueError('Unsupported EditorBuildSettings scene fields; preserve/edit the existing list explicitly.')
    return match, values


def update_build_settings():
    if SETTINGS.exists():
        text = SETTINGS.read_text()
    else:
        text = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1045 &1\nEditorBuildSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 2\n  m_Scenes:\n'
    match, old = scene_entries(text)
    game_path = GAME.relative_to(PROJECT).as_posix()
    opening_path = OPENING.relative_to(PROJECT).as_posix()
    values = [(game_path, '1', actual_guid(GAME)), (opening_path, '1', actual_guid(OPENING))]
    seen = {game_path, opening_path}
    for path, enabled, guid in old:
        if path not in seen:
            values.append((path, enabled, guid)); seen.add(path)
    block = ''.join('  - enabled: '+enabled+'\n    path: '+path+'\n    guid: '+guid+'\n' for path, enabled, guid in values)
    replacement = text[:match.start(1)]+block+text[match.end(1):]
    if replacement != text:
        SETTINGS.write_text(replacement, newline='\n')


def check():
    errors = []
    guids = {}
    for path in asset_paths():
        try:
            value = actual_guid(path)
            if value in guids:
                errors.append('Duplicate meta GUID: '+path.relative_to(ROOT).as_posix())
            guids[value] = path
        except ValueError as error:
            errors.append(str(error))
    if not GAME.exists():
        errors.append('Missing campaign scene.')
    elif BOOTSTRAP.exists():
        try:
            if 'guid: '+actual_guid(BOOTSTRAP) not in GAME.read_text():
                errors.append('Campaign scene does not reference its bootstrap script GUID.')
        except ValueError as error:
            errors.append(str(error))
    else:
        errors.append('Missing WhiteGuard.Presentation.WhiteGuardGameBootstrap source file.')
    try:
        _, entries = scene_entries(SETTINGS.read_text())
        expected = [(GAME.relative_to(PROJECT).as_posix(), '1', actual_guid(GAME)),
                    (OPENING.relative_to(PROJECT).as_posix(), '1', actual_guid(OPENING))]
        if entries[:2] != expected:
            errors.append('Build settings must enable campaign first and preserved opening second.')
    except (OSError, ValueError) as error:
        errors.append(str(error))
    return dict(status='FAIL' if errors else 'PASS', asset_guids=len(guids),
                unity_editor_executed=False, errors=errors)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='Check wiring without changing existing files.')
    args = parser.parse_args()
    if not args.check:
        if not BOOTSTRAP.is_file() or not OPENING.is_file():
            raise SystemExit('The authored campaign bootstrap and preserved Opening.unity must exist first.')
        GAME.parent.mkdir(parents=True, exist_ok=True)
        made = sum(ensure_meta(p) for p in asset_paths())
        if not GAME.exists():
            GAME.write_text(scene_text(actual_guid(BOOTSTRAP)), newline='\n')
        made += ensure_meta(GAME)
        # Directory creation and the new scene may add a small number of assets.
        made += sum(ensure_meta(p) for p in asset_paths())
        update_build_settings()
        print(json.dumps(dict(created_meta_files=made, intro_media_replaced=False, player_settings_replaced=False)))
    result = check()
    print(json.dumps(result, ensure_ascii=False, indent=2))
    raise SystemExit(result['status'] != 'PASS')


if __name__ == '__main__':
    main()
