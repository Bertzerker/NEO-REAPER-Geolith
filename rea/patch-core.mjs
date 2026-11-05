// SPDX-License-Identifier: MIT
// Apply exact, checked observer insertions to a build copy, never the submodule.
import fs from 'node:fs';import path from 'node:path';
const [source,dest]=process.argv.slice(2);let s=fs.readFileSync(source,'utf8').replaceAll('\r\n','\n');
function replace(a,b,count=1){const n=s.split(a).length-1;if(n!==count)throw Error(`Core patch anchor changed: expected ${count}, found ${n}: ${a.slice(0,100)}`);s=s.replaceAll(a,b);}
replace('#include "geo_serial.h"','#include "geo_serial.h"\n#include "observer.h"');
replace('void geo_lspc_init(void) {', `void geo_lspc_init(void) {
    nr_reset_sprite_samples();`);
replace('    unsigned *lb = linebuf[lbactive];', `    unsigned *lb = linebuf[lbactive];
    if (nr_observe)
        nr_finish_sprite_line(lbactive, lspc.scanline, palette);`);
replace('static inline void geo_lspc_sprcalc(void) {', `static inline void geo_lspc_sprcalc(void) {
    unsigned nr_chain_root = 1;
    if (nr_observe)
        nr_begin_sprite_line(lbactive);`);
replace('            xpos = (lspc.vram[0x8400 + i] >> 7) & 0x1ff;', `            nr_chain_root = i;
            xpos = (lspc.vram[0x8400 + i] >> 7) & 0x1ff;`);
replace('        ptr[p] = lb[p] ? palette[lb[p]] : bdcol;',`        ptr[p] = lb[p] ? palette[lb[p]] : bdcol;
        if (nr_observe) { size_t pos=(size_t)lspc.scanline*320+p;nr_rows[lspc.scanline]=1;nr_sprite[pos]=lb[p]?(palette[lb[p]]|0xff000000):0;nr_palette[0][pos]=lb[p]?(uint16_t)lb[p]:0xffff;nr_winner[pos]=lb[p]?1:0;nr_object[pos]=lb[p]?nr_lineobject[lbactive][p]:0xffff;nr_tile[0][pos]=lb[p]?nr_linetile[lbactive][p]:0xffffffff; }`);
replace('            if (pentry) voffset[p << 1] = palette[poffset + pentry];','            if (pentry) { voffset[p << 1] = palette[poffset + pentry];nr_fix_pixel((size_t)(voffset-vbuf)+(p<<1),palette[poffset+pentry],poffset+pentry,tnum); }',3);
replace('            if (pentry) voffset[(p << 1) + 1] = palette[poffset + pentry];','            if (pentry) { voffset[(p << 1) + 1] = palette[poffset + pentry];nr_fix_pixel((size_t)(voffset-vbuf)+(p<<1)+1,palette[poffset+pentry],poffset+pentry,tnum); }',3);
replace('                if (pentry && (xcoord < LSPC_WIDTH))\n                    linebuf[lbactive][xcoord] = poffset + pentry;', `                if (pentry && (xcoord < LSPC_WIDTH)) {
                    linebuf[lbactive][xcoord] = poffset + pentry;
                    if (nr_observe) {
                        nr_lineobject[lbactive][xcoord] = (uint16_t)i;
                        nr_linetile[lbactive][xcoord] = tnum;
                        nr_record_sprite_sample(lbactive, xcoord, i, nr_chain_root, poffset + pentry, tnum);
                    }
                }`);
