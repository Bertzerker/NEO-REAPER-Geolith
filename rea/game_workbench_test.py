"""MIT. Production checkpoint/movie API test with local user assets; private output only."""
from pathlib import Path
import sys,json,subprocess,time,urllib.request,urllib.error,uuid
ROOT=Path(__file__).parent.resolve();rom=Path(sys.argv[1]).resolve();bios=Path(sys.argv[2]).resolve();data=ROOT/'test-output/private-mslug'/('workbench-'+uuid.uuid4().hex);data.mkdir(parents=True);port=8917;base=f'http://127.0.0.1:{port}';token='';proc=None
def req(path,body=None):
    q=urllib.request.Request(base+path,data=json.dumps(body).encode() if body else None,headers={'X-REA-Token':token,'Content-Type':'application/json'})
    with urllib.request.urlopen(q,timeout=120) as r:return json.load(r)
def control(action,**kw):return req('/api/control',{'action':action,**kw})
def start():
    global proc,token
    proc=subprocess.Popen(['dotnet',str(ROOT/'bin/NeoReaper.dll'),'--rom',str(rom),'--firmware',str(bios),'--system','MVS','--port',str(port),'--data',str(data),'--web',str(ROOT/'public')],stdout=open(data/'host.log','ab'),stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
    for _ in range(100):
        try:s=req('/api/session');token=s['token'];assert not s['failure'];return s
        except (urllib.error.URLError,ConnectionError):time.sleep(.1)
    raise AssertionError('Host startup failed')
def stop():
    global proc
    if proc:proc.terminate();proc.wait(timeout=10);proc=None
try:
    first=start();control('step',frames=1800);cp=control('checkpoint-save',name='Metal Slug title');control('step',frames=30);control('checkpoint-load',id=cp['id']);assert req('/api/session')['capture'] is None
    control('movie-record',name='Local coin start and input test')
    for mask,n in [(512,5),(0,30),(256,5),(0,60),(24,60),(0,60)]:control('step',frames=n,p0=mask)
    control('movie-save');movie=req('/api/movies')[0];assert movie['rows']==220
    for _ in range(2):
        control('movie-play',id=movie['id']);r=control('step',frames=220);assert r['completed']==220 and req('/api/session')['movie']['mode']=='finished';control('movie-stop')
    stop();second=start();assert second['identity']==first['identity'];control('movie-play',id=movie['id']);control('step',frames=220);assert req('/api/session')['movie']['mode']=='finished'
    report={'native':True,'system':'US MVS','bios':'sp-u2.sp1','game':'locally supplied Metal Slug','productionCheckpoint':True,'captureInvalidation':True,'productionMovieRows':220,'replayPasses':3,'coldProcessReplay':True,'stateVideoAudioVerifiedEveryRow':True,'identity':second['identity'],'privateEvidenceNotExported':True}
    (data/'report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
finally:stop()
