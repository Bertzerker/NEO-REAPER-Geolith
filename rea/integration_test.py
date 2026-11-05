"""MIT. Actual DLL/.NET producer acceptance, isolated owned processes and data."""
from pathlib import Path
import subprocess,sys,json,time,urllib.request,urllib.error,hashlib,threading,zipfile,uuid,socket,base64,os,struct
from contextlib import ExitStack
from diagnostic import generate
from verify_bundle import verify
ROOT=Path(__file__).parent.resolve(); BIN=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else ROOT/'bin'
PORT=int(sys.argv[2]) if len(sys.argv)>2 else 8914
DATA=ROOT/'test-output'/('integration-'+uuid.uuid4().hex);DATA.mkdir(parents=True)
HELPERS=DATA/'sprite-helpers';HELPERS.mkdir()
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
def start(catalog=False):
    global proc,token
    fixture=generate(DATA/'catalog-fixture',catalog=True) if catalog else generate()
    helper={'game':'Original diagnostic','cartridgeSha256':hashlib.sha256((fixture/'original.neo').read_bytes()).hexdigest(),
            'system':'AES','region':0,'sprites':[{'id':f'test-{p}','name':f'Test palette {p}',
            'selectors':[{'palette':p,'firstTile':'0x1','lastTile':'0x1'}]} for p in range(2)]}
    (HELPERS/'diagnostic.json').write_text(json.dumps(helper))
    log=open(DATA/'host.log','ab')
    proc=subprocess.Popen(['dotnet',str(BIN/'NeoReaper.dll'),'--rom',str(fixture/'original.neo'),'--firmware',str(fixture/'original-firmware.zip'),'--port',str(PORT),'--data',str(DATA),'--web',str(ROOT/'public'),'--sprite-helpers',str(HELPERS)],stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
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
def open_sprite_stream(streams,query):
    sock=streams.enter_context(socket.create_connection(('127.0.0.1',PORT),timeout=5))
    key=base64.b64encode(os.urandom(16)).decode()
    sock.sendall((f'GET /stream/sprite?{query} HTTP/1.1\r\nHost: 127.0.0.1:{PORT}\r\n'
                  f'Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\n'
                  f'Sec-WebSocket-Version: 13\r\nOrigin: {base}\r\n\r\n').encode())
    stream=streams.enter_context(sock.makefile('rb'))
    assert b'101 Switching Protocols' in stream.readline()
    while stream.readline() not in (b'\r\n',b''):pass
    return sock,stream
def read_sprite_packet(stream,expected_opcode=0x82):
    opcode,length=stream.read(2);assert opcode==expected_opcode
    if length==126:length=struct.unpack('>H',stream.read(2))[0]
    elif length==127:length=struct.unpack('>Q',stream.read(8))[0]
    return stream.read(length)
def send_sprite_command(sock,payload):
    mask=os.urandom(4)
    sock.sendall(bytes([0x82,0x80|len(payload)])+mask+bytes(b^mask[i%4] for i,b in enumerate(payload)))
def sprite_stream_test(capture):
    packets=[]
    with ExitStack() as streams:
        for track in ['test-0','test-1']:
            _,stream=open_sprite_stream(streams,f'id={track}')
            packet=read_sprite_packet(stream)
            frame,x,y,w,h=struct.unpack_from('<Q4H',packet)
            assert frame==10 and w and h and len(packet)==16+w*h*4
            packets.append(packet)
        assert packets[0]!=packets[1]
    with ExitStack() as streams:
        sock,stream=open_sprite_stream(streams,'id=test-0&pick=1')
        all_matches=read_sprite_packet(stream)
        assert all_matches[:16]+all_matches[17:]==packets[0]
        # Strip 3 matches palette 0, and its chain also contains strip 2 in palette 1.
        send_sprite_command(sock,struct.pack('<QHH',10,82,56))
        selected=read_sprite_packet(stream)
        frame,x,y,w,h,mode=struct.unpack_from('<Q4HB',selected)
        assert frame==10 and mode==1 and x==64 and w==24 and h
        assert len(selected)==17+w*h*4
        send_sprite_command(sock,b'\0')
        assert read_sprite_packet(stream)==all_matches
    with ExitStack() as streams:
        _,stream=open_sprite_stream(streams,f'capture={capture}&view=1&x=82&y=56')
        assert json.loads(read_sprite_packet(stream,0x81))=={'id':'test-0','name':'Test palette 0'}
        assert read_sprite_packet(stream)==selected
    priority=f'/api/sprite-priority?capture={capture}'
    x=next(x for x in range(64,72) if request(priority+f'&x={x}&y=56')['atPixel']==[1,2])
    helper_path='/api/sprite-helpers'
    helper=json.loads(request(helper_path+'?file=diagnostic.json')['text'])
    helper['sprites']=[{'id':'overlap','name':'Overlap','selectors':[{'firstStrip':1,'lastStrip':3}]}]
    saved=request(helper_path,{'action':'save','file':'overlap.json','revision':'','text':json.dumps(helper)})
    try:
        with ExitStack() as streams:
            sock,stream=open_sprite_stream(streams,'id=overlap&pick=1')
            all_matches=read_sprite_packet(stream)
            for direction,layer,left in [(1,2,56),(-1,1,64),(1,2,56)]:
                send_sprite_command(sock,struct.pack('<QHHb',10,x,56,direction))
                preview=read_sprite_packet(stream)
                assert preview[16]==4 and struct.unpack_from('<HH',preview,len(preview)-4)==(layer,2)
                assert struct.unpack_from('<H',preview,8)[0]==left
            send_sprite_command(sock,b'\1')
            picked=read_sprite_packet(stream)
            assert picked[16]==1 and picked[:16]+picked[17:]==preview[:16]+preview[17:-4]
            send_sprite_command(sock,b'\0')
            assert read_sprite_packet(stream)==all_matches
    finally:
        request(helper_path,{'action':'delete','file':'overlap.json','revision':saved['revision']})
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

def tracked_packet(stream):
    payload=read_sprite_packet(stream)
    return struct.unpack_from('<I',payload)[0],payload[4:]

def tile_pixels(capture,entry,packet,all_matches=False):
    layer=2 if entry['source']=='fix' else 1
    rgba=base64.b64decode(capture['planes'][layer])
    tiles=base64.b64decode(capture['tiles'])
    palettes=base64.b64decode(capture['palette'])
    _,x,y,w,h,_=struct.unpack_from('<Q4HB',packet)
    found=set()
    for py in range(y,y+h):
        for px in range(x,x+w):
            offset=17+((py-y)*w+px-x)*4
            if not packet[offset+3]:continue
            pos=(py+24)*320+px+8
            index=(layer-1)*320*264+pos
            tile=struct.unpack_from('<I',tiles,index*4)[0]
            pal=struct.unpack_from('<H',palettes,index*2)[0]
            assert any(tile==rule['tile'] and (pal>>4)&255==rule.get('palette',(pal>>4)&255) and
                       pal>>12==rule.get('bank',pal>>12) for rule in entry['selectors'])
            assert packet[offset:offset+4]==rgba[pos*4:pos*4+3][::-1]+b'\xff'
            found.add((px,py))
    assert found
    if all_matches:
        expected=set()
        for py in range(224):
            for px in range(304):
                pos=(py+24)*320+px+8
                if not rgba[pos*4+3]:continue
                index=(layer-1)*320*264+pos
                tile=struct.unpack_from('<I',tiles,index*4)[0]
                pal=struct.unpack_from('<H',palettes,index*2)[0]
                if any(tile==rule['tile'] and (pal>>4)&255==rule.get('palette',(pal>>4)&255) and
                       pal>>12==rule.get('bank',pal>>12) for rule in entry['selectors']):
                    expected.add((px,py))
        assert found==expected
        assert len({((px+8)//8,(py+24)//8) for px,py in found})>1

def catalog_test():
    s=start(catalog=True)
    control('step',frames=10)
    first=request('/api/snapshot')['id']
    sy=next(y for y in range(224) if request(f'/api/pixel?capture={first}&view=1&x=70&y={y}')['tile']==2)
    sprite=request(f'/api/catalog-sample?capture={first}&view=1&x=70&y={sy}')
    assert sprite['tile']==2 and sprite['strip']==2 and sprite['source']=='sprite',sprite
    fix_point=next((x,y) for y in range(64,72) for x in range(56,64)
                   if request(f'/api/pixel?capture={first}&view=2&x={x}&y={y}')['tile'] is not None)
    fx,fy=fix_point
    fix=request(f'/api/catalog-sample?capture={first}&view=2&x={fx}&y={fy}')
    assert fix['source']=='fix' and fix['vramWord']==0x710a
    h='/api/sprite-helpers'
    profile={'schemaVersion':1,'cartridgeSha256':s['manifest']['cartridge']['sha256'],'system':'AES','region':0,
             'sprites':[{'id':'fig','name':'Diagnostic figure','selectors':[{'firstTile':2,'lastTile':2}]}],
             'tiles':[{'id':'scb-tile','name':'Sprite tile probe','source':'sprite','selectors':[{'tile':2,'palette':sprite['palette'],'bank':sprite['bank']}]},
                      {'id':'fix-tile','name':'FIX tile probe','source':'fix','selectors':[{'tile':fix['tile'],'palette':fix['palette'],'bank':fix['bank']}]}]}
    def save(revision='',status=200):
        return request(h,{'action':'save','file':'catalog.json','revision':revision,'text':json.dumps(profile)},status)
    original=save()
    control('step',frames=2,p0=16)
    alternate=request(f"/api/catalog-sample?capture={request('/api/snapshot')['id']}&view=1&x=70&y={sy}")
    assert alternate['tile']==3 and alternate['palette']!=sprite['palette']
    control('step',frames=2,p0=0)
    profile['sprites'][0]['selectors'].append({'firstTile':3,'lastTile':3})
    profile['tiles'][0]['selectors'].append({'tile':3,'palette':alternate['palette'],'bank':alternate['bank']})
    updated=save(original['revision'])
    save(original['revision'],409)
    wrong={**profile,'cartridgeSha256':'0'*64}
    request(h,{'action':'save','file':'wrong-rom.json','revision':'','text':json.dumps(wrong)})
    entries={entry['id']:entry for entry in request('/api/catalog')}
    assert len(entries)==5 and len(entries['fig']['selectors'])==2
    control('movie-record',name='Catalog animation')
    control('step',frames=2,p0=0);control('step',frames=9,p0=16);control('movie-save')
    movie=next(movie for movie in request('/api/movies') if movie['name']=='Catalog animation')
    movie_path=DATA/'movies'/(movie['id']+'.json')
    movie_before=movie_path.read_bytes()
    control('movie-stop')
    notes=[]
    metrics=[]
    for phase in ('live','replay'):
        if phase=='replay':control('movie-play',id=movie['id'])
        control('step',p0=0)
        for track,x,y in [('fig',70,sy),('scb-tile',70,sy),('fix-tile',fx,fy)]:
            with ExitStack() as streams:
                sock,stream=open_sprite_stream(streams,f'id={track}&pick=1&catalog=1')
                stream_id=json.loads(read_sprite_packet(stream,0x81))['stream']
                number,packet=tracked_packet(stream)
                assert packet[16]==0
                if track=='fix-tile' and phase=='live':
                    all_observation=request('/api/catalog-pin',{'stream':stream_id,'sequence':number})
                    all_note=request('/api/comments',{'capture':all_observation['capture'],'attachment':all_observation['id'],'title':'All FIX cells'})
                    notes.append((all_note,packet,True))
                frame=struct.unpack_from('<Q',packet)[0]
                send_sprite_command(sock,struct.pack('<QHH',frame,x,y))
                _,picked=tracked_packet(stream)
                assert picked[16]==1,(track,phase,list(picked[:17]))
                control('step',p0=16)
                number,followed=tracked_packet(stream)
                assert followed[16]==1 and struct.unpack_from('<Q',followed)[0]==frame+1
                attachment=request('/api/catalog-pin',{'stream':stream_id,'sequence':number})
                assert attachment['entry']==entries[track] and attachment['selection']=='instance'
                assert attachment['pickedAt']==dict(frame=frame,x=x,y=y)
                if phase=='replay':
                    assert attachment['movie']==movie['id'] and attachment['movieVersion']==hashlib.sha256(movie_before).hexdigest()
                    assert attachment['movieInput']['row']==request('/api/session')['movie']['row']-1
                else:assert attachment['movie'] is None
                note=request('/api/comments',{'capture':attachment['capture'],'attachment':attachment['id'],
                                               'title':phase+' '+track,'category':'bug'})
                notes.append((note,followed,False))
                if phase=='replay' and track=='fix-tile':
                    control('movie-restart')
                    _,reset=tracked_packet(stream)
                    assert reset[16]==2 and len(reset)==17
            metrics.append(request('/api/metrics'))
        if phase=='replay':
            control('step',frames=60);assert request('/api/session')['movie']['mode']=='finished'
            control('movie-stop')
    assert movie_path.read_bytes()==movie_before
    profile['tiles'][1]['name']='Renamed FIX tile'
    profile['sprites']=[]
    save(updated['revision'])
    stop();s=start(catalog=True)
    assert next(entry for entry in request('/api/catalog') if entry['id']=='fix-tile')['name']=='Renamed FIX tile'
    assert all(entry['id']!='fig' for entry in request('/api/catalog'))
    assert s['frame']==0
    for note,packet,all_matches in notes:
        attachment=note['attachment']
        obs=f"/api/catalog-observation?id={attachment['id']}"
        assert request(obs)==attachment
        assert request(obs+'&pixels=1',raw=True)==packet
        output=DATA/(note['id']+'.zip')
        output.write_bytes(request('/api/export?capture='+attachment['capture'],raw=True))
        verify(output)
        with zipfile.ZipFile(output) as bundle:
            capture=json.loads(bundle.read('capture.json'))
            if attachment['entry']['kind']=='tile':tile_pixels(capture,attachment['entry'],packet,all_matches)
    assert request('/api/session')['frame']==0 and movie_path.read_bytes()==movie_before
    check('catalog live and movie follow, immutable tracked comments, capture pixels and cold review/export')
    print(json.dumps({'catalogCaptureCopyP95Ms':max(m['captureCopyP95Ms'] for m in metrics),
                      'catalogPeakWorkingSet':max(m['workingSet'] for m in metrics)}))
    stop()
try:
    s=start();identity=s['identity'];assert s['capture'] is None;check('native startup and no-capture state')
    assert request('/api/sprite-tracks')==[{'id':'test-0','name':'Test palette 0'},{'id':'test-1','name':'Test palette 1'}]
    request('/stream/sprite?id=unknown',status=404)
    check('external helper exposes matching named sprite tracks')
    helpers='/api/sprite-helpers'
    original=request(helpers+'?file=diagnostic.json')
    assert request(helpers)==['diagnostic.json']
    draft=json.loads(original['text'])
    draft['sprites']=[{'id':'strip-range','name':'Strip range','selectors':[{'firstStrip':1,'lastStrip':3}]}]
    edit={'action':'save','file':'range.json','revision':'','text':json.dumps(draft,indent=2)}
    request(helpers,{**edit,'file':'../range.json'},400)
    request(helpers,edit,403,{'X-REA-Token':'bad'})
    created=request(helpers,edit)
    assert (HELPERS/'range.json').read_text()==created['text']==edit['text']
    assert request('/api/session')['spriteTrackRevision']==s['spriteTrackRevision']+1
    assert request('/api/sprite-tracks')[-1]=={'id':'strip-range','name':'Strip range'}
    request(helpers,edit,409)
    update={**edit,'revision':created['revision']}
    request(helpers,{**update,'text':edit['text'].replace('"lastStrip": 3','"lastStrip": 0')},400)
    request(helpers,{**update,'text':original['text']},400)
    assert request(helpers+'?file=range.json')==created
    updated=request(helpers,{**update,'text':edit['text'].replace('Strip range','Priority range')})
    assert request('/api/sprite-tracks')[-1]['name']=='Priority range'
    request(helpers,{'action':'delete','file':'range.json','revision':created['revision']},409)
    request(helpers,{'action':'delete','file':'range.json','revision':updated['revision']})
    assert request(helpers)==['diagnostic.json'] and not (HELPERS/'range.json').exists()
    request('/stream/sprite?id=strip-range',status=404)
    check('helper CRUD, file revision conflicts, invalid selectors and live picker updates')
    request('/api/control',{'action':'step'},403,{'X-REA-Token':'bad'})
    request('/api/control',{'action':'step'},403,{'Origin':'https://evil.example'})
    request('/api/control',{'action':'step','frames':3601},400)
    request('/api/control',{'action':'step','p0':1024},400)
    request('/api/comments',{'markdown':'x'*65536},413)
    check('token origin and bounded input rejection')
    control('step',frames=10);c=request('/api/snapshot');cid=c['id'];assert c['frame']==10
    sprite_stream_test(cid);check('sprite streams isolate tracks, retain linked strips and browse overlapped priority with the wheel')
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
    priority='/api/sprite-priority?capture='+cid
    assert c['spritePriority'] and request(priority)['strips']==[1,2,3,4]
    assert request(priority+'&image=1',raw=True)==request('/api/image?capture='+cid+'&view=1',raw=True)
    assert request(priority+'&image=1&first=1&last=1',raw=True)!=request(priority+'&image=1&first=2&last=2',raw=True)
    request(priority+'&image=1&first=2&last=1',status=400)
    check('sprite priority strip list, solo images and full range equivalence')
    group='/api/sprite-group?capture='+cid
    point='&view=1&x=82&y=56'
    assert c['spriteChains']
    pick=request(group+point)
    assert pick['strip']==3 and pick['strips']==[2,3]
    assert request(group+point+'&image=1',raw=True)==request(priority+'&image=1&first=2&last=3',raw=True)
    request(group+'&view=1&x=303&y=223',status=404)
    check('sprite group keeps hardware siblings and excludes a deeply overlapping strip')
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
    assert request('/api/sprite-priority?capture='+cid)['strips']==[1,2,3,4]
    assert request('/api/sprite-group?capture='+cid+'&view=1&x=82&y=56')['strips']==[2,3]
    assert request('/api/comments')[0]['revision']==2
    control('movie-play',id=movie['id']);control('step',frames=60);assert request('/api/session')['movie']['mode']=='finished'
    check('cold restart saved review and deterministic movie replay including sound/timing state')
    stop()
    catalog_test()
finally:
    stop();(DATA/'report.json').write_text(json.dumps({'native':True,'passed':passed,'count':len(passed)},indent=2));print(DATA,flush=True)
