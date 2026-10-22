# Native Home host — GPU, peripheral controls, saves and latency

2026-10-20 · GAM-04/05/10/11/13. User requests finishing the Games flow and the existing ~150 ms lag, with no repeated permission questions, browser suites or screenshot polishing. The work below is actual integration/code, not catalog labels promoted to compatibility evidence.

## Native host implementation

- Same released **RetroArch 1.22.2 `69a4f0e…`** and original scoped **cfb12043** held-input receiver backport. Additional changes are explicitly called **Home host adapter `home-host-1`**, not passed off as unmodified upstream or a nightly frontend. Mature RetroArch, its FFmpeg recorder and MediaMTX still own emulation, graphics, encoding, muxing and WebRTC. No emulator, codec, streaming or virtual-HID engine was written.
- Windows **CreateDesktop / CreateProcess** runs graphics contexts on a private, never-selected desktop. Only the owned child's GPU framebuffer is recorded. CreateProcess starts suspended, is attached to the existing kill-on-close job, inherits only explicit stdin/stdout/stderr handles, then resumes. Native logs are continuously drained into bounded tails. No personal desktop capture, display switching or new display/input driver.
- **OpenGL, OpenGL Core, Direct3D 11 and Vulkan** are built from the pinned frontend source. Final executable: **12,195,177 bytes**, SHA-256 **`c1b9dd158da2a41db79a774c0ec31865b889433b196800099a4c9ad1cfb854f9`**. Verified binaries, dependency files and proofs are copied into `games/native-hosts/<sha256>/` under Home's state directory; the new runtime does not depend on keeping the build cache. Libretro API headers are the pinned released ones; additional Vulkan-Headers API text is pinned to **`acbb9391c0a597272800cd3f4363661c00a27c77`**. Source files with git executable bit were correctly treated as C/C++ text, not dropped from the GLSLang build.
- Home adapter binds native command and Remote RetroPad sockets **directly to 127.0.0.1**. The application no longer needs a new firewall/UAC rule for each rebuilt host. This is a source-level loopback bind plus actual listener verification, not disabling authentication or ignoring an exposed listener. Existing GBA profile and firewall rules are retained; new host launch still has owner/login/ticket/epoch validation.

## Controls and game lifecycle

- Four browser gamepad slots keep their identity across disconnects; complete 16-button RetroPad, both sticks and analog button pressure (including partial trigger presses). New runtime Remote RetroPad uses **62420–62423**, avoiding the TS port; command **62406** stays separate. The original single GBA host port remains unchanged in its retained profile.
- Libretro pointer/touch, relative mouse/wheel/buttons, keyboard callback/state, and lightgun coordinates are relayed to existing libretro interfaces. They never become desktop-level input injection. Browser pointer mapping respects letterbox/integer/fill sizes. Gameplay suppresses Home navigation, button/pointer/keyboard edges remain frame-visible, stable input keeps a bounded keepalive. Motion samples coalesce without erasing press/release or accumulating a pointer jump on neutral release. Physical key codes prevent shifted key-up events from leaving a key pressed. Computer systems use their keyboard directly, including Escape; Shift+Escape opens Home's menu.
- WebSocket input edges wake the relay **immediately**, with a **4ms** fallback timer; HTTP fallback remains serialized and bounded. Only a subsequent contradictory press/release is kept apart for the core's own reported frame period so a brief tap cannot be swallowed. Initial neutral samples cannot insert an extra input frame. Core timing is queried through an upstream-API facade, not a hardcoded speed limit. Database session checks and firewall RPC are outside per-message WebSocket input; the existing lease monitor and authenticated handshake enforce ownership, expiry and revocation.
- The actual running core provides controller types and options. Home's pause menu exposes these values, with enum-bound setters, per-profile persistence and locked managed save paths. Geometry/renderer/restart options apply at next launch so changing layout does not invalidate a live recording. In-core settings are data from the core, not invented controls.
- Disc count/index are queried from actual libretro disk callbacks, with eject/select/insert and independent read-back verification. Selected disc is included with the checkpoint and restored before loading state. CUE/M3U/GDI import remains reference-checked and owned.
- Actual serialization size determines whether an instant state exists. Cores without savestates can still save their native files; an empty save directory is never reported as a fabricated checkpoint. Standard and nested Citra/Azahar/PSP/Dolphin/PCSX2 saves, in-game NAND/HDD changes and memory cards remain profile/core partitioned. Large files use immutable per-user content-addressed blobs; large manifests use a bounded hash-verified reference.
- Native save paths, callback results, conflict protection, CAS and restart journals are explicit. Normal exit closes the core before snapshotting long-lived native save files. Crash recovery runs in the background before new native launches and keeps on-disk game saves without reusing a stale pre-crash instant state. A newer checkpoint is never overwritten by stale recovery. A lost exit acknowledgement can be retried by the owning login. ROM source copies are preserved.
- Writable computer disk images and flat multi-disk sets run from a separate checkpointed `saves/writable-media/` copy. In-game floppy/HDD writes never change the imported original. Known emulator shader/cache folders are excluded; a game's own nested `cache` directory remains part of its save tree.

