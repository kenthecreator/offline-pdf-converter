#!/usr/bin/env python3
"""Developer-only acquisition of pinned sources. The application never invokes this."""
from pathlib import Path
import hashlib,json,tarfile,urllib.request
root=Path(__file__).resolve().parent.parent
sources=json.loads((root/'vendor/ocr-source-manifest.json').read_text())['sources']
cache=root/'vendor-downloads';cache.mkdir(exist_ok=True)
target=root/'native-build/source';target.mkdir(parents=True,exist_ok=True)
for item in sources:
 archive=cache/item['archive']
 if not archive.exists(): urllib.request.urlretrieve(item['url'],archive)
 if hashlib.sha256(archive.read_bytes()).hexdigest()!=item['sha256']: raise SystemExit('Source checksum mismatch: '+item['archive'])
 if (target/item['directory']).exists(): continue
 with tarfile.open(archive) as tar:
  for member in tar.getmembers():
   path=Path(member.name)
   if path.is_absolute() or '..' in path.parts or member.issym() or member.islnk(): raise SystemExit('Unexpected archive member: '+member.name)
  tar.extractall(target)
 print(item['directory'])
