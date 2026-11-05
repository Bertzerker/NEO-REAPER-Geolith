/* SPDX-License-Identifier: MIT
 * Geolith is separately BSD-3-Clause. This observer never executes a second render.
 */
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <stdarg.h>
#include "geo.h"
#include "geo_neo.h"
#include "geo_lspc.h"
#include "geo_mixer.h"
#include "m68k/m68k.h"
#include "m68k/m68kcpu.h"
#include "observer.h"
#include "miniz.h"
#define API __declspec(dllexport)
int nr_observe=1;
uint32_t nr_sprite[320*264], nr_fix[320*264];
uint16_t nr_palette[2][320*264],nr_object[320*264];
uint32_t nr_tile[2][320*264];
uint8_t nr_winner[320*264],nr_rows[264];
uint16_t nr_lineobject[2][320];
uint32_t nr_linetile[2][320];
enum {
    NR_CROP_X = 8, NR_CROP_Y = 24, NR_WIDTH = 304, NR_HEIGHT = 224,
    NR_SAMPLES_PER_LINE = 96 * 16, NR_POSITION_BITS = 17
};
static struct {
    uint64_t samples[NR_SAMPLES_PER_LINE];
    size_t count;
    int ready;
} sprite_lines[2];
static struct {
    uint32_t position_and_strip;
    uint32_t color;
} sprite_samples[NR_HEIGHT * NR_SAMPLES_PER_LINE];
static size_t sprite_count;
static unsigned sprite_rows;
static int sprite_valid;
static uint32_t sprite_selectors[NR_HEIGHT * NR_SAMPLES_PER_LINE];
static struct {
    uint32_t key, color, selector;
} matched_samples[NR_HEIGHT * NR_SAMPLES_PER_LINE];
static uint8_t matched_pixels[NR_HEIGHT * NR_SAMPLES_PER_LINE];
static unsigned follow_left, follow_top, follow_right, follow_bottom;
static uint32_t follow_pixels[NR_WIDTH * NR_HEIGHT];
static uint32_t follow_packed[NR_WIDTH * NR_HEIGHT];

static void nr_follow_pixel(unsigned x, unsigned y, uint32_t color) {
    // The browser's ImageData expects RGBA bytes.
    follow_pixels[y * NR_WIDTH + x] = 0xff000000 | ((color & 0xff) << 16) |
        (color & 0xff00) | ((color >> 16) & 0xff);
    if (x < follow_left) follow_left = x;
    if (x > follow_right) follow_right = x;
    if (y < follow_top) follow_top = y;
    if (y > follow_bottom) follow_bottom = y;
}

static int follow_matches(size_t index, const uint32_t *rules, size_t count) {
    unsigned slot = sprite_selectors[index] & 0xff;
    unsigned tile = sprite_selectors[index] >> 8;
    unsigned strip = (sprite_samples[index].position_and_strip >> NR_POSITION_BITS) & 0x1ff;
    for (size_t j = 0; j < count; ++j) {
        const uint32_t *rule = rules + j * 5;
        if ((rule[0] == 0x100 || slot == rule[0]) && tile >= rule[1] && tile <= rule[2] &&
            strip >= rule[3] && strip <= rule[4])
            return 1;
    }
    return 0;
}

API const void *nr_follow_samples(const uint32_t *rules, size_t count, size_t *length) {
    *length = 0;
    if (!count || !sprite_valid || sprite_rows != NR_HEIGHT)
        return NULL;
    uint8_t roots[512] = {0};
    int found = 0;
    for (size_t i = 0; i < sprite_count; ++i) {
        matched_pixels[i] = follow_matches(i, rules, count);
        if (!matched_pixels[i])
            continue;
        unsigned root = (sprite_samples[i].position_and_strip >> 26) | ((sprite_samples[i].color >> 24) << 6);
        roots[root] = 1;
        found = 1;
    }
    if (!found)
        return NULL;
    for (size_t i = 0; i < sprite_count; ++i) {
        unsigned root = (sprite_samples[i].position_and_strip >> 26) | ((sprite_samples[i].color >> 24) << 6);
        if (!roots[root])
            continue;
        matched_samples[*length].key = sprite_samples[i].position_and_strip;
        matched_samples[*length].color = sprite_samples[i].color;
        matched_samples[*length].selector = sprite_selectors[i] | (matched_pixels[i] ? 0 : 0x80000000u);
        ++*length;
    }
    return matched_samples;
}