## Catalog/setup

- Official emulator system IDs extend the import list instead of maintaining a 35-system hard stop. Existing canonical system IDs/GBA saves remain stable. Unknown extensions remain tied to the selected real core/system; no arbitrary host executable importer.
- Core installation remains an explicit Home button using the disclosed upstream Windows build/license. No automatic bulk game/core downloads, no implicit ROM/BIOS acquisition. Software/core runtime compatibility is not inferred from an image or a catalog count; required firmware and actual launch errors remain visible. Systems with no upstream Windows package remain correctly unavailable on this host.
- Firmware and resource **folders**, including short path components such as Dolphin `GC`, are accepted through bounded streaming. Citra/Azahar Mii/system data, PCSX2 BIOS folders, PPSSPP/Dolphin resources and Cemu disc keys/fonts use declared managed roots. WUD/WUX require the user's keys; WUA imports do not. Original firmware payloads are immutable; mutable save copies are restored before seeding missing files. User supplies these files. Current PCEE2 documentation marks its embedded resources optional; the runtime override records that instead of requiring an unnecessary upload.
- Native state size, render path and core options are runtime capabilities. The existing verified released **mGBA 0.10.5** DLL is retained. The new host changes transport/input/graphics, not GBA emulation; **`native-mgba-7abcc80ad4dac4a5e6f46e9d0279ac4d`** and the existing software-core save partitions are retained. New GPU-core save partitions likewise do not fork merely because the recording host changes. Core changes retain independent save partitions.

## Measured latency work

The original recorder's mux/IO buffering and GPU readback scheduling were real delay sources. The Home adapter forwards supported FFmpeg mux options, explicitly flushes low-delay mux/IO, reads the **current** GPU frame and avoids a multi-frame readback ring. GPU recording is armed when requested instead of checking a recording flag that is initialized later.

- Early simplified GPU diagnostic builds measured **13–16ms** input datagram → live FFmpeg decoded color, versus roughly **93–113ms** before the low-delay changes. These early samples are not the final expanded build's end-to-end latency.
- The later diagnostic decoder sometimes added **40–80ms** even while the encoded frame had already arrived. `tests/native/EncodedVideoTiming.cs` now correlates captured PES arrival timestamps with frames decoded independently by FFmpeg. This separates actual host delivery from the diagnostic decoder's buffering; neither number is browser latency.
- Final-build input → complete encoded-frame arrival: **OpenGL 13.8 / 20.2 / 20.9ms**, **OpenGL Core 14.0 / 23.0 / 22.1ms**, **Direct3D 11 29.3 / 37.3 / 35.7ms**, **Vulkan 17.5 / 17.6 / 8.7ms**. Vulkan uses a two-image swapchain. Earlier duplicate VSync config experiments were not authoritative because RetroArch uses the first occurrence; final configuration has no duplicate VSync setting.
- Browser requests both standard `jitterBufferTarget=0` and older supported `playoutDelayHint=0`; actual minimum/target/buffer/decode counter deltas are shown. A preference is not a guaranteed zero buffer. The **~150ms real user end-to-end measurement has not been re-measured in a user browser**; isolated numbers are not relabeled as end-to-end latency or a playability SLA.

Native diagnostics are original standalone test code in `tests/native/SyntheticGpu*`, use real graphics APIs/encoders/MediaMTX, and never enter the normal Home catalog. The user owns browser/device and real ROM acceptance. No internet ROM/media/BIOS download or automated user-ROM execution was used.

## Verification / publication

**84 focused backend checks + 17 Node checks, 0 skipped**; frontend/worker/native-reader/PWA build passed. Native proofs include actual GPU pixels/audio, all four loopback controller ports, held/analog/pressure/pointer input, keyboard state/callback/shifted characters and releases, options, disk control, state restoration and WHEP H264/Opus negotiation. WHEP negotiation is not a fabricated browser/DTLS/media-reception proof.

The exact released mGBA DLL and final host passed held+simultaneous input, pause/state restore and H264/Opus/WHEP. The real Home manager additionally passed private-desktop process ownership, setup/active-runtime guard, authenticated/scoped WHEP, input epoch idempotence, actual core options and controller enumeration, checkpoint CAS, exit and relaunch with the **original GBA save partition**. This caught and fixed an invalid `Process.StartInfo` read on the native-created process before publication.