// Geolith's state excludes the precomputed sprite line buffers. Export their exact
// state as a frontend extension so a restored capture does not lose two rows.
s+=`\n/* NEO REAPER observer context. These are renderer scheduling state, not evidence. */
size_t nr_lspc_context_size(void){return sizeof(linebuf)+sizeof(lbactive);}
void nr_lspc_context_save(void* out){memcpy(out,linebuf,sizeof(linebuf));memcpy((uint8_t*)out+sizeof(linebuf),&lbactive,sizeof(lbactive));}
void nr_lspc_context_load(const void* in){memcpy(linebuf,in,sizeof(linebuf));memcpy(&lbactive,(const uint8_t*)in+sizeof(linebuf),sizeof(lbactive));geo_lspc_shadow_wr(lspc.shadow);nr_reset_sprite_samples();}
unsigned nr_lspc_register(unsigned r){switch(r){case 0:return lspc.palbank;case 1:return lspc.vramaddr;case 2:return lspc.vrambank;case 3:return (uint16_t)lspc.vrammod;case 4:return lspc.shadow;case 5:return lspc.aa_counter;case 6:return lspc.aa_disable;case 7:return lspc.aa_reload;case 8:return lspc.aa_timer;case 9:return lspc.scanline;case 10:return lspc.cyc;default:return 0;}}
`;
replace('#include <stdint.h>','#include <stdint.h>\n#include <string.h>');
fs.mkdirSync(path.dirname(dest),{recursive:true});fs.writeFileSync(dest,s);
// Upstream omits SSG resampler phase and FM preparation/cache scheduling.
// Preserve values without serializing process-specific waveform pointers.
let ym=fs.readFileSync(path.join(path.dirname(source),'ymfm/ymfm_opn.c'),'utf8');
ym+='\n#include <stddef.h>\n#include <string.h>\n';
ym+=`size_t nr_ym_context_size(void){return 7*4+OPERATORS*(sizeof(opdata_cache)-offsetof(opdata_cache,phase_step));}
void nr_ym_context_save(void* out){uint32_t a[7]={m_ssg_resampler_sampindex,m_ssg_resampler_last[0],m_ssg_resampler_last[1],m_ssg_resampler_last[2],m_active_channels,m_modified_channels,m_prepare_count};memcpy(out,a,sizeof(a));unsigned char* p=(unsigned char*)out+sizeof(a);for(int i=0;i<OPERATORS;i++){size_t n=sizeof(opdata_cache)-offsetof(opdata_cache,phase_step);memcpy(p,&m_operator[i].m_cache.phase_step,n);p+=n;}}
void nr_ym_context_load(const void* in){uint32_t a[7];memcpy(a,in,sizeof(a));m_ssg_resampler_sampindex=a[0];for(int i=0;i<3;i++)m_ssg_resampler_last[i]=a[i+1];m_active_channels=a[4];m_modified_channels=a[5];m_prepare_count=a[6];const unsigned char* p=(const unsigned char*)in+sizeof(a);for(int i=0;i<OPERATORS;i++){size_t n=sizeof(opdata_cache)-offsetof(opdata_cache,phase_step);m_operator[i].m_cache.waveform=m_waveform[0];memcpy(&m_operator[i].m_cache.phase_step,p,n);p+=n;}}
`;
fs.writeFileSync(path.join(path.dirname(dest),'ymfm_opn.c'),ym);
let cpu=fs.readFileSync(path.join(path.dirname(source),'geo_m68k.c'),'utf8');
const bank='(addr & 0xfffff) + banksw_addr';
// Default physical banking is bounded even for partially populated PROM sets.
// Restrict replacement to the two default reader bodies, not protected boards.
for(const fn of ['8','16']){
 const original=`return read${fn==='8'?'08':'16'}(romdata->p, ${bank});`;
 const start=cpu.indexOf(`static uint${fn}_t geo_m68k_read_banksw_${fn}_default`);
 const pos=cpu.indexOf(original,start);if(start<0||pos<0||pos>start+250)throw Error('Default bank guard anchor changed');
 cpu=cpu.slice(0,pos)+original.replace(bank,`((${bank}) % romdata->psz)`)+cpu.slice(pos+original.length);
}
fs.writeFileSync(path.join(path.dirname(dest),'geo_m68k.c'),cpu);
