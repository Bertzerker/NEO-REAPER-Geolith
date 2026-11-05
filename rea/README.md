# NEO REAPER - Geolith

Local Windows x64 AES/MVS investigation workbench. Uses actual Geolith native CPU, sound and renderer execution, with one emulation instance. Original source is MIT; upstream and bundled dependencies retain their own licenses. No commercial cartridge or firmware is supplied.

## Build and run

Prerequisites: PowerShell 7, .NET 10 SDK/runtime, Node.js, Visual Studio C++ x64 tools (VS18 works), Git. Python is only needed for original diagnostic generation/tests; use `C:/Users/Chris/AppData/Local/Python/bin/python.exe` on this workstation.

```powershell
cd 'C:/Users/Chris/Desktop/AI Code/REA Emulators/neogeo'
git submodule update --init --recursive
./rea/Build.ps1
./rea/Start-REA.ps1 -Diagnostic -Wait
```

Open [the workbench](http://127.0.0.1:8910/). It starts paused; use **Step frame** or **Play**. `-Diagnostic` generates original test-only firmware/reset vectors and an original cartridge. This fixture is not a retail BIOS replacement and establishes no commercial compatibility.

For locally supplied assets:

```powershell
./rea/Start-REA.ps1 -Rom 'C:/local/game.neo' -Firmware 'C:/local/neogeo.zip' -System AES -Region 0 -Port 8910 -Wait
```

`-System AES|MVS`, `-Region 0|1|2|3` (US/JP/Asia/Europe), `-Port`, `-DataDirectory`, `-Build` are configurable. Without `-Wait`, the launcher returns the process ID; it uses a hidden process with an argument list preserving spaces/Unicode. The launcher refuses occupied ports and never kills an existing session. The host binds only `127.0.0.1`. HTTP/WebSocket share 8910. Native execution/control are **in-process**; 8911 and optional 8912 remain reserved and unused, not fake connection indicators.

Firmware ZIP needs `neo-epo.bin` (AES except JP), `neo-po.bin` (AES JP), or `sp-u2.sp1`/`japan-j3.bin`/`sp-45.sp1`/`sp-s2.sp1` (MVS US/JP/Asia/EU); program BIOS at least 128 KiB. This adapter requires supplied `000-lo.lo` (64 KiB), `sfix.sfix` (128 KiB), and `sm1.sm1` (128 KiB) in both modes so system graphics/music mappings remain backed when selected. Missing/undersized components produce specific diagnostics before core load. ZIP files/components are bounded. Cartridge P/S/M/V1/C minimums are 1 MiB/128/128/128/128 KiB; V2 may be absent. Regions must be 128-byte aligned and exactly match the `.neo` header/file length. ZIP game sets are not accepted; conversion/decryption is outside this adapter. Default-board digital-controller cartridges are admitted; the pinned core's special-board NGH cases, alternate controllers and link games are rejected explicitly. Commercial assets have not been tested here.

## Inspect and control

Graphics: Composite, Sprite plane and FIX/text are native samples from one normal execution. Composite is a 304×224 crop at native (8,24) in the 320×264 buffer. The sprite plane is the last nontransparent sprite sample admitted at each position before FIX composition; it does not preserve every occluded sprite. FIX retains its raster-time color. Pixel inspection includes the recorded compositor winner and available tile/object/palette indices; fetch/CPU/DMA provenance and game collision geometry are unavailable. VRAM strip words are boundary memory diagnostics. Overlays enclose recorded admitted sprite coverage (possibly with gaps), not game objects or complete hardware rectangles.

Selecting a pixel pins the displayed capture. **Freeze inspection** leaves the machine running. The two 120-second leases used by this browser retain inspection and comparison captures beyond the eight-item history; the page renews them. **Set baseline** and **Read** show compatible copied-memory differences. Memory offsets/lengths are bytes; VRAM/palette words are exported big-endian, with VRAM word index = byte offset / 2. Main RAM preserves CPU bus byte order. Domains 8–13 read verified immutable P/S/M/V1/V2/C regions on demand without copying them into captures or exports. P bytes use original NEO word-swapped storage, distinct from CPU bus order; physical assets do not describe logical bank mappings. These domains require the matching currently loaded identity. Extra/cart/NVRAM storage may be unmapped in the chosen default board/machine; presence of a copied physical buffer does not establish a writable bus mapping.

Keyboard controller is opt-in and only active while the graphics canvas has focus. Arrows, Z/X/A/S, Enter/Shift map to directions, A/B/C/D, Start and AES Select/MVS Coin. Without controller mode, arrows edit inspected coordinates and Enter pins/inspects. Two digital pads are supported. Experiments supply complete masks at the start of each `geo_exec`, release unspecified buttons, and return requested/completed frame counts. Pause can interrupt long experiments after a completed frame. Service/test/reset/topology events are not implemented or recorded and have no control route.

Checkpoints are persistent full native state plus renderer sprite-line scheduling, sound resampler/FM cache scheduling and pointer-free 68000 execution context extensions. CPU/sound state, RTC, banks, RAM and timing exported by the core are retained; frontend held-pad state is restored. Exact asset/firmware/native DLL/host build/settings identity is mandatory. State files are trusted locally generated artifacts, not a portable or hostile-input state format. Restore invalidates live evidence until a complete fresh render; capture IDs remain unique across restore.

Movies start from an embedded current-state checkpoint, including RTC. Row 0 supplies both pad masks for the first `geo_exec` after that state; row hashes describe the completed output/native state/audio. Replay locks out live pad input, verifies every row, and stops on divergence. Load starts paused; use Play or Step. Saved movies are immutable versions. Save a recording before archiving its linked capture. No seek/branch/timeline editing is advertised. Emulated audio is generated and verified, but playback to the speakers is not implemented.

Comments retain immutable raster/memory evidence independently of live history and state. Markdown is sanitized locally, JSON is validated, Mermaid uses strict mode with directives/links rejected. Browser drafts recover after reload; changing notes requires save or explicit discard. Server revision checks reject stale writes. Saved review after restart does not restore the machine. Exports contain captures, images, identities, note revisions and a matching saved input movie when available; they exclude cartridge/firmware bytes. Verify without extraction:

```powershell
& 'C:/Users/Chris/AppData/Local/Python/bin/python.exe' ./rea/verify_bundle.py 'C:/local/evidence.zip'
```

## API

All mutations are loopback POST JSON with `X-REA-Token` from `GET /api/session`. Host and any Origin are validated. WebSocket requires the local Origin. Body limit 64 KiB; no arbitrary file/shell routes. Controls time out as **unknown outcome**: requery session before any retry.

```powershell
$base='http://127.0.0.1:8910'
$s=Invoke-RestMethod "$base/api/session"
$headers=@{'X-REA-Token'=$s.token}
Invoke-RestMethod "$base/api/control" -Method Post -ContentType application/json -Headers $headers -Body '{"action":"step","frames":60,"p0":16,"p1":0}'
$c=Invoke-RestMethod "$base/api/snapshot"
Invoke-RestMethod "$base/api/memory?capture=$($c.id)&domain=0&offset=0&length=128"
```

| Route | Behavior |
| --- | --- |
| `/api/session`, `/api/metrics` | Separate viewer/native/machine/evidence state, token, manifest, capability reasons, boundedness/cost counters |
| `/api/snapshot?capture=ID`, `/api/history` | Metadata; snapshot without ID explicitly requests latest fresh capture |
| POST `/api/lease` | `{capture,owner}` acquire/renew; `{owner,action:"release"}` releases; max eight leases total |
| `/api/live?capture=ID`, `/stream` | Lossless atlas PNG; WebSocket binary: LE uint32 JSON metadata length, UTF-8 metadata, PNG. One message per immutable atlas; latest-only, max four viewers |
| `/api/image?capture=ID&view=0` | Lossless visible image, views 0–2 |
| `/api/pixel?capture=ID&view=0&x=0&y=0` | Visible coordinates, native coordinates, captured color/sample and explicit unavailable fields |
| `/api/sprites?capture=ID`, `/api/registers?capture=ID` | Boundary controls + admitted coverage bounds; CPU PC/SR/D0–7/A0–7 plus palette/VRAM/animation/shadow/boundary timing registers |
| `/api/memory?capture=ID&domain=0&offset=0&length=128` | Domain IDs 0 main RAM, 1 Z80 RAM, 2 VRAM, 3 palette, 4 NVRAM, 5 cart RAM, 6 extra RAM, 7 memory card, 8–13 P/S/M/V1/V2/C immutable physical asset bytes; clipped reads, max 4096 bytes |
| `/api/diff?capture=ID&baseline=ID&domain=0&offset=0&length=128` | Same identities required; changed relative byte offsets |
| POST `/api/control` | `run`, `pause`, `step` (1–3600 frames, p0/p1 masks), `pad`, `speed` (0.25/0.5/1/2), `checkpoint-save` (name), `checkpoint-load` (id), `movie-record` (name), `movie-save`, `movie-play` (id), `movie-restart`, `movie-stop` |
| `/api/checkpoints`, `/api/movies`, `/api/inputs?id=ID&start=0&length=60` | Named library/compatibility and saved input rows (max 600 per request) |
| `/api/comments`, POST `/api/comments` | Latest note revisions; create `{capture,title,category,markdown,json,mermaid}`, update additionally `{id,revision}`; 409 on stale revision |
| `/api/export?capture=ID` | ZIP + SHA-256 manifest, offline verifier; no user cartridge/firmware |

Every evidence read except the explicit latest snapshot requires an ID. Expired/missing evidence returns 410. Saved IDs remain readable after process restart. The native owner queue is bounded to 16 commands; busy returns 429. The machine never waits for a browser. This first implementation allocates detached capture arrays instead of pooling; see measured limits and backlog in [VALIDATION](VALIDATION.md) and [SPEC](SPEC.md).

## Tests and troubleshooting

```powershell
& 'C:/Users/Chris/AppData/Local/Python/bin/python.exe' ./rea/native_test.py
& 'C:/Users/Chris/AppData/Local/Python/bin/python.exe' ./rea/native_test.py 1
& 'C:/Users/Chris/AppData/Local/Python/bin/python.exe' ./rea/integration_test.py ./rea/bin 8914
& 'C:/Users/Chris/AppData/Local/Python/bin/python.exe' ./rea/host_performance.py 180
& 'C:/Users/Chris/AppData/Local/Python/bin/python.exe' ./rea/performance_test.py 180
```

Tests start/stop only their owned processes and use ignored isolated data under `rea/test-output/`. Do not run builds while a host has the DLL loaded. Close only that owned process first. If HTTP.sys reports `The handle is invalid` in the Codex restricted execution sandbox, run the launcher from ordinary local PowerShell or the approved outside-sandbox execution context; this was required for loopback validation here. No URL ACL, firewall or system settings are changed by the build/launcher. Native code is not a security sandbox for hostile cartridge execution.

See [SPEC](SPEC.md), [VALIDATION](VALIDATION.md), [PARITY](PARITY.md), [CORE-SELECTION](CORE-SELECTION.md), and [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md).

For the verified locally supplied Metal Slug set, `convert_mslug.py ./roms/mslug.zip ./roms/mslug.neo` makes a checksum-verified ignored local derivative. Then use `-Rom ./roms/mslug.neo -Firmware ./roms/neogeo.zip -System MVS`. The converter is specific to the known set and rejects unknown component hashes. `game_workbench_test.py` exercises the production checkpoint/movie API without exporting private game evidence.
