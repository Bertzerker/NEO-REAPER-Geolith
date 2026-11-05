# Interface parity — source review and local validation, 5 November 2026

References inspected: current root/unified proposal, NES SPEC/server/annotations, Genesis SPEC/server/movie/frontend and SNES SPEC/server/bridge/frontend/notes. Siblings remain read-only. Their feature descriptions below are source observations, not new emulator certifications. Tests here use actual Geolith; none use a mocked producer to claim native success.

| Feature | NEO REAPER - Geolith | Current sibling reference |
| --- | --- | --- |
| Execution, speeds, bounded inputs | UI/API; 1–3600 frames; interruptible at frame boundary; .25/.5/1/2× | NES/SNES bounded inputs; Genesis frame/live-pad transport |
| Connection axes | UI/API separate viewer/native owner/machine/valid capture/movie | Current SNES/Genesis now include strengthened pinned-selection/session state, beyond the initial review |
| Captured graphics | Composite/sprite/FIX from one native execution; source winner in pixel API | NES BG/OBJ, Genesis planes/window, SNES BG1–4/OBJ; hardware meanings differ |
| Pin/freeze, compatible baseline | UI/API, two finite renewable viewer leases; eight-item history | Current siblings expose leases/pinning with adapter-specific bounds |
| Memory and diffs | UI/API, eight copied physical buffers + six verified on-demand immutable asset regions; explicit units/order | All three have copied memory; exact domain/bank semantics differ |
| Hardware overlays | UI/API admitted sprite sample coverage; boundary strip controls | NES OAM, Genesis SAT, SNES OAM diagnostics; no shared collision inference |
| Checkpoints | UI/API named disk state, exact identity, fresh-render invalidation | SNES disk; NES slots; Genesis native/movie-start semantics differ |
| Input movies | UI/API current full-state start, immutable saved versions, state/video/audio per-row verification; restart/cold replay | NES FM2/TAS editor, Genesis RMTAS, SNES BK2/TAStudio are distinct formats |
| Timeline editing | Unsupported | Native FCEUX TAS Editor and BizHawk TAStudio remain stronger |
| Comments | UI/API title/category/Markdown/JSON/Mermaid; browser draft recovery, discard, optimistic revisions | Current NES/SNES/Genesis have evolving revision/pinning implementations; no copied GPL persistence code |
| Saved review | UI/API after eviction and restart without machine restore | Current NES/SNES/Genesis saved-review adapters have different archive semantics |
| Evidence export | UI/API lossless images, copied memory/sample data, identities, revisions, linked saved movie + SHA-256 verifier | Existing snapshot/note/movie exports differ; no claim of cross-core movie compatibility |
| Live transport | One WebGL texture, lossless atlas WebSocket, latest-only bounded delivery | Genesis/SNES atlases; NES bounded on-demand transport |
| Trace/access watches | Unsupported, explicit reason | NES offers native bounded traces/watches; other cores' native tools differ |
| Audio | Emulated/generated/verified in movies; no speaker playback | Existing native emulators have normal playback; this remains a usability gap |
| Commercial configurations | US MVS Metal Slug local test recorded separately in VALIDATION; AES commercial unverified | Sibling results concern other systems and cannot certify Geolith |
| Layout/accessibility | Shared tokens/order; tested effective browser dimensions recorded in VALIDATION | No general accessibility conformance claim |

Performance separates native frame rate, capture copying/encoding/delivery and unique browser presentations. One smooth fixture or short title-specific replay does not establish universal hardware accuracy/determinism.
