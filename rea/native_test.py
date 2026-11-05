"""MIT. Runs the actual compiled core; no mocked producer."""
import ctypes as C, hashlib, json, struct, time, sys
from pathlib import Path
from diagnostic import generate
ROOT=Path(__file__).parent
def sha(b): return hashlib.sha256(b).hexdigest()
def load(mode=0,rom_path=None,bios_path=None):
    out=generate() if rom_path is None else Path(rom_path).parent;lib=C.CDLL(str(ROOT/'bin'/'neo-reaper-core.dll'))
    lib.nr_open.argtypes=[C.c_void_p,C.c_size_t,C.c_void_p,C.c_size_t,C.c_int,C.c_int]
    lib.nr_error.restype=C.c_char_p;lib.nr_step.restype=C.c_uint64
    for n in ['nr_video','nr_evidence']: getattr(lib,n).restype=C.c_void_p
    for n in ['nr_memory','nr_state']:
        getattr(lib,n).restype=C.c_void_p
    lib.nr_memory.argtypes=[C.c_int,C.POINTER(C.c_size_t)]
    lib.nr_state.argtypes=[C.POINTER(C.c_size_t)]
    lib.nr_sprite_samples.argtypes=[C.POINTER(C.c_size_t)]
    lib.nr_sprite_samples.restype=C.c_void_p
    lib.nr_follow_frame.argtypes=[C.POINTER(C.c_uint32),C.c_size_t]+[C.POINTER(C.c_uint)]*4
    lib.nr_follow_frame.restype=C.c_void_p
    lib.nr_restore.argtypes=[C.c_void_p,C.c_size_t,C.c_uint64]
    lib.nr_audio.argtypes=[C.POINTER(C.c_uint)];lib.nr_audio.restype=C.c_void_p
    rom=Path(rom_path or out/'original.neo').read_bytes();bios=Path(bios_path or out/'original-firmware.zip').read_bytes()
    assert lib.nr_open(rom,len(rom),bios,len(bios),mode,0),lib.nr_error()
    return lib
def state(lib):
    n=C.c_size_t();p=lib.nr_state(C.byref(n));return C.string_at(p,n.value)
def memory(lib,d):
    n=C.c_size_t();p=lib.nr_memory(d,C.byref(n));return C.string_at(p,n.value)
def video(lib,p=0):return C.string_at(lib.nr_video(p),320*264*4)
def sprite_samples(lib):
    n=C.c_size_t()
    p=lib.nr_sprite_samples(C.byref(n))
    return C.string_at(p,n.value) if p else None

