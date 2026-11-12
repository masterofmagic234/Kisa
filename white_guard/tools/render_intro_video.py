#!/usr/bin/env python3
"""Render the game's visual storyboard with complete voice and optional subtitles."""
import argparse,json,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('destination',type=Path);ap.add_argument('--scratch',type=Path,required=True);ap.add_argument('--fps',type=int,default=24);args=ap.parse_args()
 args.scratch.mkdir(parents=True,exist_ok=True);args.destination.parent.mkdir(parents=True,exist_ok=True)
 book=json.loads((ROOT/'data/intro.json').read_text());html=args.scratch/'player.html'
 subprocess.run(['python',str(ROOT/'tools/build_intro_preview.py'),str(html)],check=True)
 concat=args.scratch/'voices.txt';concat.write_text(''.join("file '"+str(ROOT/b['audio'].removeprefix('res://'))+"'\n" for b in book['blocks']))
 voice=args.scratch/'voice.mp3';subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-y','-f','concat','-safe','0','-i',str(concat),'-c','copy',str(voice)],check=True)
 def clock(sec):
  v=round(sec*1000);return f'{v//3600000:02d}:{v//60000%60:02d}:{v//1000%60:02d},{v%1000:03d}'
 lines=[];offset=0;n=1
 for b in book['blocks']:
  for cue in b['timed_captions']:lines.append(f'{n}\n{clock(offset+cue["start"])} --> {clock(offset+cue["end"])}\n{cue["text"]}\n');n+=1
  offset+=b['duration']
 srt=args.destination.with_suffix('.srt');srt.write_text('\n'.join(lines))
 subprocess.run(['node',str(ROOT/'tools/render_intro_video.cjs'),str(html),str(voice),str(ROOT/'assets/intro/road-ambience.ogg'),str(srt),str(args.destination),str(args.fps)],check=True)
if __name__=='__main__':main()
