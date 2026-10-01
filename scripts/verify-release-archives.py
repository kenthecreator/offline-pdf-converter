"""Verify exact release ZIP contents and checksums before making a release public."""
import hashlib
import plistlib
import sys
import zipfile
from pathlib import Path

folder = Path(sys.argv[1])
for suffix, executable in [('Windows-x64', 'Offline PDF Converter (v4.1).exe'),
                           ('macOS-arm64', 'Offline PDF Converter (v4.1).app')]:
    name = f'Offline PDF Converter (v4.1)-{suffix}.zip'
    matches = list(folder.rglob(name))
    assert len(matches) == 1, f'Missing or duplicate archive: {name}'
    archive = matches[0]
    expected = archive.with_suffix('.zip.sha256.txt').read_text().split()[0]
    assert hashlib.sha256(archive.read_bytes()).hexdigest() == expected, f'ZIP hash mismatch: {name}'
    with zipfile.ZipFile(archive) as bundle:
        assert bundle.testzip() is None, f'Corrupt archive: {name}'
        names = bundle.namelist()
        assert not any('v4.0' in n for n in names), 'Stale package name'
        assert 'RELEASE_DETAILS_v4.1.0.md' in names
        if suffix == 'Windows-x64':
            assert executable in names
            assert [n for n in names if n.endswith('.exe')] == [executable]
            assert not any(n.lower().endswith('.dll') for n in names), 'Loose DLLs in single-exe package'
        else:
            info = plistlib.loads(bundle.read(executable + '/Contents/Info.plist'))
            assert info['CFBundleName'] == info['CFBundleDisplayName'] == 'Offline PDF Converter (v4.1)'
            assert info['CFBundleVersion'] == info['CFBundleShortVersionString'] == '4.1.0'
    print('Verified release package:', name)
