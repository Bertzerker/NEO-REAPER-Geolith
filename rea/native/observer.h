/* SPDX-License-Identifier: MIT */
#ifndef NR_OBSERVER_H
#define NR_OBSERVER_H
#include <stdint.h>
#include <stddef.h>
extern int nr_observe;
extern uint32_t nr_sprite[320*264], nr_fix[320*264];
extern uint16_t nr_palette[2][320*264], nr_object[320*264];
extern uint32_t nr_tile[2][320*264];
extern uint8_t nr_winner[320*264], nr_rows[264];
extern uint16_t nr_lineobject[2][320];
extern uint32_t nr_linetile[2][320];
static inline void nr_fix_pixel(size_t pos, uint32_t color, unsigned pal, unsigned tile) {
    if (nr_observe) { nr_fix[pos]=color|0xff000000;nr_palette[1][pos]=(uint16_t)pal;nr_tile[1][pos]=tile;nr_winner[pos]=2; }
}
#endif
