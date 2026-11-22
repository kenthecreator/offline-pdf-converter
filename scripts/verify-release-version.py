"""Validate user-visible and assembly version fields before publishing v5.0."""
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
project = ET.parse(root / 'src/OfflinePDFConverter/OfflinePDFConverter.csproj')
for field, expected in [('Version', '5.0.0'), ('AssemblyVersion', '5.0.0.0'), ('FileVersion', '5.0.0.0')]:
    assert project.findtext('.//' + field) == expected, field
assert all(p.text == 'Offline PDF Converter (v5.0)' for p in project.findall('.//Product'))
identity = (root / 'src/OfflinePDFConverter/Services/AppIdentity.cs').read_text(encoding="utf-8")
assert identity.count('WindowTitle = "Offline PDF Converter (v5.0)"') == 2
assert identity.count('HeaderSuffix = " (v5.0)"') == 2
assert 'ランタイムが必要です' not in identity
assert "TITLE = 'Offline PDF Converter (v5.0)'" in (root / 'scripts/build-paddle-edition.py').read_text(encoding="utf-8")
print('v5.0 display names and 5.0.0 assembly metadata verified')
