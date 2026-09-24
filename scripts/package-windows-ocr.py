#!/usr/bin/env python3
"""Package the locally built static engine and models; no runtime downloads."""
from pathlib import Path
import hashlib,json,re,subprocess,zipfile
root=Path(__file__).resolve().parent.parent
engine=root/'native-build/tesseract/bin/tesseract.exe'
imports=subprocess.check_output(['x86_64-w64-mingw32-objdump','-p',str(engine)],text=True)
dlls=re.findall(r'DLL Name: (\S+)',imports)
assert dlls and all(d.lower()=='kernel32.dll' or d.lower().startswith('api-ms-win-crt-') for d in dlls), dlls
files={'tesseract.exe':engine.read_bytes()}
for p in sorted((root/'src/OfflinePDFConverter/ocr/tessdata').glob('*.traineddata')):
 files['tessdata/'+p.name]=p.read_bytes()
for p in sorted((root/'vendor/ocr-licenses').glob('*')):
 files['licenses/'+p.name]=p.read_bytes()
files['licenses/tessdata-LICENSE.txt']=(root/'src/OfflinePDFConverter/ocr/tessdata/LICENSE').read_bytes()
files['provenance.json']=(root/'vendor/ocr-source-manifest.json').read_bytes()
files['windows-imports.json']=json.dumps(dlls,indent=2).encode()
files['manifest.json']=json.dumps({name:hashlib.sha256(data).hexdigest() for name,data in files.items()},sort_keys=True,indent=2).encode()
archive=root/'src/OfflinePDFConverter/ocr/windows-x64.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
 for name,data in sorted(files.items()):
  entry=zipfile.ZipInfo(name,date_time=(2026,9,5,0,0,0));entry.compress_type=zipfile.ZIP_DEFLATED
  z.writestr(entry,data)
digest=hashlib.sha256(archive.read_bytes()).hexdigest()
archive.with_suffix('.sha256').write_text(digest+'\n')
print(json.dumps({'files':len(files),'bytes':archive.stat().st_size,'sha256':digest,'systemImports':dlls},indent=2))
