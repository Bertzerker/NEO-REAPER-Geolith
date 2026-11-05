# NEO REAPER - Geolith coordinator handoff

Continued the existing implementation in place from `rea/HANDOFF.md`; preserved the pinned upstream source and all user assets. Owns only `neogeo/`. Sibling emulator trees and shared documents were read-only references. No additional agents were started.

Delivered: native MSVC Geolith DLL, exact-anchor build patches, .NET 10 single-owner host, loopback API/WebSocket atlas, compact workbench, raster samples, CPU/LSPC boundary registers, copied memory/diffs, verified immutable asset-domain reads, leases, named checkpoints, input recording/replay with state/video/audio verification, revision-safe comments, per-tab recovery, saved review and offline-verifiable bundles. See `rea/README.md`, `SPEC.md`, `PARITY.md`, `CORE-SELECTION.md`, `THIRD-PARTY-NOTICES.md`, `VALIDATION.md`.

```powershell
./rea/Build.ps1
./rea/Start-REA.ps1 -Diagnostic -Wait
& 'C:/Users/Chris/AppData/Local/Python/bin/python.exe' ./rea/integration_test.py ./rea/bin 8914
```

Locally supplied commercial configuration:

```powershell
& 'C:/Users/Chris/AppData/Local/Python/bin/python.exe' ./rea/convert_mslug.py ./roms/mslug.zip ./roms/mslug.neo
./rea/Start-REA.ps1 -Rom ./roms/mslug.neo -Firmware ./roms/neogeo.zip -System MVS -Wait
& 'C:/Users/Chris/AppData/Local/Python/bin/python.exe' ./rea/game_workbench_test.py ./roms/mslug.neo ./roms/neogeo.zip
```

US MVS Metal Slug with supplied `sp-u2.sp1` passes native input/state/video/audio observer equality and cold replay, plus production checkpoint/movie API tests. AES commercial remains unverified because the supplied pack lacks AES BIOS names; AES/MVS original diagnostics are separately validated. Generated user derivatives and evidence stay ignored and were not exported or staged. No commercial asset downloads occurred.

Default HTTP/WebSocket port 8910 is configurable. Native execution/control are in-process; 8911/optional 8912 are reserved and intentionally unbound. This avoids a fictitious external-producer connection state. Launchers refuse occupied ports without terminating existing processes. HTTP.sys loopback startup required approved execution outside the Codex restricted sandbox here; no ACL/firewall changes were made.

Suggested shared changes (not applied): allow the unified capability schema to describe an in-process native owner; standardize per-view raster versus boundary-memory labels, source-winner classification without a dedicated graphics card, asset-domain availability after saved review, and explicit native/capture/delivery/presentation counters. Do not unify hardware meanings or movie formats across cores.

Backlog: audible playback, buffer pooling/shared atlas encoder, special cartridge/alternate-controller bounds and compatibility, vertical-zoom/hardware diagnostic coverage, instruction/access hooks, fetch/game-object provenance, CD/link/multi-slot and timeline editing. Exact delivered limitations are visible in capabilities and docs.

Publishing: the initial task specified a private repository. A final check found the remote **PUBLIC**; the user then explicitly authorized pushing the reviewed source to that public repository on 5 November 2026. Repository visibility remains unchanged. ROM/BIOS/private evidence are excluded from source publication. No public release or binary package was created.
