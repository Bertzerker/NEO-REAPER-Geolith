# NEO REAPER - Geolith handoff

Written 5 November 2026 at the coordinator's request. **Implementation stopped for migration to a visible chat. No concurrent writer remains in this agent.** Resume the same directory; preserve existing work.

## Assignment and ownership

Build Neo Geo AES/MVS REA according to `../../docs/agent-briefs/NEOGEO-AGENT.md`, `SHARED-REQUIREMENTS.md`, workspace/user instructions and the unified interface spec. Own `neogeo/` only. Existing emulator/shared trees and sibling `tg16/` are read-only references. Product: **NEO REAPER - Geolith**. Configurable suggested ports 8910/8911/8912. No further agents were spawned.

The user authorized a dedicated GitHub repository and commits/pushes, overriding the original brief's no-publication clause for this repo only. Keep it private; do not create releases/change visibility or publish user ROMs/BIOS/session material. Parent provisioned `Bertzerker/NEO-REAPER-Geolith`.

## Git/source state

- Local repository: `C:/Users/Chris/Desktop/AI Code/REA Emulators/neogeo`.
- Branch `main`, origin `https://github.com/Bertzerker/NEO-REAPER-Geolith.git`.
- Pushed bootstrap commit `9d2c9d8570970c7f09d54a9256492b278adba31f`.
- Committed: `.gitignore`, `.gitmodules`, MIT `LICENSE`, root `README.md`, `rea/CORE-SELECTION.md`, upstream gitlink.
- Upstream `upstream/geolith` is a pristine shallow clone of `https://github.com/libretro/geolith-libretro.git` at **`2dc88e3fa72ff404213a65d6eb5bb33baa687f73`**. `.gitmodules` is committed; nested clone has not been absorbed into `.git/modules`. Ordinary recursive clone should resolve the gitlink, but test that later. Do not alter upstream remote or commit local wrapper files in the upstream repository.
- Uncommitted/untracked implementation files: `rea/Build.ps1`, `rea/patch-core.mjs`, `rea/native/observer.h`, `rea/native/bridge.c`, and this `rea/HANDOFF.md`.
- Ignored partial outputs: `rea/obj/native/` contains patched `geo_lspc.c`, generated compiler `.cmd/.rsp` and many `.obj` files. `rea/bin/` is empty; no working DLL/executable exists.

## Core decision

Geolith is an actual AES/MVS C core with accessible native renderer/state/input hooks, BSD-3-Clause core plus preserved dependency licenses. New wrapper is MIT. MAME was considered but has a much larger build boundary. **BizHawk NeoGeoPort is Neo Geo Pocket/NGP, not AES/MVS.** See CORE-SELECTION.md for rationale/limits.

Chosen architecture (intended, not delivered): direct Geolith C DLL with renderer observer hooks, plus a local .NET host for execution thread, API/WebSocket, disk checkpoint/movie/evidence management and static workbench. .NET SDK 10.0.202 and 10.0.301 are installed. VS18 Community C++ toolchain exists at `C:/Program Files/Microsoft Visual Studio/18/Community`, MSVC 14.51.36231. No .NET host/frontend project has yet been written.

## Partial native code

`patch-core.mjs` applies exact-count anchors to a build copy of `src/geo_lspc.c`; never changes upstream. It captures actual sprite line-buffer palette/color/object/tile samples, FIX draw-time color/palette/tile samples, and compositor source flags. It also appends helper functions to preserve the precomputed sprite line-buffer scheduling state. These helpers are **not yet wired into the wrapper state format**.

`native/bridge.c` exports tentative `nr_open`, `nr_step`, video/evidence/memory/state/register/audio access and observer enable. It uses actual Geolith `geo_exec` and callbacks. No native execution has occurred; do not regard this as correct or complete. Review/check:

- Validate every NEO/ZIP region, minimum size and accessible BIOS/graphics storage **before** calling core functions that use them. Current checks need strengthening/reordering.
- Complete full state serialization including renderer scheduling buffers and frontend input/frame state; current `nr_restore` only uses upstream state and clears evidence. Preserve observer-only metadata separately; invalidate graphics until fresh complete render.
- Decide exact crop. Native framebuffer is 320×264, rendered scanlines 8–247. Libretro defaults use x=8/y=24, visible 304×224. Do not accidentally publish unrendered borders or claim a different crop.
- `geo_mem_ptr` VRAM/palette are host-endian words; convert to documented bus byte order for copied memory. Main RAM differs. No copied-domain format is implemented yet.
- RTC initializes from wall time; preserve initial state/RTC in movies for reproducible MVS replay.
- Audio raw mixer callback exists but no playback/frontend is implemented.
- Track inherited core accuracy limits: instruction-level CPU sync/best-effort fine raster timing; not cycle-perfect hardware claims.

