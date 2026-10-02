"""Build-time only: prepare the pinned Noto Sans Japanese font embedded in release apps."""
import hashlib
import io
import json
import urllib.request
from pathlib import Path
import fontTools
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

ROOT = Path(__file__).resolve().parents[1]
FONTS = ROOT / 'src/OfflinePDFConverter/Assets/Fonts'
URL = 'https://raw.githubusercontent.com/google/fonts/295d98a7a0c17c68f1341eaeea354e7960ea70d3/ofl/notosansjp/NotoSansJP%5Bwght%5D.ttf'
SOURCE_SHA256 = 'c2f3b4d463500a2ddcd3849cded1fceeb9fd6d1c32e6cbecd568453ba50fc68f'

def main():
    records = {x['file']: x for x in json.loads((FONTS / 'manifest.json').read_text())['files']}
    target = FONTS / 'NotoSansJP-Regular.ttf'
    if not target.exists() or hashlib.sha256(target.read_bytes()).hexdigest() != records[target.name]['sha256']:
        if fontTools.__version__ != '4.61.1':
            raise RuntimeError('Install the pinned build dependency: python -m pip install fonttools==4.61.1')
        with urllib.request.urlopen(URL, timeout=120) as response:
            source = response.read(16 * 1024 * 1024)
        if hashlib.sha256(source).hexdigest() != SOURCE_SHA256:
            raise ValueError('Noto Sans JP source hash mismatch')
        font = TTFont(io.BytesIO(source), recalcTimestamp=False)
        instantiateVariableFont(font, {'wght': 400}, inplace=True, updateFontNames=True)
        temporary = target.with_suffix('.tmp')
        font.save(temporary)
        if hashlib.sha256(temporary.read_bytes()).hexdigest() != records[target.name]['sha256']:
            temporary.unlink()
            raise ValueError('Generated Noto Sans JP font hash mismatch')
        temporary.replace(target)
    for name in ['NotoSansJP-Regular.ttf', 'OFL-NotoSansJP.txt']:
        payload = (FONTS / name).read_bytes()
        if len(payload) != records[name]['bytes'] or hashlib.sha256(payload).hexdigest() != records[name]['sha256']:
            raise ValueError('Bundled font resource mismatch: ' + name)
    print('Pinned Noto Sans Japanese font and SIL OFL license verified; no runtime downloads')

if __name__ == '__main__':
    main()
