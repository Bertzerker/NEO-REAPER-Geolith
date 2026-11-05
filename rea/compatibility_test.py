"""MIT. User-supplied assets only. Never exports or commits game evidence."""
from native_test import *
import subprocess,struct,zlib,binascii
out=ROOT/'test-output'/'private-mslug';out.mkdir(parents=True,exist_ok=True)
rom=Path(sys.argv[1]).resolve();bios=Path(sys.argv[2]).resolve()
l=load(1,rom,bios)
if len(sys.argv)>3 and sys.argv[3]=='cold':
    a=(out/'checkpoint.bin').read_bytes();assert l.nr_restore(a,len(a),1800)
    rows=json.loads((out/'rows.json').read_text())
    for row in rows:
        l.nr_step(row['p0'],0)
        assert sha(state(l))==row['state'] and sha(video(l))==row['video'] and sha(audio(l))==row['audio'],('cold divergence',row['row'])
    print('Cold replay passed',flush=True);sys.exit(0)
for _ in range(1800):l.nr_step(0,0)
before=state(l);(out/'checkpoint.bin').write_bytes(before)
def png(raw,path,opaque=False):
    def chunk(t,d):return struct.pack('>I',len(d))+t+d+struct.pack('>I',binascii.crc32(t+d)&0xffffffff)
    rows=[]
    for y in range(24,248):
        r=bytearray([0])
        for x in range(8,312):
            b,g,red,a=raw[(y*320+x)*4:(y*320+x)*4+4];r.extend([red,g,b,255 if opaque else a])
        rows.append(r)
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',304,224,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(b''.join(rows)))+chunk(b'IEND',b''))
for plane,name in enumerate(['composite','sprites','fix']):png(video(l,plane),out/(name+'.png'),plane==0)
rows=[];on_cost=[];off_cost=[]
# Coin, release, Start, release, right+A, release.
for p0 in [512]*5+[0]*30+[256]*5+[0]*60+[24]*60+[0]*60:
    t=time.perf_counter();l.nr_step(p0,0);on_cost.append((time.perf_counter()-t)*1000);rows.append({'row':len(rows),'p0':p0,'state':sha(state(l)),'video':sha(video(l)),'audio':sha(audio(l))})
(out/'rows.json').write_text(json.dumps(rows))
for plane,name in enumerate(['after-input','after-input-sprites','after-input-fix']):png(video(l,plane),out/(name+'.png'),plane==0)
assert l.nr_restore(before,len(before),1800)
l.nr_observer(0)
for row in rows:
    t=time.perf_counter();l.nr_step(row['p0'],0);off_cost.append((time.perf_counter()-t)*1000);assert sha(state(l))==row['state'] and sha(video(l))==row['video'] and sha(audio(l))==row['audio'],('observer divergence',row['row'])
subprocess.run([sys.executable,__file__,str(rom),str(bios),'cold'],check=True)
report={'system':'MVS','region':'US','biosEntry':'sp-u2.sp1','cartridge':rom.name,'romSha256':sha(rom.read_bytes()),'firmwareZipSha256':sha(bios.read_bytes()),'coreDllSha256':sha((ROOT/'bin'/'neo-reaper-core.dll').read_bytes()),'framesBooted':1800,'inputRows':len(rows),'observerStateVideoAudioEquality':True,'coldReplay':True,'scope':'Attract-mode boot plus bounded coin/start/right+A sequence; no full-game compatibility claim; evidence remains private/ignored'}
report['observerOnMeanStepMs']=sum(on_cost)/len(on_cost);report['observerOffMeanStepMs']=sum(off_cost)/len(off_cost)
report['observerOnP95StepMs']=sorted(on_cost)[int(.95*(len(on_cost)-1))];report['observerOffP95StepMs']=sorted(off_cost)[int(.95*(len(off_cost)-1))]
report['observerMeanOverheadPercent']=(report['observerOnMeanStepMs']/report['observerOffMeanStepMs']-1)*100
(out/'report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