def follow_frame(lib,selectors):
    bounds=[C.c_uint() for _ in range(4)]
    rules=(C.c_uint32*len(selectors))(*selectors)
    p=lib.nr_follow_frame(rules,len(rules)//5,*(C.byref(value) for value in bounds))
    x,y,w,h=(value.value for value in bounds)
    return (x,y,w,h,C.string_at(p,w*h*4) if p else b'')

def check_sprite_priority(samples,sprite):
    combined=bytearray(304*224*4)
    strip_one={}
    roots={}
    occluded=False
    for key,bgr,root_high in struct.iter_unpack('<I3sB',samples):
        position=key&0x1ffff
        strip=(key>>17)&0x1ff
        root=(key>>26)|(root_high<<6)
        roots.setdefault(strip,set()).add(root)
        color=bgr+b'\xff'
        if strip==1:
            strip_one[position]=color
        if strip==2 and position in strip_one and strip_one[position]!=color:
            occluded=True
        combined[position*4:position*4+4]=color
    assert occluded,'Earlier strip must retain pixels overwritten by the next strip'
    assert roots=={1:{1},2:{2},3:{2},4:{4}},'Chain membership must reflect raster-time control words'
    crop=b''.join(sprite[(y*320+8)*4:(y*320+312)*4] for y in range(24,248))
    assert combined==crop,'Full priority range must reproduce the native sprite plane'

def audio(lib):
    n=C.c_uint();p=lib.nr_audio(C.byref(n));return C.string_at(p,n.value*2)
if __name__=='__main__':
    mode=int(sys.argv[1]) if len(sys.argv)>1 else 0
    lib=load(mode)
    selectors=(0,1,1,1,381)
    for _ in range(10):lib.nr_step(0,0)
    print('PC',hex(lib.nr_register(0)), 'RAM',memory(lib,0)[:8].hex(),flush=True)
    before=state(lib);lib.nr_step(16,0)
    observed=state(lib);image=video(lib);sprite=video(lib,1);fix=video(lib,2);sound=audio(lib)
    priority=sprite_samples(lib)
    check_sprite_priority(priority,sprite)
    x,y,w,h,rgba=follow_frame(lib,selectors)
    assert w>0 and h>0
    expected=bytearray(w*h*4)
    for key,bgr,_ in struct.iter_unpack('<I3sB',priority):
        if ((key>>17)&0x1ff) not in (1,3):continue
        py,px=divmod(key&0x1ffff,304)
        assert x<=px<x+w and y<=py<y+h
        offset=((py-y)*w+px-x)*4
        expected[offset:offset+4]=bgr[::-1]+b'\xff'
    assert rgba==expected,'Follow frame lost selected pixels behind other strips'
    for selector,strips in [((0x100,0,0xfffff,1,3),{1,2,3}),((0,1,1,2,3),{3})]:
        x,y,w,h,rgba=follow_frame(lib,selector)
        assert w and h
        expected=bytearray(w*h*4)
        for key,bgr,_ in struct.iter_unpack('<I3sB',priority):
            if ((key>>17)&0x1ff) not in strips:continue
            py,px=divmod(key&0x1ffff,304)
            assert x<=px<x+w and y<=py<y+h
            offset=((py-y)*w+px-x)*4
            expected[offset:offset+4]=bgr[::-1]+b'\xff'
        assert rgba==expected,'Follow frame did not respect hardware strip range and draw order'
    assert any(sound),'Original SSG tone must generate native samples'
    assert memory(lib,0)[:3]==b'\x12\x34\xef',memory(lib,0)[:8].hex()
    assert any(sprite) and any(fix),'Actual captured sprite/FIX must be populated'
    for y in range(24,248):
        for x in range(8,312):
            i=(y*320+x)*4
            layer=fix if fix[i+3] else sprite if sprite[i+3] else None
            if layer: assert image[i:i+3]==layer[i:i+3], 'Captured composition mismatch'
    def rgb(x,y):
        b,g,r,_=sprite[(y*320+x)*4:(y*320+x)*4+4];return (r,g,b)
    assert rgb(65,80)[0]>200 and rgb(65,120)[2]>200 and rgb(65,220)[1]>200,'Raster-time palette changes lost'
    assert lib.nr_restore(before,len(before),10)
    assert not any(C.string_at(lib.nr_evidence(4),264)),'Restore must invalidate evidence'
    assert sprite_samples(lib) is None
    assert follow_frame(lib,selectors)==(0,0,0,0,b'')
    lib.nr_observer(0);lib.nr_step(16,0)
    off=state(lib)
    assert observed==off,'Observer changes emulated state'
    assert image==video(lib),'Observer changes normal output'
    assert sound==audio(lib),'Observer changes generated audio'
    assert sprite_samples(lib) is None
    lib.nr_observer(1);assert lib.nr_restore(before,len(before),10);lib.nr_step(16,0)
    assert observed==state(lib) and image==video(lib),'State restore replay mismatch'
    assert sprite_samples(lib)==priority,'Priority samples changed after restore and replay'
    for p0,p1,low_b,low_a in [(256,0,14,7),(512,0,13 if mode==0 else 15,7 if mode==0 else 6),(0,256,11,7),(0,512,7 if mode==0 else 15,7 if mode==0 else 5)]:
        assert lib.nr_restore(before,len(before),10);lib.nr_step(p0,p1);ram=memory(lib,0)
        assert ram[3]&15==low_b and ram[4]&7==low_a,('Start/Select/Coin mapping',mode,p0,p1,ram[:8].hex())
    assert lib.nr_restore(before,len(before),10);lib.nr_step(16,0)
    # PNG from actual native output, standard library only.
    import zlib,binascii
    def png(raw,path):
        def chunk(t,d):return struct.pack('>I',len(d))+t+d+struct.pack('>I',binascii.crc32(t+d)&0xffffffff)
        rows=[]
        for y in range(24,248):
            row=bytearray([0])
            for x in range(8,312):
                b,g,r,a=raw[(y*320+x)*4:(y*320+x)*4+4];row.extend([r,g,b,255 if path.stem=='composite' else a])
            rows.append(row)
        path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',304,224,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(b''.join(rows)))+chunk(b'IEND',b''))
    out=ROOT/'test-output';out.mkdir(exist_ok=True)
    for name,data in [('composite',image),('sprites',sprite),('fix',fix)]:png(data,out/(name+'.png'))
    report={'native':True,'system':'MVS' if mode else 'AES','fixture':'original test firmware; no retail BIOS','stateSha256':sha(observed),'outputSha256':sha(image),'observerEquality':True,'restoreReplay':True,'ram':memory(lib,0)[:8].hex()}
    (out/f'native-{mode}.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
