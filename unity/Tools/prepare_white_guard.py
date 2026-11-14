#!/usr/bin/env python3
"""Prepare the Unity project from the approved, unchanged Godot intro assets."""
from pathlib import Path
import hashlib
import json
import shutil
import struct
import uuid

ROOT = Path(__file__).resolve().parents[2]
UNITY = ROOT / 'unity'
PROJECT = UNITY / 'WhiteGuard'
PACKAGE = UNITY / 'Packages/com.arkon.runtime'
GUID_NAMESPACE = uuid.UUID('148da6b4-dd8f-4fc4-9fac-eaa35141a82e')

def guid(path):
    return uuid.uuid5(GUID_NAMESPACE, str(path.relative_to(UNITY))).hex

def meta(path, folder=False):
    target = Path(str(path)+'.meta')
    if not target.exists():
        target.write_text('fileFormatVersion: 2\nguid: '+guid(path)+'\n'+('folderAsset: yes\n' if folder else ''))

def main():
    resources = PROJECT / 'Assets/Resources/WhiteGuardIntro'
    for directory in [resources/'audio', resources/'fonts', PROJECT/'Assets/Scenes', PROJECT/'Packages', PROJECT/'ProjectSettings']:
        directory.mkdir(parents=True, exist_ok=True)
    source = ROOT/'white_guard'
    entries = [(source/'data/intro.json', resources/'intro.json'), (source/'data/intro_shots.json', resources/'intro_shots.json')]
    entries += [(p,resources/p.name) for p in sorted((source/'assets/intro').glob('*.png'))]
    entries += [(p,resources/'audio'/p.name) for p in sorted((source/'assets/intro/audio').glob('*.mp3'))]
    entries += [(source/'assets/intro/road-ambience.ogg', resources/'road-ambience.ogg')]
    entries += [(source/'assets/reused/fonts'/name, resources/'fonts'/name) for name in ['Manrope-Variable.ttf','Forum-Regular.ttf','Manrope-OFL.txt','Forum-OFL.txt','FONT_NOTICE.md']]
    records=[]
    for original, destination in entries:
        shutil.copyfile(original,destination)
        data=destination.read_bytes()
        assert data==original.read_bytes()
        record=dict(source=str(original.relative_to(ROOT)),target=str(destination.relative_to(ROOT)),bytes=len(data),sha256=hashlib.sha256(data).hexdigest())
        if destination.suffix=='.png': record['width'],record['height']=struct.unpack('>II',data[16:24])
        records.append(record)
    (PROJECT/'Assets/original-media-manifest.json').write_text(json.dumps(dict(schema=1,source_art_baseline='8207adbe7fddb7f6166f68a0300783e1f00b62c7',art_redesigned=False,direction_reworked=False,files=records),ensure_ascii=False,indent=2)+'\n')
    manifest={'dependencies':{'com.arkon.runtime':'file:../../Packages/com.arkon.runtime',**{'com.unity.modules.'+name:'1.0.0' for name in ['audio','imgui','jsonserialize','physics','physics2d','ui','video']}}}
    (PROJECT/'Packages/manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    (PROJECT/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.3.28f1\nm_EditorVersionWithRevision: 6000.3.28f1 (8f082d2fe476)\n')
    (PROJECT/'ProjectSettings/TimeManager.asset').write_text('''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!5 &1
TimeManager:
  m_ObjectHideFlags: 0
  Fixed Timestep: 0.016666668
  Maximum Allowed Timestep: 0.33333334
  m_TimeScale: 1
  Maximum Particle Timestep: 0.03
''')
    (PROJECT/'ProjectSettings/ProjectSettings.asset').write_text('''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 27
  companyName: masterofmagic234
  productName: White Guard
  defaultScreenWidth: 1280
  defaultScreenHeight: 720
  runInBackground: 0
  fullscreenMode: 3
  activeInputHandler: 0
''')
    bootstrap=PACKAGE/'Runtime/Unity/WhiteGuardBootstrap.cs'
    (PROJECT/'Assets/Scenes/Opening.unity').write_text('''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &1000
GameObject:
  m_ObjectHideFlags: 0
  m_Component:
  - component: {fileID: 1001}
  - component: {fileID: 1002}
  m_Layer: 0
  m_Name: White Guard Opening
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
  m_Script: {fileID: 11500000, guid: '''+guid(bootstrap)+''', type: 3}
  m_Name:
  m_EditorClassIdentifier:
''')
    (PROJECT/'ProjectSettings/EditorBuildSettings.asset').write_text('''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1045 &1
EditorBuildSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Scenes:
  - enabled: 1
    path: Assets/Scenes/Opening.unity
    guid: '''+guid(PROJECT/'Assets/Scenes/Opening.unity')+'''
''')
    # Stable GUIDs are generated for our files, never copied from Godot UIDs.
    for root in [PACKAGE,PROJECT/'Assets']:
        for path in sorted(root.rglob('*')):
            if path.suffix=='.meta' or '__pycache__' in path.parts: continue
            meta(path,path.is_dir())
    print(json.dumps(dict(project=str(PROJECT),unchanged_media_files=len(records),unity_editor_verified=False)))

if __name__=='__main__': main()
