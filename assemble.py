from pathlib import Path
import hashlib,json
manifest=json.loads(Path('delivery-ready.json').read_text())
dist=Path('dist');dist.mkdir(exist_ok=True)
for file in manifest['files']:
    target=dist/file['name']
    with target.open('wb') as output:
        for part in file['parts']:
            output.write(Path(part).read_bytes())
    assert target.stat().st_size==file['bytes'],file['name']
    assert hashlib.sha256(target.read_bytes()).hexdigest()==file['sha256'],file['name']
sums=''.join(file['sha256']+'  '+file['name']+'\n' for file in manifest['files'])
(dist/'SHA256SUMS.txt').write_text(sums)
print('All delivery files assembled and SHA-256 verified')
