"""MIT. Original test-only 68000 cartridge/firmware, NOT a retail BIOS replacement.
No third-party ROM bytes. Disk P/BIOS words use Geolith NEO byte order.
"""
from pathlib import Path
import struct, zipfile, hashlib, json
OUT=Path(__file__).parent/'test-output'/'diagnostic'
def generate(output=OUT,catalog=False):
    output.mkdir(parents=True,exist_ok=True)
    p=bytearray(1024*1024); code=bytearray()
    def w(*v): code.extend(struct.pack('>'+'H'*len(v),*v))
    def mw(value,addr): w(0x33fc,value,addr>>16,addr&65535)
    def mb(value,addr): w(0x13fc,value,addr>>16,addr&65535)
    def vram(addr,value): mw(addr,0x3c0000); mw(value,0x3c0002)
    w(0x46fc,0x2700) # supervisor; mask interrupts (poll scanline)
    mb(0,0x3a001b); mw(1,0x3c0004)
    mw(0x1234,0x100000)
    for n,col in [(1,0x0f00),(17,0x00f0),(33,0x000f),(4095,0x0222)]: mw(col,0x400000+n*2)
    if catalog: mw(0x00ff,0x400000+49*2)
    # FIX pattern at all positions; transparent tile 0 except the test band.
    for x in range(8,15):
        for y in range(10,16): vram(0x7000+x*32+y,0x2001)
    # Overlapping strips: later sprites win, chaining, zoom, wrapping.
    for i,x,y,z,chain,pal in [(1,64,64,0x0fff,0,0),(2,72,68,0x0fff,0,1),(3,0,0,0x07ff,1,0),(4,508,150,0x0fff,0,1)]:
        vram(0x8000+i,z); vram(0x8200+i,0x40 if chain else (((512-y)&511)<<7)|12); vram(0x8400+i,x<<7)
        for t in range(12):
            vram(i*64+t*2,2 if catalog and i in (2,4) and t==2 else 1)
            vram(i*64+t*2+1,pal<<8)
    start=len(code)
    def wait(line):
        at=len(code);w(0x3039,0x003c,0x0006,0x0240,0xff80,0x0c40,((line+248)<<7)&0xffff)
        w(0x6600, (at-(len(code)+2))&65535)
    wait(32);mw(0x0f00,0x400002)
    # Read active-low pad and show its exact byte in copied work RAM.
    w(0x1039,0x0030,0x0000,0x13c0,0x0010,0x0002)
    w(0x1239,0x0038,0x0000,0x13c1,0x0010,0x0003) # status B -> RAM[3]
    w(0x1239,0x0032,0x0001,0x13c1,0x0010,0x0004) # status A -> RAM[4]
    w(0x0800,4) # BTST #4,D0: pressed A is active low
    branch=len(code);w(0x6700,0)
    mw(0x0222,0x401ffe)
    if catalog:
        vram(2*64+4,2);vram(2*64+5,1<<8);vram(0x8402,72<<7)
    jump=len(code);w(0x6000,0)
    pressed=len(code);mw(0x0f00,0x401ffe)
    if catalog:
        vram(2*64+4,3);vram(2*64+5,3<<8);vram(0x8402,74<<7)
    after=len(code)
    struct.pack_into('>H',code,branch+2,pressed-(branch+2));struct.pack_into('>H',code,jump+2,after-(jump+2))
    mb(0,0x300001) # watchdog
    wait(100);mw(0x000f,0x400002)
    wait(200);mw(0x00f0,0x400002) # distinct end-of-frame palette
    w(0x4ef9,0,(0x200+start)&65535)
    p[0x200:0x200+len(code)]=code
    struct.pack_into('>II',p,0,0x10ff00,0x200)
    bios=bytearray(128*1024);struct.pack_into('>II',bios,0,0x10ff00,0x200)
    def swapped(b): return bytes(b[i^1] for i in range(len(b)))
    s=bytearray(128*1024);s[32:64]=bytes([0x11,0,0x11,0,0x11,0,0x11,0]*4)
    c=bytearray(128*1024)
    for off in range(128,256,4): c[off]=255
    if catalog:
        c[256:384]=c[128:256]
        c[384:512]=c[128:256]
    m=bytearray(128*1024);z=bytearray()
    # Original Z80 SSG square tone and live sound-RAM counter.
    for reg,value in [(0,0x80),(1,0),(7,0x3e),(8,15)]:z.extend([0x3e,reg,0xd3,4,0x3e,value,0xd3,5])
    z.extend([0x21,0,0xf8,0x34,0x18,0xfd]);m[:len(z)]=z
    sizes=[len(p),len(s),len(m),128*1024,0,len(c)]
    h=bytearray(4096);h[:4]=b'NEO\x01';struct.pack_into('<6I',h,4,*sizes);h[44:65]=b'NEO REAPER diagnostic'
    rom=h+swapped(p)+s+m+bytes(128*1024)+c
    (output/'original.neo').write_bytes(rom)
    # Only identity zoom is validated; other tables are original linear test data.
    lo=bytes(range(256))*256
    with zipfile.ZipFile(output/'original-firmware.zip','w',zipfile.ZIP_DEFLATED) as z:
        def entry(name,data):
            info=zipfile.ZipInfo(name,(1980,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;z.writestr(info,data)
        for name in ['neo-epo.bin','neo-po.bin','sp-u2.sp1','japan-j3.bin','sp-45.sp1','sp-s2.sp1']: entry(name,swapped(bios))
        entry('000-lo.lo',lo);entry('sfix.sfix',s);entry('sm1.sm1',m)
    (output/'NOTICE.txt').write_text('Original MIT test fixtures. Minimal reset vectors and artificial zoom tables. NOT retail firmware; no commercial BIOS/game compatibility established.\n')
    return output
if __name__=='__main__': print(generate())
