#!/usr/bin/env python3
"""Run the real campaign's portable objective/checkpoint witnesses; never Unity."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default=os.environ.get('DOTNET_BIN') or shutil.which('dotnet'))
parser.add_argument('--report', type=Path, default=ROOT/'unity/Validation/Campaign/domain.json')
args = parser.parse_args()
if not args.dotnet:
    parser.error('Install .NET 10 SDK or pass --dotnet /path/to/dotnet.')
campaign = ROOT/'unity/WhiteGuard/Assets/Resources/WhiteGuardCampaign/campaign.json'
sources = sorted((ROOT/'unity/WhiteGuard/Assets/Scripts/Gameplay/Domain').glob('*.cs'))
sources += [ROOT/'unity/WhiteGuard/Assets/Scripts/Gameplay/CampaignSave.cs']
sources += sorted((ROOT/'unity/Tests/Campaign').glob('*.cs'))
sources += sorted((ROOT/'unity/Packages/com.arkon.runtime/Runtime/Core').rglob('*.cs'))
sources += [ROOT/'unity/Tests/Campaign/Campaign.Tests.csproj', ROOT/'unity/Tests/Campaign/Domain/Campaign.Domain.csproj',
            ROOT/'unity/Tests/Core/Arkon.Core.csproj', campaign]

def records():
    return [dict(path=p.relative_to(ROOT).as_posix(), sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in sources]

before = records()
env = os.environ.copy()
env['DOTNET_NOLOGO'] = '1'
env['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
result = subprocess.run([args.dotnet, 'run', '--project', str(ROOT/'unity/Tests/Campaign/Campaign.Tests.csproj'),
                         '--configuration', 'Release', '--verbosity', 'quiet', '--', str(campaign)],
                        cwd=ROOT, env=env, text=True, capture_output=True)
if result.stderr:
    print(result.stderr, end='')
try:
    report = json.loads(result.stdout)
except json.JSONDecodeError:
    print(result.stdout, end='')
    report = dict(status='FAIL', failures=['The portable campaign runner did not produce a JSON result.'])
stable = before == records()
if result.returncode != 0 or not stable:
    report['status'] = 'FAIL'
report.update(schema=1, campaign_sha256=hashlib.sha256(campaign.read_bytes()).hexdigest(),
              process_exit_code=result.returncode, source_unchanged_during_run=stable,
              unity_editor_executed=False, full_native_campaign_completed=False, files=before)
args.report.parent.mkdir(parents=True, exist_ok=True)
args.report.write_text(json.dumps(report, indent=2, ensure_ascii=False)+'\n')
print(json.dumps({k:v for k,v in report.items() if k != 'files'}, indent=2, ensure_ascii=False))
raise SystemExit(0 if report['status'] == 'PASS' else 1)
