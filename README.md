# NEO REAPER - Geolith

A local Neo Geo AES/MVS reverse-engineering workbench built around [Geolith](https://github.com/libretro/geolith-libretro). This repository contains the REA integration and a pinned upstream source submodule. Neo Geo Pocket and CD are outside the initial integration scope.

Implementation is in progress. Build/run instructions and measured validation will be recorded in `rea/README.md` and `rea/VALIDATION.md`. No commercial ROM or system firmware is included.

The integration preserves actual renderer samples, immutable capture identities, copied memory, input movies and checkpoint/evidence distinctions. A software collision box or exact fetch provenance is not inferred from a hardware sprite rectangle.

Geolith is copyright Rupert Carmichael and contributors, BSD-3-Clause, with dependency notices retained in its source. New NEO REAPER integration code is MIT licensed unless a file states otherwise.
