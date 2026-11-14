#!/usr/bin/env python3
"""Type-check adapters against verified official Unity DLLs; never runs Unity."""
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
parser.add_argument('--reference-root', required=True, type=Path,
                    help='Extracted official managed references, including extraction-complete.json.')
parser.add_argument('--report', type=Path, default=ROOT/'unity/Validation/unity-api-compilation.json')
args = parser.parse_args()
if not args.dotnet:
    parser.error('Install .NET 10 SDK or pass --dotnet /path/to/dotnet.')
refs = args.reference_root.resolve()
provenance = json.loads((refs/'extraction-complete.json').read_text())
if provenance.get('status') != 'PASS' or provenance.get('version') != '6000.3.28f1':
    parser.error('Reference extraction must be verified for Unity6000.3.28f1.')
expected_archive = ('ccbe0a59c4fbee234991a841d9506ad8', 4556838576)
if (provenance.get('archive_md5'), provenance.get('archive_bytes')) != expected_archive:
    parser.error('Reference provenance differs from the pinned official archive.')
expected_files = {entry['path']: entry['sha256'] for entry in provenance['files']}
assemblies = sorted((refs/'UnityEngine').glob('*.dll'))
if not assemblies or not (refs/'UnityEngine/UnityEngine.CoreModule.dll').is_file():
    parser.error('Missing modular Unity assemblies.')
for assembly in assemblies:
    relative = assembly.relative_to(refs).as_posix()
    if hashlib.sha256(assembly.read_bytes()).hexdigest() != expected_files.get(relative):
        parser.error('Unity reference SHA-256 mismatch: '+relative)
def source_records():
    files = sorted((ROOT/'unity/Packages/com.arkon.runtime').rglob('*.cs'))
    files += [ROOT/p for p in ['unity/Tests/Core/Arkon.Core.csproj', 'unity/Tests/UnityApi/Arkon.UnityApi.csproj']]
    return [dict(path=p.relative_to(ROOT).as_posix(),sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in files]

before = source_records()
result = subprocess.run([
    args.dotnet, 'build', str(ROOT/'unity/Tests/UnityApi/Arkon.UnityApi.csproj'),
    '--configuration', 'Release', '--no-incremental', '-warnaserror',
    '-p:UnityReferenceRoot='+str(refs)], cwd=ROOT)
stable = before == source_records()
passed = result.returncode == 0 and stable
report = dict(
    status='PASS' if passed else 'FAIL',
    check='Compilation against official Unity managed API assemblies; warnings treated as errors',
    unity_version=provenance['version'], unity_revision=provenance['revision'],
    source_url=provenance['source'], archive_md5=provenance['archive_md5'],
    archive_bytes_verified=provenance['archive_bytes'], reference_assemblies_verified=len(assemblies),
    compiler_exit_code=result.returncode,
    source_unchanged_during_build=stable,
    unity_editor_executed=False, unity_native_runtime_executed=False,
    files=before)
args.report.parent.mkdir(parents=True, exist_ok=True)
args.report.write_text(json.dumps(report, indent=2)+'\n')
print(json.dumps({k:v for k,v in report.items() if k != 'files'}))
raise SystemExit(0 if passed else 1)
