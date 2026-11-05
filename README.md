# NEO REAPER - Geolith

A local Neo Geo AES/MVS reverse-engineering workbench built around [Geolith](https://github.com/libretro/geolith-libretro). This repository contains the REA integration and a pinned upstream source submodule. Neo Geo Pocket and CD are outside the initial integration scope.

The native integration and local workbench are runnable. See [build/run and API instructions](rea/README.md), [measured validation](rea/VALIDATION.md), [actual scope](rea/SPEC.md) and [feature parity](rea/PARITY.md). No commercial ROM or system firmware is included.

The integration preserves actual renderer samples, immutable capture identities, copied memory, input movies and checkpoint/evidence distinctions. A software collision box or exact fetch provenance is not inferred from a hardware sprite rectangle.

```powershell
./rea/Build.ps1
./rea/Start-REA.ps1 -Diagnostic -Wait
```

Open [the local workbench](http://127.0.0.1:8910/). Original diagnostic fixtures are test-only and do not replace retail firmware. A locally supplied Metal Slug cartridge and US MVS BIOS were separately tested; that evidence stays private and ignored. AES retail compatibility and special cartridge boards remain unverified/unsupported as documented.

Geolith is copyright Rupert Carmichael and contributors, BSD-3-Clause, with dependency notices retained in its source. New NEO REAPER integration code is MIT licensed unless a file states otherwise.