## Successful commands

From workspace root:

```powershell
git clone --depth 1 https://github.com/libretro/geolith-libretro.git neogeo/upstream/geolith
```

From `neogeo/`:

```powershell
git init -b main
git remote add origin https://github.com/Bertzerker/NEO-REAPER-Geolith.git
git add .gitignore .gitmodules LICENSE README.md rea/CORE-SELECTION.md upstream/geolith
git -c user.name=Bertzerker -c user.email=133337+Bertzerker@users.noreply.github.com commit -m 'Bootstrap NEO REAPER Geolith integration and pinned core'
git push -u origin main
```

Remote operations worked with `require_escalated`. Parent verified existing gh authentication for Bertzerker outside sandbox. Do not expose credentials. git submodule status encountered a Git/MSYS sandbox access error; use escalation for Git operations that require its shell if needed.

## Last build and exact failures

From workspace root:

```powershell
./neogeo/rea/Build.ps1 -NativeOnly
```

This **failed**, exit 1. It found VS18, ran the checked observer patch and compiled most core source/object files, including patched LSPC and the wrapper. Failures:

1. `src/geo_cue.c` cannot find `dr/dr_flac.h`. The header is likely under the bundled libretro-common include directory, which is currently missing from include paths; verify its location, or exclude/de-scope CD with explicit resolved stubs rather than pretending CD support.
2. `src/z80/z80.h` requires GCC byte-order macros. For MSVC x64 define `__BYTE_ORDER__=1234`, `__ORDER_LITTLE_ENDIAN__=1234`, `__ORDER_BIG_ENDIAN__=4321` (verify correct intended values/use).
3. Compiler emitted numerous `/O*` warnings interpreting `/OUT:` and its path as compiler options. Generated response file has `/link` on a separate line from `/OUT:`; MSVC treats remaining options on that line differently. Put link options together on one response line or invoke linker separately. Also rename PowerShell `$args` in Build.ps1 to a task-specific variable to avoid its automatic-variable semantics.

WSL probing returned `Wsl/E_ACCESSDENIED` under sandbox. It was not needed after finding native MSVC; no WSL install or system configuration was changed.

## Next actions

1. Read this handoff and current brief/source, inspect `git status --short`, then fix Build.ps1 include/macros/link response and native wrapper validation. Next meaningful command after fixes: `./rea/Build.ps1 -NativeOnly` from the `neogeo/` directory.
2. Build a tiny original diagnostic cartridge and original minimal test-only firmware fixture (all original source). Core BIOS archive expects AES `neo-epo.bin`/`neo-po.bin`, or regional MVS names, plus `000-lo.lo`; MVS also loads `sfix.sfix` and `sm1.sm1`. Such a diagnostic fixture is **not a retail-compatible BIOS** and must be labeled accordingly. Do not download commercial BIOS/ROMs. No user assets were found in the workspace during a scoped filename search.
3. Execute core diagnostic and prove real renderer output/layer captures, RAM/input, state restore and observer on/off equality. Do not substitute a mock producer for native evidence.
4. Implement dedicated .NET host and common workbench/API, movies/checkpoints, leases, saved review, recovery/conflicts and bundle verification as required by brief. No such features exist yet.
5. Complete reproducible docs/tests/parity/validation; push reviewed source incrementally. No final compatibility, determinism or throughput claims are established.

## Process/sessions and blockers

No emulator, native host, HTTP server, browser test session, background worker or persistent unified-exec process was started by this agent. Clone/build commands finished. There are **no owned live process handles to adopt or terminate**. Do not kill sibling/user sessions.

No native tests, browser checks or performance runs have passed; no diagnostic was generated. Firmware/user cartridge assets remain an external compatibility-validation dependency. Current blockers are fixable compile configuration issues and the large unfinished implementation scope, not an established need for user approval. Preserve all partial files while the visible chat takes over.

## Continuation in the visible owner chat

The native build and workbench were subsequently implemented and validated in place. This document preserves the original migration state; current behavior, reports, asset-specific validation and publishing status are in README.md, SPEC.md, VALIDATION.md and ../INTEGRATION-NOTES.md. No user assets or private session evidence belong in source commits.
