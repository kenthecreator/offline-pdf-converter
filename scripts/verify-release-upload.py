"""Verify an unpublished release's commit and exact uploaded bytes before publication."""
import hashlib
import json
import sys
from pathlib import Path

release = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
folder = Path(sys.argv[2])
commit = sys.argv[3]
assert release['tag_name'] == 'v4.2.0' and release['draft'], 'Expected the new v4.2 draft'
assert release['target_commitish'] == commit, 'Draft targets a different source commit'
expected = {p.name: 'sha256:' + hashlib.sha256(p.read_bytes()).hexdigest()
            for p in folder.iterdir() if p.is_file()}
assets = release['assets']
actual = {a['name']: a['digest'] for a in assets}
assert len(assets) == len(actual) and actual == expected, 'Uploaded files differ from verified artifacts'
reports = list(folder.glob('verification-*.json'))
assert {p.name for p in reports} == {
    'verification-windows2022-features.json', 'verification-windows2022-gui.json',
    'verification-windows2025-features.json', 'verification-windows2025-gui.json'
}, 'Missing Windows verification evidence'
for path in reports:
    report = json.loads(path.read_text(encoding='utf-8-sig'))
    assert report['passed'] and report['checks'] and report['freshBundleExtraction'], path
    assert report['networkIsolation'] == 'Windows Firewall: tested exe blocked inbound and outbound on all profiles', path
print('Verified draft commit, uploaded hashes and Windows offline test evidence')
