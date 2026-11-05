# Dependency and attribution inventory

NEO REAPER integration/diagnostic/conversion/test code is MIT, as stated per file and in the root LICENSE. Third-party licenses remain applicable; the integration license does not relabel them. No NES/Genesis GPL implementation was copied. Sibling projects were inspected as read-only design references. UI libraries were copied from their installed upstream distributions, not from GPL workbench code.

| Component | Source/revision | License / distribution |
| --- | --- | --- |
| Geolith C AES/MVS core | `https://github.com/libretro/geolith-libretro`, `2dc88e3fa72ff404213a65d6eb5bb33baa687f73` git submodule | BSD-3-Clause; Rupert Carmichael and contributors; upstream file notices retained |
| Geolith CUE/disc support compiled into DLL | Pinned source, cartridge frontend does not expose CD loading | BSD-3-Clause, including Romain Tisserand notices; CD capability remains unsupported |
| Musashi 4.60 | Pinned `src/m68k/` | MIT, Karl Stenerud; source header retained and notice copied |
| Jolly Good Z80 adaptation | Pinned `src/z80/` | MIT, Nicolas Allemand, Rupert Carmichael, rofl0r; source notice retained |
| YMFM C adaptation | Pinned `src/ymfm/` | BSD-3-Clause, Aaron Giles/Rupert Carmichael; file notices retained |
| miniz | Pinned `deps/miniz/` | MIT; bundled LICENSE retained |
| Speex resampler | Pinned `deps/speex/` | BSD-style notice in COPYING retained; raw audio mode used |
| dr_flac | Pinned `deps/libretro-common/include/dr/dr_flac.h` | MIT No Attribution option; original dual-license footer retained/copied; compiled via CUE source |
| Marked | Vendored upstream `marked.umd.js`, 18.1.3 | MIT plus bundled Markdown attribution; full license in `licenses/marked.txt` |
| DOMPurify | Vendored upstream `purify.min.js`, 3.4.17 | Apache-2.0 or MPL-2.0; Apache-2.0 option; full license in `licenses/dompurify.txt` |
| Mermaid | Vendored upstream standalone `mermaid.min.js`, 12.3.0 | MIT plus bundled dependencies; original bundle notices retained; full Mermaid license and installed dependency notices copied |
| .NET 10 | Installed Microsoft SDK/runtime, framework-dependent host | No runtime binaries vendored or committed; uses installed runtime |
| MSVC x64 CRT | Installed VS18 toolchain, static `/MT` build | Microsoft toolchain redistributable conditions apply to locally built native binaries; no binary release/package is published here |

`licenses/` preserves notices for binary users. `licenses/ui-dependencies.json` records the installed UI dependency versions and declared licenses; `licenses/ui/` intentionally retains an overinclusive set of installed UI dependency notices, including build/type dependencies not present in the standalone bundles. No external CDN is required. The three vendor files are committed for an offline build and preserve their upstream license comments. To refresh, obtain these exact versions from the official npm registry, copy their standalone distributions and all applicable notices, then rerun browser preview/security checks. The update is a deliberate source change, not a build-time network fetch.

Upstream zstd/libretro-common/CHD code remains in the submodule with its own notices. The direct DLL build does not compile CHD/zstd or the libretro frontend. Their presence in source is not advertised runtime CD support.

Original diagnostic assets are generated from `diagnostic.py` under MIT. Commercial ROMs/firmware, locally converted user cartridges, private snapshots, user movies and workbench session files are ignored and are not licensed or redistributed by this project. The optional Metal Slug converter contains original code and checksum/mapping facts referenced to MAME's BSD-3-Clause driver; it includes no game bytes or copied MAME implementation.
