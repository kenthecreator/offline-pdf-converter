#!/usr/bin/env python3
from pathlib import Path
import hashlib,json,sys,struct
root=Path(__file__).resolve().parent.parent
folder=Path(sys.argv[1])
files=[p for p in folder.rglob('*') if p.is_file()]
assert len(files)==1 and files[0].suffix.lower()=='.exe', [str(p) for p in files]
exe=files[0];data=exe.read_bytes();payload=(root/'src/OfflinePDFConverter/ocr/windows-x64.zip').read_bytes()
assert data[:2]==b'MZ', 'Not a Windows PE executable'
assert payload in data, 'Exact OCR payload was not found in the executable'
pe=struct.unpack_from('<I',data,0x3c)[0]
assert data[pe:pe+4]==b'PE\0\0' and struct.unpack_from('<H',data,pe+4)[0]==0x8664, 'Not Windows x64'
count=struct.unpack_from('<H',data,pe+6)[0]; optional_size=struct.unpack_from('<H',data,pe+20)[0]
section_start=pe+24+optional_size
sections=[];resource_root=None
for i in range(count):
 section=section_start+40*i
 virtual_size,rva,size,offset=struct.unpack_from('<IIII',data,section+8)
 sections.append((rva,size,offset))
 if data[section:section+8].rstrip(b'\0')==b'.rsrc': resource_root=offset
assert resource_root is not None, 'No resource section'
def resource_entries(relative):
 named,ids=struct.unpack_from('<HH',data,resource_root+relative+12)
 return [struct.unpack_from('<II',data,resource_root+relative+16+i*8) for i in range(named+ids)]
versions=[]
for kind,relative in resource_entries(0):
 if kind != 16: continue  # RT_VERSION, excluding unrelated resources containing version-shaped bytes.
 for name,next_relative in resource_entries(relative&0x7fffffff):
  for language,leaf in resource_entries(next_relative&0x7fffffff):
   rva,size=struct.unpack_from('<II',data,resource_root+leaf)
   for base,length,offset in sections:
    if base<=rva<base+length:
     version_data=data[offset+rva-base:offset+rva-base+size]
     signature=version_data.find(bytes.fromhex('bd04effe'))
     assert signature>=0, 'No VS_FIXEDFILEINFO'
     ms,ls=struct.unpack_from('<II',version_data,signature+8)
     versions.append(f'{ms>>16}.{ms&65535}.{ls>>16}.{ls&65535}')
version=versions[0] if len(versions)==1 else None
assert version=='3.2.0.0', f'Wrong PE file version: {version}'
print(json.dumps({'file':exe.name,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'singleFile':True,'exactOcrPayloadEmbedded':True,'fileVersion':'3.2.0.0'},indent=2))