API const void *nr_follow_frame(const uint32_t *rules, size_t count, unsigned *x,
    unsigned *y, unsigned *width, unsigned *height) {
    *x = *y = *width = *height = 0;
    if (!count || !sprite_valid || sprite_rows != NR_HEIGHT)
        return NULL;
    follow_left = NR_WIDTH;
    follow_top = NR_HEIGHT;
    follow_right = follow_bottom = 0;
    memset(follow_pixels, 0, sizeof(follow_pixels));
    // Samples include covered pixels and retain hardware draw order on each line.
    for (size_t i = 0; i < sprite_count; ++i) {
        if (!follow_matches(i, rules, count))
            continue;
        unsigned pos = sprite_samples[i].position_and_strip & ((1u << NR_POSITION_BITS) - 1);
        nr_follow_pixel(pos % NR_WIDTH, pos / NR_WIDTH, sprite_samples[i].color);
    }
    if (follow_left == NR_WIDTH)
        return NULL;
    *x = follow_left;
    *y = follow_top;
    *width = follow_right - follow_left + 1;
    *height = follow_bottom - follow_top + 1;
    for (unsigned row = 0; row < *height; ++row)
        memcpy(follow_packed + row * *width, follow_pixels + (row + *y) * NR_WIDTH + *x,
            *width * sizeof(uint32_t));
    return follow_packed;
}

void nr_reset_sprite_samples(void) {
    sprite_lines[0].ready = sprite_lines[1].ready = 0;
    sprite_valid = 0;
}

void nr_begin_sprite_line(unsigned buffer) {
    sprite_lines[buffer].count = 0;
    sprite_lines[buffer].ready = 1;
}

void nr_record_sprite_sample(unsigned buffer, unsigned x, unsigned strip, unsigned root, unsigned palette_entry, unsigned tile) {
    if (x < NR_CROP_X || x >= NR_CROP_X + NR_WIDTH)
        return;
    size_t index = sprite_lines[buffer].count;
    if (index == NR_SAMPLES_PER_LINE) {
        sprite_lines[buffer].ready = 0;
        return;
    }
    // Palette index (13 bits), native X, strip and chain root (9 bits each), then tile.
    sprite_lines[buffer].samples[index] = ((uint64_t)tile << 40) | ((uint64_t)root << 31) |
        (strip << 22) | (x << 13) | palette_entry;
    sprite_lines[buffer].count++;
}

void nr_finish_sprite_line(unsigned buffer, unsigned y, const uint32_t *palette) {
    int ready = sprite_lines[buffer].ready;
    sprite_lines[buffer].ready = 0;
    if (y < NR_CROP_Y || y >= NR_CROP_Y + NR_HEIGHT)
        return;
    if (!ready || nr_rows[y]) {
        sprite_valid = 0;
        return;
    }
    ++sprite_rows;
    unsigned row = (y - NR_CROP_Y) * NR_WIDTH;
    // Resolve colors at scanout, when Geolith reads the palette for this line.
    for (size_t i = 0; i < sprite_lines[buffer].count; ++i) {
        uint64_t sample = sprite_lines[buffer].samples[i];
        unsigned x = ((sample >> 13) & 0x1ff) - NR_CROP_X;
        unsigned position = row + x;
        unsigned strip = (sample >> 22) & 0x1ff;
        unsigned root = (sample >> 31) & 0x1ff;
        uint32_t color = palette[sample & 0x1fff];
        // Reuse the six free key bits and the implicit opaque alpha byte.
        sprite_samples[sprite_count].position_and_strip = ((root & 0x3f) << 26) | (strip << NR_POSITION_BITS) | position;
        sprite_samples[sprite_count].color = (color & 0x00ffffff) | ((root >> 6) << 24);
        sprite_selectors[sprite_count] = ((uint32_t)(sample >> 40) << 8) | ((sample >> 4) & 0xff);
        ++sprite_count;
    }
}

