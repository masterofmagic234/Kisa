#!/usr/bin/env python3
"""Package the same intro data, artwork and voice into an offline HTML player."""
import argparse
import base64
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def uri(path, mime):
    return f'data:{mime};base64,' + base64.b64encode(path.read_bytes()).decode()

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('destination', type=Path)
    args=parser.parse_args()
    book=json.loads((ROOT/'data/intro.json').read_text())
    book['visuals']=json.loads((ROOT/book['storyboard'].removeprefix('res://')).read_text())
    media = {}
    for block in book['blocks']:
        path = block['audio']
        media[path] = uri(ROOT / path.removeprefix('res://'), 'audio/mpeg')
    for path, *_ in book['visuals']['art'].values():
        if path not in media:
            media[path] = uri(ROOT / path.removeprefix('res://'), 'image/png')
    media['ambience']=uri(ROOT/'assets/intro/road-ambience.ogg','audio/ogg')
    body=uri(ROOT/'assets/reused/fonts/Manrope-Variable.ttf','font/ttf')
    title=uri(ROOT/'assets/reused/fonts/Forum-Regular.ttf','font/ttf')
    template=(ROOT/'tools/templates/intro-preview.html').read_text()
    stage=(ROOT/'tools/templates/intro-stage.js').read_text()
    result=template.replace('__BODY_FONT__',body).replace('__TITLE_FONT__',title).replace('__INTRO_BOOK__',json.dumps(book,ensure_ascii=False)).replace('__INTRO_MEDIA__',json.dumps(media)).replace('__INTRO_STAGE__',stage)
    args.destination.parent.mkdir(parents=True,exist_ok=True)
    args.destination.write_text(result)
    print(args.destination)

if __name__=='__main__':main()
