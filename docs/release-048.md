# Home 0.48.0 — handheld games and clear support information

**2026-10-27 / authorized release preparation, not yet published.** Schema **44** remains unchanged. User authorized staging, commit, push and publication of the accumulated PSP/DS/3DS/game-removal/support-dropdown and scene-still/trailer changes once qualification passes. Public `stable` remains 0.47 until the workflow succeeds and anonymous registry identity is verified.

## Included slices

- **GAM-20/21/22 / PSP-01, GAME-REMOVE-01:** separately pinned PPSSPP software/native-1x package, strong PSP ISO identity and confirmed correction of earlier wrong PS1 imports. Confirmed uninstall removes only managed last-reference game payloads; saves, progress, artwork, manual details and source-library files remain.
- **GAM-23/24 / HANDHELD-01, SUPPORTED-SYSTEMS-01:** source-built DeSmuME/Citra Linux packages, native dual-screen/stylus/controller/save paths and proof-bound Standard/optional Selkies. New supported-devices popover beside Games settings separates supported, experimental and not-ready systems. **3DS remains experimental and hardware/game-dependent**, not a performance guarantee.
- **DETAIL-MEDIA-02:** actual local scene-frame gallery, three initial/six expanded timestamped samples, photo modal, and trailer action beside the main play/request actions opening a click-loaded privacy-enhanced modal. No alternate posters or scraped IMDb photos are substituted for scene frames.

## Preservation and limits

Existing seven-system frontend/core/save/settings identities are unchanged and checked by the immutable runtime lock. Existing user-installed Citra versions are retained rather than silently overridden. New handheld states remain version/hash-bound; the Citra out-of-source SCM lookup is fixed and its build date is deterministic. Heavy 3DS state commands use a bounded longer command budget, not a slower gameplay-input path. No BIOS/Mii/ROM/media acquisition is initiated during development verification.

Media/library bindings, manual metadata, saves, settings, navigation and paused downloads are not reset. There is no schema bump or automatic database rollback. CasaOS installation/update remains the existing same-data/mount-preserving, owner-confirmed update flow; publishing an image does not prove a household update.

## Qualification/publication status

Local component checks have passed: PSP and DS/3DS real native frame/input/stylus/state checks, optional Selkies presentation checks, exact corresponding-source/notices closure and supported-systems API/UI contracts. **53 focused backend/native checks passed, zero failed/skipped**, including actual HLG/P8.1 still JPEGs, cached-image login denial, cancelled/revoked waiters and owned-cache pruning. Counts overlap earlier qualification and are not summed. Final Linux/pinned-runtime release-image workflow checks remain pending. No automated browser suite or repeated seven-system latency comparison is run.

**46 Node/localization contracts**, **5 Python source-contract checks**, 0.48 frontend/worker/PWA build and the immutable native runtime/source/save-settings lock passed. Raw household details and local evidence remain ignored, not release contents.

User PC/TV/real-game acceptance, previous unresolved radio/game/3DS device issues, full DV/HWA/media parity, alternate architectures/distribution and the overall product license remain separate open gates. Do not call the entire product complete.

- Source commit/workflow/digest: **pending successful publication**.
- Raw operational receipts: ignored `artifacts/release-048/`, `artifacts/psp-01/`, `artifacts/handheld-01/`.

References/status: [PSP/removal](psp-and-game-removal.md), [DS/3DS/support](ds-3ds-supported-systems.md), [scene/trailer](scene-stills-trailer-modal.md), [update policy](managed-updates.md), [product board](product/execution.md).
