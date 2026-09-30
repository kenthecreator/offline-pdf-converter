"""Add pinned Japanese/English recognizers; no documents are sent."""
import hashlib,json,sys
from pathlib import Path
from importlib.util import spec_from_file_location,module_from_spec
spec=spec_from_file_location('models',Path(__file__).with_name('prepare-paddle-ocr.py'));m=module_from_spec(spec);spec.loader.exec_module(m)
manifest=m.OUT/'manifest.json';data=json.loads(manifest.read_text());new=[]
base='https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv4/rec/'
for lang,name,digest in [('jpn','japan_PP-OCRv4_rec_mobile.onnx','e1075a67dba758ecfc7ebc78a10ae61c95ac8fb66a9c86fab5541e33f085cb7a'),('eng','en_PP-OCRv4_rec_mobile.onnx','e8770c967605983d1570cdf5352041dfb68fa0c21664f49f47b155abd3e0e318')]:
 filename='rec-'+lang+'.onnx';new.append(m.download(filename,base+name,digest));metadata={}
 for field,value in m.fields((m.OUT/filename).read_bytes()):
  if field==14:
   entry=dict(m.fields(value));metadata[entry[1].decode()]=entry[2].decode()
 chars=metadata['character'].split('\n')
 if chars[-1]==' ':chars.pop()
 if chars[0] in ('blank','<blank>'):chars.pop(0)
 keys=m.OUT/('keys-'+lang+'.txt');keys.write_text('\n'.join(chars)+'\n',encoding='utf-8')
 new.append({'file':keys.name,'source':filename+' metadata.character','sha256':hashlib.sha256(keys.read_bytes()).hexdigest(),'bytes':keys.stat().st_size})
new_names={r['file'] for r in new};data['files']=[r for r in data['files'] if r['file'] not in new_names]+new
manifest.write_text(json.dumps(data,indent=2)+'\n')
