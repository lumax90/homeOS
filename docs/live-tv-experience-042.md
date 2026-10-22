# Live TV workspace and public/IPTV sources — 0.42.0

Implemented **2026-10-22** for **EXT-01/02/05**, **CLI-01/05**, **USR-03**, **OPS-03/04**. User requested a Live TV refresh consistent with the new Music/Games direction, public free channel lists and support for IPTV links.

## Presentation and workflow

- Live TV owns its page heading and gear menu. The content-first workspace includes channel cards, real now/next programme information when available, guide view, private favorites/recent channels and existing DVR recordings/rules. Channel/group search and source filters use 60-item server pages; long text is bounded. Mobile controls and the shared controller focus system are retained.
- Settings group **IPTV sources** and **Free lists**. Sources support **M3U/M3U8 URL**, uploaded playlist text and a **single HTTP/HTTPS channel stream**. Provider `get.php`-style credential-bearing M3U URLs are stored protected, without returning saved secrets to the form. Empty saved address fields preserve the prior connection; source edits use revisions. XMLTV, LAN tuner access, concurrent-source limits and radio-only mode are explicit advanced options.
- Each source retains enable/refresh/edit and individual channel enable controls. The channel guide preserves EPG-ID override, ordering, age settings, scheduled recording, pre/post padding and recurring recording rules. Missing XMLTV data is displayed as missing data.
- Live playback uses the installed Video.js 10.1.0 native Video component/skin over the existing hls.js/MSE-first or native-HLS transport. It opens in a large player with channel rail/search, previous/next channel, live-edge return, fullscreen and a miniplayer. Shrinking keeps the same media element alive. The shared R3 dock shortcut reaches Live TV too.

## Public sources — real text contracts

The reviewed provider is **IPTV-org**, using its published playlists. Twelve explicit choices cover Türkiye, US, UK, Germany, France, Azerbaijan, News, Music, Documentary, Kids, Sports and Relax. **Preview** reads names/counts/groups only. **Add** idempotently creates the selected shared source and queues bounded metadata sync. No preset is auto-added and no broadcast starts before Watch.

Actual C# adapter probes read all twelve current public playlist texts, with **zero stream or logo requests**. Supported HTTP/HTTPS counts at verification: **Türkiye 181, US 1,483, UK 295, Germany 284, France 210, Azerbaijan 19, News 1,016, Music 781, Documentary 268, Kids 429, Sports 474, Relax 9**. Counts overlap between lists and are not a unique-channel or playback-availability guarantee.

The public probe uncovered three concrete integration details:

1. The UK's published playlist uses **`countries/uk.m3u`**, not `gb.m3u`.
2. One feed contains literal **`No response`** as an optional referrer. Invalid optional URL headers are omitted, never forwarded.
3. News/Music contain RTMP/SRT entries alongside HTTP streams. Unsupported network transports are omitted; HTTP fragments (client hints, not request targets) are removed. Unsafe file/script URLs still fail validation. UI identifies the imported transport scope as HTTP/HTTPS.

Evidence: `artifacts/live-public-playlists-v042.json`; `tools/Home.LiveProbe` records each public list's byte count, hash, supported entries and header count. It does not register real sources or open broadcasts in the user's server.

