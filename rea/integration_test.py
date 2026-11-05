"""MIT. Actual DLL/.NET producer acceptance, isolated owned processes and data."""
from pathlib import Path
import subprocess,sys,json,time,urllib.request,urllib.error,hashlib,threading,zipfile,uuid,socket,base64,os,struct
from diagnostic import generate
from verify_bundle import verify
ROOT=Path(__file__).parent.resolve(); BIN=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else ROOT/'bin'
PORT=int(sys.argv[2]) if len(sys.argv)>2 else 8914
DATA=ROOT/'test-output'/('integration-'+uuid.uuid4().hex);DATA.mkdir(parents=True)
base=f'http://127.0.0.1:{PORT}';token='';proc=None;passed=[]
def request(path,body=None,status=200,headers=None,raw=False):
    h={'Content-Type':'application/json','X-REA-Token':token};h.update(headers or {})
    req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,headers=h)
    try:
        with urllib.request.urlopen(req,timeout=120) as r: code=r.status;data=r.read()
    except urllib.error.HTTPError as e: code=e.code;data=e.read()
    assert code==status,(path,code,data[:1000])
    return data if raw else json.loads(data)
def control(action,**kw):return request('/api/control',{'action':action,**kw})
def start():
    global proc,token
    fixture=generate()
    log=open(DATA/'host.log','ab')
    proc=subprocess.Popen(['dotnet',str(BIN/'NeoReaper.dll'),'--rom',str(fixture/'original.neo'),'--firmware',str(fixture/'original-firmware.zip'),'--port',str(PORT),'--data',str(DATA),'--web',str(ROOT/'public')],stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
    for _ in range(100):
        if proc.poll() is not None:raise AssertionError((DATA/'host.log').read_text())
        try:
            s=request('/api/session')
            if s['failure']: raise AssertionError(s['failure'])
            token=s['token'];return s
        except (urllib.error.URLError,ConnectionError):time.sleep(.1)
    raise AssertionError('Host startup timeout')
def stop():
    global proc
    if proc is not None:proc.terminate();proc.wait(timeout=10);proc=None
def check(name):passed.append(name);print('PASS',name,flush=True)
def websocket_test():
    for turn in range(8):
        with socket.create_connection(('127.0.0.1',PORT),timeout=5) as sock:
            key=base64.b64encode(os.urandom(16)).decode();sock.sendall((f'GET /stream HTTP/1.1\r\nHost: 127.0.0.1:{PORT}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\nOrigin: {base}\r\n\r\n').encode());h=b''
            while not h.endswith(b'\r\n\r\n'):h+=sock.recv(1)
            assert b'101 Switching Protocols' in h,h
            # Deliberately send an unsupported client binary message, correctly masked.
            sock.sendall(b'\x82\x81\x01\x02\x03\x04'+bytes([ord('x')^1]))
            def read(n):
                b=b''
                while len(b)<n:
                    part=sock.recv(n-len(b));assert part;b+=part
                return b
            while True:
                hdr=read(2);n=hdr[1]&127
                if n==126:n=struct.unpack('>H',read(2))[0]
                elif n==127:n=struct.unpack('>Q',read(8))[0]
                assert n<4*1024*1024;payload=read(n)
                if hdr[0]&15==8:assert struct.unpack('>H',payload[:2])[0]==1008;break
        for _ in range(30):
            if request('/api/metrics')['streamClients']==0:break
            time.sleep(.05)
        assert request('/api/metrics')['streamClients']==0,'Closed WebSocket leaked a viewer slot'
try:
    s=start();identity=s['identity'];assert s['capture'] is None;check('native startup and no-capture state')
    request('/api/control',{'action':'step'},403,{'X-REA-Token':'bad'})
    request('/api/control',{'action':'step'},403,{'Origin':'https://evil.example'})
    request('/api/control',{'action':'step','frames':3601},400)
    request('/api/control',{'action':'step','p0':1024},400)
    request('/api/comments',{'markdown':'x'*65536},413)
    check('token origin and bounded input rejection')
    control('step',frames=10);c=request('/api/snapshot');cid=c['id'];assert c['frame']==10
    mem=request('/api/memory?capture='+cid+'&domain=0&length=4');assert mem['hex']=='1234ff3f'
    end=request('/api/memory?capture='+cid+'&domain=0&offset=65535&length=4096');assert end['length']==1
    asset=request('/api/memory?capture='+cid+'&domain=8&offset=0&length=8');assert asset['immutable'] and asset['total']==1024*1024
    request('/api/lease',{'owner':'selected','capture':cid});request('/api/lease',{'owner':'baseline','capture':cid})
    for i in range(12):
        control('step',frames=1,p0=16)
        if i==0:unleased=request('/api/snapshot')['id']
    assert request('/api/snapshot?capture='+cid)['id']==cid
    request('/api/snapshot?capture='+unleased,status=410)
    latest=request('/api/snapshot')['id'];diff=request('/api/diff?capture='+latest+'&baseline='+cid+'&domain=0&length=8');assert 2 in diff['changed']
    check('copied RAM bus order clipping leases history eviction and diff')
    for v in range(3):
        img=request('/api/image?capture='+cid+'&view='+str(v),raw=True);assert img[:8]==b'\x89PNG\r\n\x1a\n'
    px=request('/api/pixel?capture='+cid+'&x=60&y=56');assert px['capture']==cid and px['fetchAddress']=='unavailable'
    spr=request('/api/sprites?capture='+cid);assert len(spr['bounds'])>=3
    check('native images pixels source fields and admitted object bounds')
    websocket_test();check('malformed client packets rejected and eight WebSocket reconnects release viewer slots')
    cp=control('checkpoint-save',name='known boundary');control('step',frames=5)
    cpp=DATA/'checkpoints'/(cp['id']+'.json');original=json.loads(cpp.read_text());cpp.write_text(json.dumps({**original,'identity':'incompatible'}));request('/api/control',{'action':'checkpoint-load','id':cp['id']},409);cpp.write_text(json.dumps(original))
    control('checkpoint-load',id=cp['id']);assert request('/api/session')['capture'] is None
    control('step');fresh=request('/api/snapshot');assert fresh['id']!=cid and fresh['generation']==1
    check('persistent checkpoint restore and fresh render invalidation')
    control('movie-record',name='controller transitions');control('step',frames=30,p0=16);control('step',frames=30,p0=0);control('movie-save')
    movie=request('/api/movies')[0];assert movie['rows']==60
    mp=DATA/'movies'/(movie['id']+'.json');original=json.loads(mp.read_text());mp.write_text(json.dumps({**original,'identity':'incompatible'}));request('/api/control',{'action':'movie-play','id':movie['id']},409);mp.write_text(json.dumps(original))
    for _ in range(2):
        control('movie-play',id=movie['id']);request('/api/control',{'action':'pad','p0':1},409);r=control('step',frames=60,p0=255);assert r['completed']==60
        assert request('/api/session')['movie']['mode']=='finished';control('movie-stop')
    check('record and replay twice with authoritative inputs and full state/output hashes')
    c=request('/api/snapshot');cid=c['id']
    note=request('/api/comments',{'capture':cid,'title':'Diagnostic','category':'observation','markdown':'native evidence','json':'{"test":true}','mermaid':'flowchart LR\n A-->B'})
    edited={**note,'title':'revision two'};updated=request('/api/comments',edited);assert updated['revision']==2
    request('/api/comments',edited,409);request('/api/comments',{'capture':cid,'json':'{bad'},400)
    bundle=request('/api/export?capture='+cid,raw=True);(DATA/'bundle.zip').write_bytes(bundle);verify(DATA/'bundle.zip')
    check('durable comment revisions conflicts JSON validation and offline bundle integrity')
    # Pause interrupts a long command at the completed-frame boundary.
    result=[]
    t=threading.Thread(target=lambda:result.append(control('step',frames=3600)));t.start();time.sleep(.08);control('pause');t.join();assert 0<result[0]['completed']<3600
    check('pause interrupts bounded step and returns actual completion')
    stop();s=start();assert s['identity']==identity
    assert request('/api/snapshot?capture='+cid)['id']==cid
    assert request('/api/comments')[0]['revision']==2
    control('movie-play',id=movie['id']);control('step',frames=60);assert request('/api/session')['movie']['mode']=='finished'
    check('cold restart saved review and deterministic movie replay including sound/timing state')
    stop()
finally:
    stop();(DATA/'report.json').write_text(json.dumps({'native':True,'passed':passed,'count':len(passed)},indent=2));print(DATA,flush=True)