Proofs in the approved native diagnostic cache:

| Proof | Attempt / file |
|---|---|
| OpenGL | `native-gpu-isolated-tests/attempt-ffc01b41d4de42c3a14a61e298721f4d/probe.json` |
| OpenGL Core | `native-gpu-isolated-tests/attempt-514321504ade4f10b20af87f5c717d52/probe.json` |
| Direct3D 11 | `native-gpu-isolated-tests/attempt-6a186d1860d440dabb9fd30bd7f8e576/probe.json` |
| Vulkan | `native-gpu-isolated-tests/attempt-482a397fa8ef42b8a405d3b89eea9cbb/probe.json` |
| Released mGBA + final host | `native-host-gba-isolated-tests/attempt-e1be7361989143d19084d88ef78cbf0e/probe.json` |
| Home manager | `native-home-host-isolated-tests/attempt-df2d7a0e10de4e40a96e857982e66c27/home-native-smoke.json` |

The Home-manager input timing uses the original **full-screen-paint ARM fixture** (63–110ms polling readback); it is a functional regression check, not the palette-only latency fixture or a physical controller/display measurement.

Fresh pre-deployment evidence: `artifacts/native-preview-data-before-v037.json`, live **0.36.0/schema42**, **9 game-user states** and all three transfers paused. Signed publication and read-only preservation receipts follow.

### Live publication

- **0.37.0 / schema42**, process **20976**, signed Windows package **909 files / 116,319,133 bytes**; existing trusted signing-key fingerprint `b6ebb7e0a2b2821afd3143dc3bd9c8b666189d9a41953aa6657cf60d789b566b`.
- `artifacts/update-live-0.37.0.json`: owner login retained; rollback **0.36.0**, backup **`57ad43a57e024ea6b3c303ea08915e27`**.
- `artifacts/live-native-v037.json`: **3 ROMs, 9 game states, 8 save rows**, existing media/requests/preferences/metadata language and native enable all preserved; foreign-key violations **0**; all three transfers remain **paused**.
- `artifacts/live-core-catalog-v037.json`: **326 cores / 246 emulators / 194 available Windows adapters / 111 import systems**; runtime GPU readiness is now true; Citra and the retained mGBA are installed. Post-install read-only preservation was repeated successfully at **19:40 UTC**.
- To complete the user's existing 3DS setup, Home's software-install command installed the already-selected **Citra** core. `artifacts/prepared-native-core-citra-v037.json`: **30,267,484 bytes**, SHA-256 **`7592ce2b46ef24d2166a7f3b1e4eb04e861889edfdec9fea938b40299e0e2267`**, upstream-nightly, **GPLv2+**; archive SHA `28aa66943888c88ea8abd8f12b86f35b0935fff90b50028e95c7dcb009dbd879`. PE imports were inspected without executing it and use only Windows system libraries. The existing 3DS entry is now available to launch with no integration blocker. This is **one emulator-software install**, not a ROM/BIOS/media download or user-gameplay verification; existing mGBA was not replaced.
- Idle OS inspection after publication: Home API running, **no RetroArch or MediaMTX process**. No browser suite, screenshots, user ROM execution or game/media download was performed.

## Source references (checked 2026-10-20)

- [Released RetroArch source](https://github.com/libretro/RetroArch/tree/69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576) and [upstream input fix](https://github.com/libretro/RetroArch/commit/cfb1204314dd6b061a7b1cda77221735939bf5ba).
- [Libretro input/core API](https://docs.libretro.com/development/cores/developing-cores/), [Citra](https://docs.libretro.com/library/citra/), [Azahar](https://docs.libretro.com/library/azahar/), [Dolphin](https://docs.libretro.com/library/dolphin/), [LRPS2](https://docs.libretro.com/library/lrps2/), [PCEE2](https://docs.libretro.com/library/pcee2/), [PPSSPP](https://docs.libretro.com/library/ppsspp/), [Cemu](https://docs.libretro.com/library/cemu/).
- [FFmpeg frame timestamp / passthrough behavior](https://ffmpeg.org/ffmpeg-all.html), [Vulkan API headers](https://github.com/KhronosGroup/Vulkan-Headers/tree/acbb9391c0a597272800cd3f4363661c00a27c77).

Remaining product-wide release gates (native binary distribution/sandbox/fresh-host packaging, all individual game/core/device compatibility and physical microphone/camera/sensor accessories) are not silently marked complete by this integration. Core-provided emulated accessory modes can be selected through actual core options; unsupported physical devices are not fabricated.
