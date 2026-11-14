#!/usr/bin/env python3
"""Run portable C# cases against the checked-in Godot oracle or a fresh one."""
import argparse,os,shutil,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet',default=os.environ.get('DOTNET_BIN') or shutil.which('dotnet'))
parser.add_argument('--oracle',type=Path,default=ROOT/'unity/Tests/Fixtures/godot168-combat.json')
parser.add_argument('--intro-oracle',type=Path,default=ROOT/'unity/Tests/Fixtures/godot-intro-schema3.json')
parser.add_argument('--movement3d-oracle',type=Path,default=ROOT/'unity/Tests/Fixtures/godot168-movement3d.json')
parser.add_argument('--report',type=Path,default=ROOT/'unity/Validation/core-parity.json')
args=parser.parse_args()
if not args.dotnet:parser.error('Install .NET 10 SDK or pass --dotnet /path/to/dotnet.')
env=os.environ.copy();env['ARKON_GODOT_ORACLE']=str(args.oracle.resolve());env['ARKON_GODOT_SOURCE']=str(ROOT/'white_guard')
env['ARKON_INTRO_ORACLE']=str(args.intro_oracle.resolve())
env['ARKON_INTRO_PLAN']=str(ROOT/'white_guard/data/intro_shots.json')
env['ARKON_MOVEMENT3D_ORACLE']=str(args.movement3d_oracle.resolve())
result=subprocess.run([args.dotnet,'run','--project',str(ROOT/'unity/Tests/Parity/Arkon.Parity.csproj'),'--configuration','Release','--',str(args.report.resolve())],cwd=ROOT,env=env)
raise SystemExit(result.returncode)
