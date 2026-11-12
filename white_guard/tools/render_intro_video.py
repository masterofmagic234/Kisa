#!/usr/bin/env python3
"""Render a viewing copy from the intro's shared artwork, audio and word cues.

Requires ffmpeg; does not alter source images. Godot remains the game player.
"""
import argparse
import json
import subprocess
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]

def stamp(seconds):
    centiseconds=round(seconds*100)
    return f'{centiseconds//360000}:{centiseconds//6000%60:02d}:{centiseconds//100%60:02d}.{centiseconds%100:02d}'

def run(args):
    subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-y',*args],check=True)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('destination',type=Path)
    parser.add_argument('--scratch',type=Path,required=True)
    args=parser.parse_args();args.destination.parent.mkdir(parents=True,exist_ok=True);args.scratch.mkdir(parents=True,exist_ok=True)
    book=json.loads((ROOT/'data/intro.json').read_text())
    fonts=ROOT/'assets/reused/fonts'
    header='''[Script Info]
ScriptType: v4.00+
PlayResX: 1280
PlayResY: 720
WrapStyle: 0
[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Caption,Manrope,35,&H00D5E7EE,&H00D5E7EE,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,46,46,550,1
Style: Speaker,Manrope,18,&H0099ADA5,&H0099ADA5,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,46,46,526,1
Style: Title,Forum,64,&H00D5E7EE,&H00D5E7EE,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,5,40,40,0,1
Style: Kicker,Forum,20,&H00D5E7EE,&H00D5E7EE,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,5,40,40,0,1
[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
'''
    clips=[]
    for i,b in enumerate(book['blocks']):
        output=args.scratch/f'clip-{i+1:02d}.mp4';ass=args.scratch/f'clip-{i+1:02d}.ass'
        lines=[header,f'Dialogue: 0,0:00:00.00,{stamp(b["duration"])},Speaker,,0,0,0,,МАЗУР\n']
        for cue in b['timed_captions']:
            text=cue['text'].replace('\\','\\\\').replace('{','\\{').replace('}','\\}')
            lines.append(f'Dialogue: 0,{stamp(cue["start"])},{stamp(cue["end"])},Caption,,0,0,0,,{text}\n')
        ass.write_text(''.join(lines))
        quadrant=b['quadrant'];x=quadrant%2;y=quadrant//2
        picture=ROOT/b['atlas'].removeprefix('res://');voice=ROOT/b['audio'].removeprefix('res://')
        filter_=(f'[0:v]crop=iw/2:ih/2:{x}*iw/2:{y}*ih/2,scale=1280:854,crop=1280:720,'
                 f"zoompan=z='1+0.035*on/({b['duration']}*24)':x='iw/2-iw/zoom/2':y='ih/2-ih/zoom/2':d=1:s=1280x720:fps=24,"
                 f'fade=t=in:d=0.3,drawbox=x=0:y=510:w=iw:h=210:color=0x0e110e@0.96:t=fill,'
                 f'ass={ass}:fontsdir={fonts},format=yuv420p[v];'
                 '[2:a]volume=0.045[road];[1:a][road]amix=inputs=2:duration=first:normalize=0,alimiter=limit=0.95[a]')
        run(['-loop','1','-framerate','24','-i',str(picture),'-i',str(voice),'-stream_loop','-1','-i',str(ROOT/'assets/intro/road-ambience.ogg'),'-filter_complex',filter_,'-map','[v]','-map','[a]','-t',str(b['duration']),'-c:v','libx264','-preset','veryfast','-crf','24','-threads','2','-c:a','aac','-b:a','96k','-ar','48000','-ac','2',str(output)])
        clips.append(output);print(f'VIDEO {i+1:02d}/{len(book["blocks"])}',flush=True)
    ending=args.scratch/'ending.mp4';ass=args.scratch/'ending.ass'
    ass.write_text(header+'Dialogue: 0,0:00:00.00,0:00:03.00,Title,,0,0,0,,КОРТЕЖ\n'+'Dialogue: 0,0:00:00.00,0:00:03.00,Kicker,,0,0,0,,{\\pos(640,275)}ГЛАВА ПЕРВАЯ\n')
    run(['-f','lavfi','-i','color=c=0x151915:s=1280x720:r=24:d=3','-f','lavfi','-i','anullsrc=r=48000:cl=stereo','-vf',f'ass={ass}:fontsdir={fonts}','-t','3','-c:v','libx264','-preset','veryfast','-crf','24','-threads','2','-c:a','aac','-b:a','96k','-ar','48000','-ac','2',str(ending)])
    clips.append(ending)
    concat=args.scratch/'clips.txt';concat.write_text(''.join("file '"+str(p)+"'\n" for p in clips))
    run(['-f','concat','-safe','0','-i',str(concat),'-c','copy','-movflags','+faststart',str(args.destination)])
    print(args.destination,flush=True)

if __name__=='__main__':main()