API const void *nr_sprite_samples(size_t *bytes) {
    if (!sprite_valid || sprite_rows != NR_HEIGHT) {
        *bytes = 0;
        return NULL;
    }
    *bytes = sprite_count * sizeof(sprite_samples[0]);
    return sprite_samples;
}
static uint32_t video[320*264];
static int16_t audio[8192];
static uint8_t* cartridge;
static unsigned pads[2];
static uint64_t frame;
static char error[1024];
static unsigned samples;
static int loaded;
static uint8_t statebuf[1024*1024];
size_t nr_lspc_context_size(void);
void nr_lspc_context_save(void*);
void nr_lspc_context_load(const void*);
unsigned nr_lspc_register(unsigned);
size_t nr_ym_context_size(void);
void nr_ym_context_save(void*);
void nr_ym_context_load(const void*);
static uint32_t le32(const uint8_t* p){return (uint32_t)p[0]|(uint32_t)p[1]<<8|(uint32_t)p[2]<<16|(uint32_t)p[3]<<24;}
static int checkbios(const uint8_t* data,size_t length,int sys,int region){
    mz_zip_archive z={0};if(!mz_zip_reader_init_mem(&z,data,length,0)){snprintf(error,sizeof(error),"Invalid firmware ZIP archive.");return 0;}
    const char* mvs[]={"sp-u2.sp1","japan-j3.bin","sp-45.sp1","sp-s2.sp1"};
    const char* names[]={sys?mvs[region]:(region==1?"neo-po.bin":"neo-epo.bin"),"000-lo.lo","sfix.sfix","sm1.sm1"};
    const size_t mins[]={SIZE_128K,SIZE_64K,SIZE_128K,SIZE_128K};int ok=1;
    for(int i=0;i<4;i++){mz_zip_archive_file_stat st;int n=mz_zip_reader_locate_file(&z,names[i],NULL,0);if(n<0||!mz_zip_reader_file_stat(&z,n,&st)||st.m_uncomp_size<mins[i]||st.m_uncomp_size>SIZE_4M||(st.m_uncomp_size&1)){snprintf(error,sizeof(error),"Missing or invalid firmware component %s (minimum %zu bytes).",names[i],mins[i]);ok=0;break;}}
    mz_zip_reader_end(&z);return ok;
}
static void logmsg(int level,const char *fmt,...) { if(level>=GEO_LOG_ERR){va_list v;va_start(v,fmt);vsnprintf(error,sizeof(error),fmt,v);va_end(v);} }
static unsigned input(unsigned p){return (~pads[p&1])&255;}
static unsigned stat_a(void){return ngsys.sys==SYSTEM_AES?7:7 & ~((pads[0]&512)?1:0) & ~((pads[1]&512)?2:0);}
static unsigned stat_b(void){unsigned s=0x3f;if(pads[0]&256)s&=~1;if(ngsys.sys==SYSTEM_AES&&(pads[0]&512))s&=~2;if(pads[1]&256)s&=~4;if(ngsys.sys==SYSTEM_AES&&(pads[1]&512))s&=~8;return s;}
static unsigned systype(void){return 0xc0;}
static unsigned dips(void){return 0xff;}
static unsigned system4(void){return 0xff;}
static void audio_ready(size_t count){samples=(unsigned)count;}
API const char* nr_error(void){return error;}
API int nr_open(const uint8_t *rom,size_t romlen,const uint8_t* bios,size_t bioslen,int sys,int region){
    if(loaded){snprintf(error,sizeof(error),"A core is already loaded; restart the owned session to change assets.");return 0;}
    if(sys<0||sys>1||region<0||region>3||romlen<4096||memcmp(rom,"NEO\1",4)){snprintf(error,sizeof(error),"Expected an AES/MVS .neo cartridge with a complete header.");return 0;}
    uint64_t total=4096;const uint32_t minimum[]={SIZE_1M,SIZE_128K,SIZE_128K,SIZE_128K,0,SIZE_128K};
    for(unsigned i=0;i<6;i++){uint32_t len=le32(rom+4+i*4);if(len<minimum[i]||(len&127)||len>256*1024*1024){snprintf(error,sizeof(error),"Invalid NEO region %u length.",i);return 0;}total+=len;}
    if(total!=romlen||romlen>1024ULL*1024*1024){snprintf(error,sizeof(error),"Invalid NEO total length.");return 0;}
    /* This adapter admits only the upstream default digital-controller board.
     * Every special-case NGH in pinned geo_neo_load remains explicitly rejected. */
    switch(le32(rom+40)){
    case 0x006:case 0x019:case 0x038:case 0x008:case 0x004:case 0x027:case 0x036:case 0x048:case 0x047:case 0x052:case 0x066:case 0x080:case 0x236:case 0x242:case 0x250:case 0x151:case 0x251:case 0x253:case 0x256:case 0x257:case 0x263:case 0x266:case 0x268:case 0x269:case 0x271:case 0x275:case 0x3e7:case 0x999:case 0x5003:case 0xfedc:
        snprintf(error,sizeof(error),"Special-board, alternate-controller or link cartridge NGH %X is unsupported by this adapter.",le32(rom+40));return 0;
    }
    if(!checkbios(bios,bioslen,sys,region))return 0;
    cartridge=malloc(romlen);if(!cartridge)return 0;memcpy(cartridge,rom,romlen);
    geo_log_set_callback(logmsg);geo_set_system(sys);geo_set_region(region);
    geo_input_set_callback(0,input);geo_input_set_callback(1,input);
    geo_input_sys_set_callback(0,stat_a);geo_input_sys_set_callback(1,stat_b);geo_input_sys_set_callback(2,systype);geo_input_sys_set_callback(3,dips);geo_input_sys_set_callback(4,system4);
    geo_init();geo_lspc_set_buffer(video);geo_lspc_set_palette(1);geo_mixer_set_buffer(audio);geo_mixer_set_callback(audio_ready);geo_mixer_set_raw(1);
    if(!geo_bios_load_mem((void*)bios,bioslen))return 0;
    if(sys==SYSTEM_AES){/* Keep system FIX/music mappings backed by supplied assets. */
        mz_zip_archive z={0};if(!mz_zip_reader_init_mem(&z,bios,bioslen,0))return 0;romdata_t* r=geo_romdata_ptr();
        r->sfix=mz_zip_reader_extract_file_to_heap(&z,"sfix.sfix",&r->sfixsz,0);r->sm=mz_zip_reader_extract_file_to_heap(&z,"sm1.sm1",&r->smsz,0);mz_zip_reader_end(&z);if(!r->sfix||!r->sm)return 0;
    }
    if(!geo_neo_load(cartridge,romlen))return 0;
    romdata_t *r=geo_romdata_ptr();
    if(r->bsz<SIZE_128K||r->l0sz<SIZE_64K||r->psz<SIZE_1M||r->ssz<SIZE_128K||r->msz<SIZE_128K||r->v1sz<SIZE_128K||r->csz<SIZE_128K){snprintf(error,sizeof(error),"Asset regions are too small for this cartridge-only frontend's validated safe bounds.");return 0;}
    geo_reset(0);frame=0;loaded=1;return 1;
}
API uint64_t nr_step(unsigned p0,unsigned p1){
    if(!loaded)return frame;pads[0]=p0;pads[1]=p1;
    memset(nr_sprite,0,sizeof(nr_sprite));memset(nr_fix,0,sizeof(nr_fix));memset(nr_rows,0,sizeof(nr_rows));memset(nr_winner,0,sizeof(nr_winner));
    memset(nr_palette,255,sizeof(nr_palette));memset(nr_object,255,sizeof(nr_object));memset(nr_tile,255,sizeof(nr_tile));
    sprite_count = 0;
    sprite_rows = 0;
    sprite_valid = nr_observe;
    geo_exec();return ++frame;
}
API void nr_reset(void){
    geo_reset(0);
    frame=0;
    pads[0]=pads[1]=0;
    samples=0;
    memset(video,0,sizeof(video));
    nr_reset_sprite_samples();
}
API void nr_observer(int enabled){if(nr_observe!=!!enabled)nr_reset_sprite_samples();nr_observe=!!enabled;}
API const void* nr_video(int plane){return plane==1?nr_sprite:plane==2?nr_fix:video;}
API const void* nr_evidence(int kind){switch(kind){case 0:return nr_winner;case 1:return nr_palette;case 2:return nr_object;case 3:return nr_tile;case 4:return nr_rows;default:return NULL;}}
API const void* nr_memory(int domain,size_t* length){return geo_mem_ptr(domain,length);}
/* Pointer-free Musashi context prefix includes stopped/reset/exception modes
 * omitted by upstream serialization. Never serialize callbacks or code pointers. */
