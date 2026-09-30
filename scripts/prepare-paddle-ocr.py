"""Fetch only the pinned models used by PaddleOCR Edition; never sends documents."""
import concurrent.futures
import hashlib
import json
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'src/OfflinePDFConverter/ocr/paddle'
MODELS = {
    'det.onnx': ('PP-OCRv6/det/PP-OCRv6_det_small.onnx', '090f04abcd9d9a7498bc4ebf677e4cb9bdce1fe4197ddb7e529f1ef44e1ff94f'),
    'rec.onnx': ('PP-OCRv6/rec/PP-OCRv6_rec_small.onnx', '6f327246b50388f3c176ae304bd95767ea6dc0c9ae92153ef8cbe210b3c14884'),
    'cls.onnx': ('PP-OCRv4/cls/ch_ppocr_mobile_v2.0_cls_mobile.onnx', 'e47acedf663230f8863ff1ab0e64dd2d82b838fceb5957146dab185a89d6215c'),
}
LICENSES = {
    'PaddleOCR-LICENSE.txt': 'https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/main/LICENSE',
    'RapidOCR-LICENSE.txt': 'https://raw.githubusercontent.com/RapidAI/RapidOCR/v3.9.2/LICENSE',
    'RapidOcrNet-LICENSE.txt': 'https://raw.githubusercontent.com/BobLd/RapidOcrNet/708cae2fcb88720e1d891a81b5ee3e8b2bcc139e/LICENSE.txt',
    'ONNX-Runtime-LICENSE.txt': 'https://raw.githubusercontent.com/microsoft/onnxruntime/v1.29.0/LICENSE',
    'ONNX-Runtime-ThirdPartyNotices.txt': 'https://raw.githubusercontent.com/microsoft/onnxruntime/v1.29.0/ThirdPartyNotices.txt',
    'Clipper2-LICENSE.txt': 'https://raw.githubusercontent.com/AngusJohnson/Clipper2/main/LICENSE',
}

def download(name, url, expected=None):
    path = OUT / name
    if not path.exists() or (expected and hashlib.sha256(path.read_bytes()).hexdigest() != expected):
        temporary = path.with_suffix(path.suffix + '.download')
        with urllib.request.urlopen(url, timeout=180) as response, temporary.open('wb') as stream:
            while block := response.read(1024 * 1024):
                stream.write(block)
        actual = hashlib.sha256(temporary.read_bytes()).hexdigest()
        if expected and actual != expected:
            temporary.unlink()
            raise ValueError(f'Official model hash mismatch: {name}')
        temporary.replace(path)
    print(f'{name}: {path.stat().st_size:,} bytes verified', flush=True)
    return {'file': name, 'url': url, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'bytes': path.stat().st_size}

def fields(data):
    """Read protobuf fields without installing a model runtime or compiling code."""
    def varint(pos):
        value = shift = 0
        while True:
            byte = data[pos]; pos += 1
            value |= (byte & 127) << shift
            if byte < 128: return value, pos
            shift += 7
    pos = 0
    while pos < len(data):
        tag, pos = varint(pos); field, wire = tag >> 3, tag & 7
        if wire == 2:
            size, pos = varint(pos); yield field, data[pos:pos + size]; pos += size
        elif wire == 0: _, pos = varint(pos)
        elif wire == 1: pos += 8
        elif wire == 5: pos += 4
        else: raise ValueError('Unsupported protobuf wire type')

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    base = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/'
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as executor:
        pending = [executor.submit(download, name, base + suffix, sha) for name, (suffix, sha) in MODELS.items()]
        records = [task.result() for task in pending]
    # The ONNX exports carry the exact recognizer dictionary as model metadata.
    metadata = {}
    for field, value in fields((OUT / 'rec.onnx').read_bytes()):
        if field == 14:
            entry = dict(fields(value))
            metadata[entry[1].decode()] = entry[2].decode()
    characters = metadata['character'].split('\n')
    # RapidOcrNet adds the CTC blank and terminal space itself.
    if characters[-1] == ' ': characters.pop()
    if characters[0] in ('blank', '<blank>'): characters.pop(0)
    (OUT / 'keys.txt').write_text('\n'.join(characters) + '\n', encoding='utf-8')
    records.append({'file': 'keys.txt', 'source': 'rec.onnx metadata.character; CTC blank and terminal space supplied by RapidOcrNet',
                    'characters': len(characters), 'sha256': hashlib.sha256((OUT / 'keys.txt').read_bytes()).hexdigest(), 'bytes': (OUT / 'keys.txt').stat().st_size})
    for name, url in LICENSES.items(): records.append(download(name, url))
    contour_url = 'https://raw.githubusercontent.com/BobLd/RapidOcrNet/708cae2fcb88720e1d891a81b5ee3e8b2bcc139e/RapidOcrNet/PContour.cs'
    with urllib.request.urlopen(contour_url, timeout=30) as response:
        header = response.read().decode('utf-8-sig').split('// Ported from')[0]
    contour = OUT / 'PContour-LICENSE.txt'
    contour.write_text('\n'.join(line.removeprefix('//').lstrip() for line in header.splitlines()) + '\n')
    records.append({'file':contour.name,'url':contour_url,'sha256':hashlib.sha256(contour.read_bytes()).hexdigest(),'bytes':contour.stat().st_size})
    notice = ('PaddleOCR Edition uses PP-OCRv6 small detector and recognizer, with the legacy PP-OCRv2 angle classifier.\n'
              'Models: PaddlePaddle/PaddleOCR, distributed as ONNX exports by RapidAI/RapidOCR v3.9.2.\n'
              'RapidOcrNet 4.2.0 (BobLd, RapidOCR), commit 708cae2fcb88720e1d891a81b5ee3e8b2bcc139e.\n'
              'RapidOcrNet is based on RapidOCR and includes code derived from PdfPig and PContour/PContourNet.\n'
              'Inference: Microsoft ONNX Runtime 1.29.0; image processing: SkiaSharp; polygons: Clipper2.\n'
              'See the accompanying license texts and THIRD_PARTY_LICENSES.md.\n')
    (OUT / 'NOTICE.txt').write_text(notice)
    records.append({'file':'NOTICE.txt','sha256':hashlib.sha256((OUT/'NOTICE.txt').read_bytes()).hexdigest(),'bytes':(OUT/'NOTICE.txt').stat().st_size})
    (OUT / 'manifest.json').write_text(json.dumps({'model': 'PP-OCRv6 small', 'runtime': 'RapidOcrNet 4.2.0', 'files': records}, indent=2))
    print('Pinned PaddleOCR Edition resources ready. Dictionary characters:', len(characters), flush=True)

if __name__ == '__main__':
    main()
    import subprocess, sys
    subprocess.run([sys.executable, str(Path(__file__).with_name('prepare-ocr-language-models.py'))], check=True)
