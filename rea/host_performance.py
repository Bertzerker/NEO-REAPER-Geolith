"""MIT. 180-second actual native host + lossless WebSocket viewer workload."""
from pathlib import Path
import subprocess,sys,json,time,urllib.request,socket,base64,os,threading,struct,hashlib,uuid
from diagnostic import generate
ROOT=Path(__file__).parent.resolve();duration=float(sys.argv[1]) if len(sys.argv)>1 else 180
port=8915;data=ROOT/'test-output'/('performance-'+uuid.uuid4().hex);data.mkdir(parents=True);fixture=generate() if len(sys.argv)<4 else None;base=f'http://127.0.0.1:{port}'
rom=Path(sys.argv[2]).resolve() if len(sys.argv)>3 else fixture/'original.neo';bios=Path(sys.argv[3]).resolve() if len(sys.argv)>3 else fixture/'original-firmware.zip';system='MVS' if len(sys.argv)>3 else 'AES'
proc=subprocess.Popen(['dotnet',str(ROOT/'bin/NeoReaper.dll'),'--rom',str(rom),'--firmware',str(bios),'--system',system,'--port',str(port),'--data',str(data),'--web',str(ROOT/'public')],stdout=open(data/'host.log','wb'),stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
token='';sock=None;messages=0;received=[];wire=0;done=False
def req(path,body=None):
    r=urllib.request.Request(base+path,data=json.dumps(body).encode() if body else None,headers={'Content-Type':'application/json','X-REA-Token':token})
    with urllib.request.urlopen(r,timeout=5) as f:return json.load(f)
try:
    for _ in range(100):
        try:token=req('/api/session')['token'];break
        except:time.sleep(.1)
    assert token
    sock=socket.create_connection(('127.0.0.1',port),timeout=5)
    key=base64.b64encode(os.urandom(16)).decode();sock.sendall((f'GET /stream HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\nOrigin: {base}\r\n\r\n').encode())
    response=b''
    while not response.endswith(b'\r\n\r\n'):response+=sock.recv(1)
    assert b'101 Switching Protocols' in response,response
    def read(n):
        b=b''
        while len(b)<n:
            part=sock.recv(n-len(b))
            if not part:raise EOFError()
            b+=part
        return b
    def receiver():
        global messages,wire
        assembled=bytearray()
        try:
            while not done:
                h=read(2);n=h[1]&127
                if n==126:n=struct.unpack('>H',read(2))[0]
                elif n==127:n=struct.unpack('>Q',read(8))[0]
                assert n<4*1024*1024
                payload=read(n);wire+=n
                if h[0]&15 in (0,2):assembled.extend(payload)
                if h[0]&128 and assembled:
                    length=struct.unpack('<I',assembled[:4])[0];meta=json.loads(assembled[4:4+length]);received.append((time.perf_counter(),meta['id'],meta['frame']));messages+=1;assembled.clear()
        except (OSError,EOFError):pass
    worker=threading.Thread(target=receiver,daemon=True);worker.start()
    start=req('/api/metrics');t0=time.perf_counter();req('/api/control',{'action':'run'});samples=[]
    while time.perf_counter()-t0<duration:
        time.sleep(min(1,duration-(time.perf_counter()-t0)));samples.append(req('/api/metrics'))
    req('/api/control',{'action':'pause'});elapsed=time.perf_counter()-t0;end=req('/api/metrics');req('/api/control',{'action':'step'})
    report={'native':True,'durationSeconds':elapsed,'sourceHz':end['sourceHz'],'executedFrames':end['executed']-start['executed'],'nativeFps':(end['executed']-start['executed'])/elapsed,'capturedFrames':end['captured']-start['captured'],'capturedFps':(end['captured']-start['captured'])/elapsed,'websocketMessages':messages,'uniqueDeliveredCaptures':len(set(x[1] for x in received)),'deliveredFps':len(set(x[1] for x in received))/elapsed,'bytes':wire,'workingSetMin':min(s['workingSet'] for s in samples),'workingSetMax':max(s['workingSet'] for s in samples),'last30WorkingSetMin':min(s['workingSet'] for s in samples[-30:]),'last30WorkingSetMax':max(s['workingSet'] for s in samples[-30:]),'nativeStepP95Ms':end['nativeStepP95Ms'],'historyCount':end['history'],'identity':req('/api/session')['identity'],'scope':'AES original diagnostic, active SSG, native host and one consuming WebSocket; delivery is not browser GPU presentation. HTTP/native transport is in-process; no separate TCP producer.'}
    assert report['nativeFps']>end['sourceHz']*.95,report
    assert end['history']<=8 and report['workingSetMax']<512*1024*1024,report
    report['captureCopyP95Ms']=end.get('captureCopyP95Ms');report['streamSkippedCaptures']=end.get('streamSkippedCaptures');report['coreDllSha256']=req('/api/session')['manifest']['core'];report['hostSha256']=req('/api/session')['manifest']['host']
    if len(sys.argv)>3:report['scope']='US MVS locally supplied Metal Slug and sp-u2.sp1; one consuming WebSocket; delivery is not browser presentation; private evidence excluded from source/exports'
    (ROOT/('test-output/private-mslug/performance.json' if len(sys.argv)>3 else 'test-output/host-performance.json')).write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
finally:
    done=True
    if sock:sock.close()
    proc.terminate();proc.wait(timeout=10)
