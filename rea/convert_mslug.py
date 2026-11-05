"""MIT. Local-only, checksum-verified Metal Slug set conversion; no downloads.
Mapping reference: mamedev/mame src/mame/snk/neogeo.cpp ROM_START(mslug).
Original archives remain unchanged. Output must remain in ignored local storage.
"""
from pathlib import Path
import zipfile,hashlib,struct,json,sys,zlib
EXPECTED={
 'p1':(2097152,'b888993dbb7e9f0a28a01d7d2e1da00ef9cf6f38'),
 's1':(131072,'550b53628daec9f1e1e11a398854092d90f9505a'),
 'm1':(131072,'fd75bd15aed30266a8b3775f276f997af57d1c06'),
 'v1':(4194304,'cd076928468ad6bcc5f19f88cb843ecb5e660681'),
 'v2':(4194304,'5f79ea9286d22ed208128f9c31ca75552ce08b57'),
 'c1':(4194304,'7b045d1a48980cb1a140699011cb1a3d4acdc4d1'),
 'c2':(4194304,'cb7254b885989223bba597b8ff0972dfa5957816'),
 'c3':(4194304,'0a7a27d603d1bb2520b5570ebf5b34a106e255a6'),
 'c4':(4194304,'4e94fda8ee63abf0f92afe08060a488546e5c280')}
def convert(source,output):
 source,output=Path(source),Path(output);assert source.resolve()!=output.resolve()
 parts={};mapping=[]
 with zipfile.ZipFile(source) as z:
  assert len(z.infolist())<1000 and sum(e.file_size for e in z.infolist())<128*1024*1024
  for key,(size,sha1) in EXPECTED.items():
   matches=[n for n in z.namelist() if Path(n).name.lower().startswith('201-'+key+'.')]
   assert len(matches)==1, f'Missing/ambiguous 201-{key} component'
   n=matches[0];b=z.read(n);assert len(b)==size,(n,len(b),size);original=hashlib.sha256(b).hexdigest();transformation='none'
   if hashlib.sha1(b).hexdigest()!=sha1 and key.startswith('c'):
    b=b[size//2:]+b[:size//2];transformation='swap two equal physical C-ROM halves (legacy layout)'
   assert hashlib.sha1(b).hexdigest()==sha1, f'Unrecognized bytes for {n}; no guessed conversion performed'
   parts[key]=b;mapping.append({'entry':n,'bytes':size,'sourceSha256':original,'verifiedCanonicalSha1':sha1,'transformation':transformation})
 p=parts['p1'][1048576:]+parts['p1'][:1048576]
 c=bytearray(16*1024*1024)
 for base,odd,even in [(0,'c1','c2'),(8*1024*1024,'c3','c4')]:c[base:base+8*1024*1024:2]=parts[odd];c[base+1:base+8*1024*1024:2]=parts[even]
 regions=[p,parts['s1'],parts['m1'],parts['v1']+parts['v2'],b'',c]
 h=bytearray(4096);h[:4]=b'NEO\x01';struct.pack_into('<6I',h,4,*map(len,regions));struct.pack_into('<I',h,28,1996);struct.pack_into('<I',h,40,0x201);h[44:54]=b'Metal Slug';h[77:82]=b'Nazca'
 result=h+b''.join(regions);output.parent.mkdir(parents=True,exist_ok=True)
 if output.exists():assert output.read_bytes()==result,'Refusing to overwrite a different existing derivative'
 else:output.write_bytes(result)
 manifest={'source':source.name,'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'output':output.name,'outputSha256':hashlib.sha256(result).hexdigest(),'regions':[len(r) for r in regions],'components':mapping,'mapping':'P: swap 1 MiB halves, retain disk word swap; C: byte-interleave canonical pairs; V: concatenate V1/V2 into ADPCM-A region, V2 alias; S/M direct','reference':'https://github.com/mamedev/mame/blob/master/src/mame/snk/neogeo.cpp'}
 output.with_suffix('.conversion.json').write_text(json.dumps(manifest,indent=2));print(json.dumps({'converted':str(output),'bytes':len(result),'allCanonicalHashesVerified':True}));return output
if __name__=='__main__':convert(sys.argv[1],sys.argv[2])