Sources reviewed: [IPTV-org README](https://github.com/iptv-org/iptv), [published playlists](https://github.com/iptv-org/iptv/blob/master/PLAYLISTS.md), [actual LICENSE](https://github.com/iptv-org/iptv/blob/master/LICENSE), [API documentation](https://github.com/iptv-org/api). Repository LICENSE is **Unlicense/public-domain dedication**; the README's CC0 badge is not a license for broadcaster video/logo payloads. No provider code or full dataset dump is embedded. EPG feeds are optional user XMLTV URLs; no universal free hosted EPG service is claimed.

## Protocol/data boundaries

- Schema **42→43** adds private channel preferences/history, public-preset identity, encrypted per-channel header/logo fields, search text and live client revisions. Existing source/channel IDs, encrypted connections, channel overrides and DVR rows are retained.
- M3U parsing supports quote-aware comma separation, single/double-quoted attributes, per-channel VLC User-Agent/Referer/Origin hints, URL-suffix hints and redirect-relative addresses. Headers reset for each channel; only the bounded allowlist can reach outbound requests. HLS media/master manifests are distinguished from channel libraries and direct the user to single-channel input.
- Text/list/image sizes, row counts, HTTP readers and cache sizes are bounded. The existing public-address-pinned HTTP transport, explicit LAN option and HTTPS redirect policy remain in force. Server-side logo caching returns same-origin image URLs; new input, stream, logo and header secrets remain protected in state.
- Source snapshots publish only after a nonempty valid parse and a current durable job/source revision. Failed refreshes retain the previous catalog, favorites and manual channel options. Queue pressure does not turn an already-saved source into a failed creation; the existing source scheduler refills later.
- Live TV shares browser intent/revision fences and account/device/global slots with local media and radio. Same-client channel changes close prior production before starting the next source, even with a one-tuner source. Late/cancelled starts cannot resurrect older playback. Close/logout/expired access cancels owned work and releases its cache; restart recovers only owned marked work directories.
- The original FFmpeg CPU H.264/AAC, up-to-1080p HLS production and 30-minute rolling window are retained; publication uses temporary files. No new GPU/codec capability is claimed. The input relay now resolves HLS segments/keys/maps against the final redirect URI, applies per-channel headers, validates ranges, handles short initial reads, and cancels stalled upstream reads when removed. DVR uses the same header-aware relay and existing idempotent publication/recovery.

## Verification

- Initial focused gate: **61 backend/native checks, zero skipped**, covering source secrecy, idempotent presets, quote/URI/header parsing, failure preservation, profile age/favorites/history, pagination, migration, CSRF/actor binding, shared quotas, DVR/restart and existing radio/playback boundaries.
- An original, locally generated 12-second fixture was actually passed through **Kestrel loopback relay → owned FFmpeg producer → HLS**. `ffprobe` verified H.264/AAC segments; cancellation removed the owned work directory, released the lease and preserved the input bytes. No internet television/film/music/game payload was used.
- Node checks cover live-window bounds/guide progress, shared HLS routing, existing media bridge, all semantic UI text/selects, controller gameplay/modal ownership and navigation. Visual/device acceptance is user-owned; no automated browser suite or screenshot polish was run.
- Final parser/protocol gate: **18 checks passed, zero skipped**. Together with the initial gate this covers **63 distinct backend/native cases**. **74 Node checks passed, zero skipped**. The HLS-source routing assertion was tightened to stop at statement boundaries rather than treating a nearby `if` and later `canPlayType` call on the same source line as one condition.
- Evidence: `artifacts/test-results/live-tv-experience-v042.trx`, `artifacts/test-results/live-tv-experience-v042-final.trx`, `artifacts/live-tv-experience-v042-node-release.tap`, and the public text receipt above.

## Signed publication — 2026-10-22 local verification

Final frontend/worker/native-reader/Vite/PWA build passed: **80 public PWA files**, shell hash **`d294f4b051e6c494`**. Signed **0.42.0 / schema43** is live at **http://localhost:5080/live**, PID **19704**. Package `artifacts/packages/home-0.42.0-win-x64.zip`: **921 verified files / 116,740,440 bytes**. Trusted-key fingerprint remains **`b6ebb7e0a2b2821afd3143dc3bd9c8b666189d9a41953aa6657cf60d789b566b`**. Rollback **0.41.1**, backup **`c226b5a6c1804d13b3d6db9860969f64`**.

The streaming updater checked playback, games, transfers, pending restore and active/finalizing/publishing DVR work before applying. Owner authentication survived; no transfer was resumed. Read-only post-update verification confirmed **12 public-list descriptors**, the live **181-channel Türkiye playlist preview**, the new workspace/settings/guide/lazy player assets, and migration **42→43** with **zero foreign-key violations**. The verifier follows the built lazy JS import graph, including template-literal import paths; it does not execute the client or open broadcasts.

Existing media, saved queues/history, music data, Live TV/DVR configuration projections, **4 ROMs / 10 game-state rows / 10 saves**, native enabled state and **3 paused transfers** were preserved. There were **0 configured TV sources/channels** before and after publication: sources are offered for explicit user selection, not silently installed. Verification started **0 broadcasts, 0 recordings, 0 logo requests and 0 acquisition commands**.

Evidence: `artifacts/update-live-0.42.0.json`, `artifacts/live-tv-data-before-v042.json`, `artifacts/live-tv-experience-v042.json`, `artifacts/native-preview-data-before-v042.json`, `artifacts/live-native-v042.json`. Browser/device/real-provider acceptance remains open. All changes are local; no commit or push was made.
