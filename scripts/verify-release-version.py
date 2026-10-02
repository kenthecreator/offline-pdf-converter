"""Validate user-visible and assembly version fields before publishing v4.3."""
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
project = ET.parse(root / 'src/OfflinePDFConverter/OfflinePDFConverter.csproj')
for field, expected in [('Version', '4.3.0'), ('AssemblyVersion', '4.3.0.0'), ('FileVersion', '4.3.0.0')]:
    assert project.findtext('.//' + field) == expected, field
assert all(p.text == 'Offline PDF Converter (v4.3)' for p in project.findall('.//Product'))
identity = (root / 'src/OfflinePDFConverter/Services/AppIdentity.cs').read_text(encoding="utf-8")
assert identity.count('WindowTitle = "Offline PDF Converter (v4.3)"') == 2
assert identity.count('HeaderSuffix = " (v4.3)"') == 2
assert 'ランタイムが必要です' not in identity
assert "TITLE = 'Offline PDF Converter (v4.3)'" in (root / 'scripts/build-paddle-edition.py').read_text(encoding="utf-8")
print('v4.3 display names and 4.3.0 assembly metadata verified')