API const void* nr_state(size_t* length){size_t n=geo_state_size();memcpy(statebuf,geo_state_save_raw(),n);nr_lspc_context_save(statebuf+n);n+=nr_lspc_context_size();nr_ym_context_save(statebuf+n);n+=nr_ym_context_size();memcpy(statebuf+n,&m68ki_cpu,offsetof(m68ki_cpu_core,cyc_instruction));n+=offsetof(m68ki_cpu_core,cyc_instruction);memcpy(statebuf+n,pads,sizeof(pads));*length=n+sizeof(pads);return statebuf;}
API int nr_restore(const uint8_t* bytes,size_t length,uint64_t restoreframe){size_t n=geo_state_size();if(length!=n+nr_lspc_context_size()+nr_ym_context_size()+offsetof(m68ki_cpu_core,cyc_instruction)+sizeof(pads)||memcmp(bytes,"GEO",3))return 0;int ok=geo_state_load_raw(bytes);if(ok){nr_lspc_context_load(bytes+n);n+=nr_lspc_context_size();nr_ym_context_load(bytes+n);n+=nr_ym_context_size();memcpy(&m68ki_cpu,bytes+n,offsetof(m68ki_cpu_core,cyc_instruction));n+=offsetof(m68ki_cpu_core,cyc_instruction);memcpy(pads,bytes+n,sizeof(pads));frame=restoreframe;memset(video,0,sizeof(video));memset(nr_rows,0,sizeof(nr_rows));memset(nr_lineobject,255,sizeof(nr_lineobject));}return ok;}
API unsigned nr_register(unsigned r){if(r>=18)return r<29?nr_lspc_register(r-18):0;return m68k_get_reg(NULL,r==0?M68K_REG_PC:r==1?M68K_REG_SR:r<=9?(M68K_REG_D0+r-2):(M68K_REG_A0+r-10));}
API const void* nr_audio(unsigned* count){*count=samples;return audio;}
