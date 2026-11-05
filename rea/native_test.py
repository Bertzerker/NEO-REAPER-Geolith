"""MIT. Runs the actual compiled core; no mocked producer."""
import ctypes as C, hashlib, json, time, sys
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
def audio(lib):
    n=C.c_uint();p=lib.nr_audio(C.byref(n));return C.string_at(p,n.value*2)
if __name__=='__main__':
    mode=int(sys.argv[1]) if len(sys.argv)>1 else 0
    lib=load(mode)
    for _ in range(10):lib.nr_step(0,0)
    print('PC',hex(lib.nr_register(0)), 'RAM',memory(lib,0)[:8].hex(),flush=True)
    before=state(lib);lib.nr_step(16,0)
    observed=state(lib);image=video(lib);sprite=video(lib,1);fix=video(lib,2);sound=audio(lib)
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
    lib.nr_observer(0);lib.nr_step(16,0)
    off=state(lib)
    assert observed==off,'Observer changes emulated state'
    assert image==video(lib),'Observer changes normal output'
    assert sound==audio(lib),'Observer changes generated audio'
    lib.nr_observer(1);assert lib.nr_restore(before,len(before),10);lib.nr_step(16,0)
    assert observed==state(lib) and image==video(lib),'State restore replay mismatch'
    for p0,p1,low_b,low_a in [(256,0,14,7),(512,0,13 if mode==0 else 15,7 if mode==0 else 6),(0,256,11,7),(0,512,7 if mode==0 else 15,7 if mode==0 else 5)]:
        assert lib.nr_restore(before,len(before),10);lib.nr_step(p0,p1);ram=memory(lib,0)
        assert ram[3]&15==low_b and ram[4]&7==low_a,('Start/Select/Coin mapping',mode,p0,p1,ram[:8].hex())
    assert lib.nr_restore(before,len(before),10);lib.nr_step(16,0)
    # PNG from actual native output, standard library only.
    import zlib,struct,binascii
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
