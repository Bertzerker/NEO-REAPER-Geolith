# Core selection

Selected: Geolith libretro source, exact upstream commit `2dc88e3fa72ff404213a65d6eb5bb33baa687f73`, fetched from https://github.com/libretro/geolith-libretro on 5 November 2026.

Product: **NEO REAPER - Geolith**. Repository: https://github.com/Bertzerker/NEO-REAPER-Geolith (dedicated source remote; verify visibility against user authorization before publishing).

Geolith offers accessible C AES/MVS renderer, native state serialization augmented by tested frontend context extensions, digital input callbacks and copied memory domains. These are a smaller integration/build surface than the MAME machine framework. MAME was considered for its mature device/render hooks and debugging, but its larger source/build boundary is unnecessary for this initial cartridge-only frontend. BizHawk's NeoGeoPort is an NGP/NeoPop core and is not an AES/MVS candidate.

The integration uses the Geolith core directly rather than advertising CD/CHD support from the libretro frontend. Renderer observer additions are applied to a private build copy; the pinned submodule remains pristine. Input polling, checkpoints and asset loading stay native, with frontend compatibility metadata added around core state bytes.

Licenses: Geolith and its YMFM adaptation use BSD-3-Clause notices; Musashi and Jolly Good Z80 retain their MIT notices; miniz and Speex carry their bundled licenses. See upstream README/LICENSE and source/dependency notices. The integration is MIT. No SNES/Genesis GPL code is copied into this project.

Limitations inherited from upstream include instruction-level CPU synchronization and best-effort fine raster timing. Captured samples reproduce this renderer; they do not establish cycle-perfect physical hardware behavior. Actual AES/MVS compatibility will be listed separately from diagnostic validation in VALIDATION.md.

Implemented integration points: native renderer samples and compositor winner, frame-boundary memory/CPU/LSPC registers, digital pad callbacks, and complete declared checkpoint extensions. Default-board cartridges are admitted; pinned geo_neo special-case NGH boards/controllers/link paths are explicitly rejected. User-supplied Metal Slug is separately validated in US MVS mode. AES diagnostic success is not commercial AES validation. See VALIDATION.md.
