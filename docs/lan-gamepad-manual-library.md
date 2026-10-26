# LAN gamepad and manually copied films — 2026-10-25

Requirements **CLI-01/05/01-K, GAM-05, CAT-01/03/05**. User report: a PC browser activates its gamepad on localhost but not on the CasaOS-hosted Home page. Two named film folders manually copied beside a downloaded title in the managed library did not appear. **Local source follow-up; not published.** Existing radio banner removal remains in the same uncommitted work.

**User correction, 2026-10-25:** The films are already visible in Library; the missing visibility is on the page the user calls the main/home page. The scan findings below explain the original hypothesis, not an established failure in the household scanner. [Landing-page and playback follow-up](playback-hls-followup.md).

## Gamepad capability

- `ControllerRuntime` previously declared every non-secure page `insecure` before checking whether this particular browser exposes `navigator.getGamepads`. Its poll loop then refused to call the API. That was an application-imposed restriction, separate from the browser's genuine security policy.
- The constructor now tests the actual API surface. If an HTTP browser exposes it, Home can poll it and switch to controller navigation after a fresh button press. A missing API on an untrusted origin is still reported as insecure; a missing API on a trusted origin is unsupported. A refused call stays fail-closed with no continued timer loop.
- Focus/gamepad-connected events can re-probe capability. Gameplay ownership, neutral-before-activation, remote/keyboard switching and setting disablement remain unchanged. Home does not override browser flags, fake user activation, or bypass Permissions Policy.
- Native gameplay already probes `getGamepads` independently. Removing the shell gate is **not** proof that a browser which hides or refuses this API on LAN HTTP will now supply native-game input.
- Localhost is a potentially trustworthy origin even over HTTP. An ordinary LAN IP over HTTP is not; reliable operation where the browser restricts Gamepad API requires **trusted HTTPS**. The actual user's browser/API/policy result is not available yet; this report does not establish that a TLS change or another library fixes their entire device path.

## Manually copied films

- Screenshot shows two named directories beside a download-ID directory under the managed `downloads/library` root. Import markers and hexadecimal directory names are **not** requirements of `LibraryScanner`; traversal recursively inspects supported media extensions.
- The managed download library is created with `LibraryOptions`: filesystem watching **off**, periodic scans **off**. A completed Home import explicitly queues a scan; a file-manager copy does not do so unless watching/periodic scanning was enabled.
- Immediate user check: open the existing **İndirilenler / Downloads** library editor and choose **Şimdi tara / Scan now**. For future copies, explicitly enable **Klasör değişikliklerini izle / Watch folder changes**, save, then scan. Existing files do not need to be moved, renamed to a download ID, or supplied with forged import manifests.
- In the container, the existing root is `/data/downloads/library`; the host's `/DATA/...` path is not the container's path. Do not register all of `/data` as an external source library or duplicate the existing managed root.
- If a manual scan still does not find the films, inspect the job result, actual file extensions and read/traverse permissions for Home's UID/GID **1654**. The screenshot alone does not establish any of these. No blanket recursive ownership change or source-file rewrite was made.
- No scanner defaults or persisted user options were changed. A new regression test places two named folders without import markers beside an existing managed item, then rescans twice. All three items appear with unchanged original bytes, existing media identity/progress and no duplicate record.

## Verification

- **60 Node tests**, including new real-API/HTTP refusal/re-probe cases and existing menu/gameplay ownership, input and localization tests, passed.
- **15 backend library/lifecycle tests** passed, including the new managed-root/manual-folder regression, unavailable-volume preservation and repeatable scanning.
- Frontend/worker/PWA build passed. No real browser/device automation, household filesystem changes, downloads, commit or push were performed. These checks are not user device acceptance.

Sources reviewed **2026-10-25**: [MDN getGamepads](https://developer.mozilla.org/en-US/docs/Web/API/Navigator/getGamepads), [MDN secure contexts / localhost trust](https://developer.mozilla.org/en-US/docs/Web/Security/Defenses/Secure_Contexts). Context7's quota was exhausted; official documentation and installed source contracts were used.

Implementation references: `web/src/controller/runtime.ts`, `tests/unit/controller-runtime.test.mjs`, `server/HomeServer.Api/Scanning/LibraryScanner.cs`, `LibraryTraversal.cs`, `LibraryMonitor.cs`, `Infrastructure/MediaStore.Downloads.cs`, and `tests/HomeServer.Tests/LibraryTests.cs`.
