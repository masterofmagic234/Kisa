#!/usr/bin/env python3
"""Build original shared vector art and its voiced director's timeline."""
import json
import sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools/art'))
from world_scenes import build_scenes as world_scenes
from character_scenes import build_scenes as character_scenes

def main():
    scenes=world_scenes();scenes.update(character_scenes())
    art=dict(schema=1,width=1280,height=720,
             authorship='Original authored polygon geometry, articulated figure poses and perspective projection; no copied game frames or extracted models.',
             scenes=scenes)
    destination=ROOT/'data/intro_art.json'
    destination.write_text(json.dumps(art,ensure_ascii=False,separators=(',',':'))+'\n')
    direction=json.loads((ROOT/'data/intro_direction.json').read_text())
    missing=set(direction['scene_ids'])-set(scenes)
    if missing:raise ValueError('Missing authored scenes: '+str(sorted(missing)))
    direction.update(schema=2,captions_default=False,controls_idle_seconds=2.25,
                     source_text_unchanged=True,art_path='res://data/intro_art.json')
    (ROOT/'data/intro_shots.json').write_text(json.dumps(direction,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps(dict(scenes=len(scenes),compositions=len(direction['shots']),bytes=destination.stat().st_size)))

if __name__=='__main__':main()
