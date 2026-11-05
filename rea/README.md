# NEO REAPER - Geolith

Local Windows x64 AES/MVS investigation workbench. Uses actual Geolith native CPU, sound and renderer execution, with one emulation instance. Original source is MIT; upstream and bundled dependencies retain their own licenses. No commercial cartridge or firmware is supplied.

## Run the Windows release

Extract the [v0.1.0 Windows x64 release](https://github.com/Bertzerker/NEO-REAPER-Geolith/releases/tag/v0.1.0). Run `rea/Start-REA.ps1` with PowerShell 7 and the .NET 10 x64 runtime. The launcher examples below work from the extracted folder. The release includes the Geolith core, the workbench and the optional transport for an installed RetroArch.

## Build from source

The source build requires PowerShell 7, the .NET 10 SDK, Node.js, Visual Studio C++ x64 tools and Git. Python is used for diagnostic generation, the converter and the test scripts. Pass `-Python` to select an installed interpreter for the diagnostic launcher.

```powershell
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

Pass `-RetroArch 'C:/path/to/retroarch.exe'` to play in RetroArch and inspect the same live session in the browser. Open RetroArch's Quick Menu with **F1** and use **Record Replay**, **Halt Record/Replay**, **Play Replay**, or **Save States**. Enable **Show Replay Controls** in RetroArch's Quick Menu visibility settings if needed. The browser keeps its frame, sprite and memory inspection tools while RetroArch supplies controller input and runs the replay. Close RetroArch to end the linked session. Keep the same NEO REAPER build, cartridge, firmware and machine settings when reopening a native replay or savestate.

The firmware ZIP needs a program BIOS of at least 128 KiB. Use `neo-epo.bin` for AES outside Japan, `neo-po.bin` for Japanese AES, or `sp-u2.sp1`, `japan-j3.bin`, `sp-45.sp1` or `sp-s2.sp1` for MVS US, JP, Asia or Europe, respectively. Both modes also require `000-lo.lo` (64 KiB), `sfix.sfix` (128 KiB) and `sm1.sm1` (128 KiB). The adapter validates ZIP components and `.neo` cartridge regions before loading. It supports default-board digital-controller cartridges. The validation report covers the original fixture and one locally supplied US MVS Metal Slug setup.

## Inspect and control

Composite, Sprite plane and FIX/text come from one normal execution. Composite is a 304×224 crop at native (8,24) in the 320×264 buffer. Sprite plane shows the last admitted sprite sample at each pixel before FIX draws. FIX retains its raster-time color. Pixel inspection includes the compositor winner and available tile, strip and palette indices. The bounds overlay encloses the winning sprite pixels before FIX, with possible gaps. VRAM strip words show memory at the frame boundary.

Choose **Sprite priority** to freeze the displayed frame while the machine keeps running. **Previous strip** and **Next strip** visit strips with pixels in the capture, including pixels covered by later strips. Enter **First strip** and **Last strip**, then choose **Show range** to compose those strips in draw order. Lower IDs draw first. A character or background can span multiple chained strips. If you inspect a sprite pixel first, the priority view starts with that pixel’s strip. **Return to live** takes you back to the running Composite view.

In **Composite** or **Sprite plane**, choose **Pick sprite group**, then click a visible character pixel. The picker freezes the frame and shows the selected group on a transparent background. It follows hardware chains and also groups consecutive strips where opaque pixels meet at a seam. Separate pieces can require adjusting the strip range. Earlier captures support strip ranges and need a fresh frame for group picking.

In **Composite** or **Sprite plane**, choose **Pick and follow** and click a visible sprite to open its helper and track that instance. In a helper view, scroll over overlapping sprites to preview the matches at that pixel, then click to follow the preview. Click again to show all matches. In **Sprite plane**, scrolling over a pixel opens **Sprite priority** at the strips drawn there. Scroll down to step back in priority and up to step forward. **Return to live** switches back to Composite.

In **Composite**, **Sprite plane** or **FIX/text**, pick an opaque pixel and choose **Add to catalog**. The inspector shows the resolved tile, palette, palette bank and hardware locator from that capture. Save a new **Sprite tile**, **FIX tile** or **Sprite group**, or append the selector to an existing entry. Search the **Sprite & tile catalog** tab to review selectors, edit names, labels and notes in JSON, or **Show all matches**.

The catalog uses schema version 1 and the helper's cartridge SHA-256, system and region. Existing `sprites` entries are sprite groups. The optional `tiles` array takes entries such as `{"id":"marker","name":"Marker","source":"fix","selectors":[{"tile":"0x1","palette":2,"bank":0}]}`. A tile entry uses `sprite` or `fix` as its source and matches only the listed resolved tile pixels. `palette` and `bank` are optional. Catalogs created in the workbench use ignored `catalog-*.json` files in the configured helper directory. **Reload JSON** applies hand edits and imports from that directory. Saves check the file revision.

Sprite tiles are 16×16 tiles in hardware strips. Their archived attribution covers the winning sprite sample before FIX. Priority inspection can reveal covered strip writes, whose tile IDs are unavailable in the archived raster. FIX tiles are 8×8 cells with captured tile and palette identity. A FIX follow stays on its chosen cell. Sprite tile follow uses visible occurrences within a strip and motion continuity. When repeated rows cannot be separated or two matches become ambiguous, the view asks you to pick again. A missing candidate gets up to six captured frames to reappear. Loading state or restarting a replay clears the selection.

Choose **Attach to comment** while displaying visible matches or a followed instance. The saved observation includes the exact capture, isolated pixels, catalog revision and selectors, selection coordinates or bounds and any Geolith input movie revision and row. **Inspect saved frame** reopens the stored frame and attachment while the emulator or replay playhead stays where it is. Evidence bundles hash the attachment and its movie revision. Geolith's in-process input movies use the same track viewer as live play. RetroArch runs its own movie controls through the native frontend.

Open the **Sprite helpers** tab to create, edit or delete per-game JSON files in `rea/sprite-helpers` (or the directory passed to `Start-REA.ps1 -SpriteHelpers`). Browser saves and deletions update the Graphics picker while the emulator is running. **Reload JSON** reads changes made in an external editor. The host reads files at startup and when applying a browser save or deletion.

Helpers match by `cartridgeSha256`, `system` and `region`. Add to the `sprites` array as you identify more characters or objects. Give each sprite a unique `id`, a display `name` and one or more `selectors`. Each selector accepts an inclusive `firstTile`–`lastTile` range with an optional `palette`, an inclusive `firstStrip`–`lastStrip` range (1–381), or both ranges. Values can be decimal numbers or `0x`-prefixed strings. All fields in a selector must match. Matching samples are composited in hardware draw order, including pixels covered by strips outside the selection.

Choose **New from strip range** after inspecting a range in **Sprite priority** to seed the JSON with those strip IDs. Games can reuse strip slots across frames. The included `metal-slug-us-mvs.json` tracks Marco across palette changes and uses priority and tile ranges for the enemy soldier.

Selecting a pixel pins the displayed capture. **Freeze inspection** leaves the machine running. The two 120-second leases used by this browser retain inspection and comparison captures beyond the eight-item history; the page renews them. **Set baseline** and **Read** show compatible copied-memory differences. Memory offsets/lengths are bytes; VRAM/palette words are exported big-endian, with VRAM word index = byte offset / 2. Main RAM preserves CPU bus byte order. Domains 8–13 read verified immutable P/S/M/V1/V2/C regions on demand without copying them into captures or exports. P bytes use original NEO word-swapped storage, distinct from CPU bus order; physical assets do not describe logical bank mappings. These domains require the matching currently loaded identity. Extra/cart/NVRAM storage may be unmapped in the chosen default board/machine; presence of a copied physical buffer does not establish a writable bus mapping.

Keyboard controller is opt-in and only active while the graphics canvas has focus. Arrows, Z/X/A/S, Enter/Shift map to directions, A/B/C/D, Start and AES Select/MVS Coin. Without controller mode, arrows edit inspected coordinates and Enter pins/inspects. Two digital pads are supported. Experiments supply complete masks at the start of each `geo_exec`, release unspecified buttons, and return requested/completed frame counts. Pause can interrupt long experiments after a completed frame. Service/test/reset/topology events are not implemented or recorded and have no control route.

Checkpoints are persistent full native state plus renderer sprite-line scheduling, sound resampler/FM cache scheduling and pointer-free 68000 execution context extensions. CPU/sound state, RTC, banks, RAM and timing exported by the core are retained; frontend held-pad state is restored. Exact asset/firmware/native DLL/host build/settings identity is mandatory. State files are trusted locally generated artifacts, not a portable or hostile-input state format. Restore invalidates live evidence until a complete fresh render; capture IDs remain unique across restore.

Movies start from an embedded current-state checkpoint, including RTC. Row 0 supplies both pad masks for the first `geo_exec` after that state; row hashes describe the completed output/native state/audio. Replay locks out live pad input, verifies every row, and stops on divergence. Load starts paused; use Play or Step. Saved movies are immutable versions. Save a recording before archiving its linked capture. No seek/branch/timeline editing is advertised. Emulated audio is generated and verified, but playback to the speakers is not implemented.

Comments retain immutable raster/memory evidence independently of live history and state. Markdown is sanitized locally, JSON is validated, Mermaid uses strict mode with directives/links rejected. Browser drafts recover after reload; changing notes requires save or explicit discard. Server revision checks reject stale writes. Saved review after restart does not restore the machine. Exports contain captures, images, identities, note revisions and a matching saved input movie when available; they exclude cartridge/firmware bytes. Verify without extraction:

```powershell
& 'C:/path/to/python.exe' ./rea/verify_bundle.py 'C:/local/evidence.zip'
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
& 'C:/path/to/python.exe' ./rea/native_test.py
& 'C:/path/to/python.exe' ./rea/native_test.py 1
& 'C:/path/to/python.exe' ./rea/integration_test.py ./rea/bin 8914
& 'C:/path/to/python.exe' ./rea/host_performance.py 180
& 'C:/path/to/python.exe' ./rea/performance_test.py 180
```

Tests start/stop only their owned processes and use ignored isolated data under `rea/test-output/`. Do not run builds while a host has the DLL loaded. Close only that owned process first. If HTTP.sys reports `The handle is invalid` in the Codex restricted execution sandbox, run the launcher from ordinary local PowerShell or the approved outside-sandbox execution context; this was required for loopback validation here. No URL ACL, firewall or system settings are changed by the build/launcher. Native code is not a security sandbox for hostile cartridge execution.

See [SPEC](SPEC.md), [VALIDATION](VALIDATION.md), [PARITY](PARITY.md), [CORE-SELECTION](CORE-SELECTION.md), and [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md).

For the verified locally supplied Metal Slug set, `convert_mslug.py ./roms/mslug.zip ./roms/mslug.neo` makes a checksum-verified ignored local derivative. Then use `-Rom ./roms/mslug.neo -Firmware ./roms/neogeo.zip -System MVS`. The converter is specific to the known set and rejects unknown component hashes. `game_workbench_test.py` exercises the production checkpoint/movie API without exporting private game evidence.
