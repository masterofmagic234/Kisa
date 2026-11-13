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
    vector_art = book['visuals'].get('schema') == 2
    if vector_art:
        art_path=book['visuals'].get('art_path','res://data/intro_art.json')
        book['visuals']['art']=json.loads((ROOT/art_path.removeprefix('res://')).read_text())
    media={}
    for block in book['blocks']:
        entries=[('audio','audio/mpeg')]
        if not vector_art:entries.append(('atlas','image/png'))
        for key,mime in entries:
            name=block[key]
            if name not in media:media[name]=uri(ROOT/name.removeprefix('res://'),mime)
    if not vector_art:
        for shot in book['visuals']['shots']:
            for entry in [shot,*shot['layers']]:
                name=entry['asset']
                if name not in media:media[name]=uri(ROOT/name.removeprefix('res://'),'image/png')
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
