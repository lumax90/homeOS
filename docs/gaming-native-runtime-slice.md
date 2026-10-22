# Native GBA prototipi — 2026-10-19/20

Kaynak **0.34.0-dev.1 / şema 41** (package sürümü artırılmadı). GAM-04/05/10/11, **GAMING-RUNTIME-01**. İlk kapsam **Windows sunucusunun kendi tarayıcısı, sahip hesabı, tek GBA oturumu**. TV/uzak cihaz, PS2/Steam ve public distribution kabulü değildir.

## Seçilen ilk yol

**RetroArch 1.22.2 + kararlı mGBA 0.10.5 → RetroArch'ın kendi FFmpeg kaydedicisi → loopback MPEG-TS → MediaMTX 1.21.2 → WebRTC/WHEP → Home video alanı.**

Emülatör/encoder/RTC motoru yazılmadı. GBA görüntü/ses verisi doğrudan core callback'lerinden upstream kaydediciye gider; private Desktop capture ve emulator window yok. Bu **GBA** yolu sanal ekran kullanmaz; kullanıcı onaylı VDD gelecekteki standalone Windows emulator/capture yolu için kalır, kaldırılmadı. Sunshine/Moonlight Web sadece araştırma cache'inde, bu GBA runner'a bağlanmadı.

Kararlı RetroArch ZIP core seti mGBA **0.11-dev c758314** içeriyordu. Normal oyun runner'ında kullanılmadı. mGBA **0.10.5**, source commit **26b7884bc25a5933960f3cdcd98bac1ae14d42e2**, değiştirilmeden CMake/libretro target ile derlendi. Core SHA-256 **e5b1c0d03f7a7faf43b865e26fbf66a5ca5d33559ec6df62fc05bbac9700f4bb**. Binary runtime core-info çağrısı **mGBA / 0.10.5** doğruladı. Reproducible build ve public binary signing iddiası yok.

Upstream source `cinema/` ROM testleri içerdiğinden source archive indirilmedi: `tools/stage-mgba-source.mjs` yalnız **1122 build-source/notice** dosyasını pinned Git blob/SHA-256 ile aldı; `cinema/`, ROM ve BIOS binary'leri dışarıda. Build flags Qt/SDL/ek dependency'leri kapatır, `BUILD_LIBRETRO=ON`, `SKIP_LIBRARY=ON`; upstream emulator code patch edilmedi. GCC warning'leri gizlenmedi.

## Kimlikler / lisanslar

- RetroArch official stable 1.22.2, Git **69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576**, GPL-3.0. Archive SHA-256 **b2139b1d0f9d4526dc6b5ce23cbb3efdc766096fa6f2c3df016818b486ac6372**, EXE **81c11b6f24932bf7918f05eee8928035bff3887335fd2a081507c75e9d94d06a**. Archive hash official-HTTPS observation'dır; publisher signature/checksum manifest gibi sunulmaz.
- MediaMTX **1.21.2**, MIT; Git **914e39535ece709919ac325aaeed98d394ed9093**. Official release ZIP SHA-256 **a84224f5a490855684e62ba6fb71e80ea2a5e16aed61883bb58380290e93e467**. `reader.js` same commit, SHA-256 **a802f229b803c33713d4c69c4cc0d480108a5bf384947aeee4aaf04268bf85c1**; original MIT notice `web/public/native/MediaMTX-LICENSE.txt`.
- mGBA MPL-2.0 source/notice; libretro frontend GPL-3.0, native FFmpeg codec/build conditions ayrı. Home dağıtım lisansı/corresponding-source/public signing kapısı açık. Native binaries Home ZIP'e bu development slice'ta eklenmez; local reviewed cache profile ile kullanılır.
- CMake **3.31.8**, BSD-3-Clause; MinGW GCC **16.2.0 / mingw-w64 14**, toolchain GPL/runtime exceptions. Sadece dev build araçları; kullanıcının Home kurulumunda compiler gereksinimi olarak sunulmaz.

## Sınırlar / lifecycle

`NativeRuntimeProfile` exact file/hash/core/source version ve Windows firewall precondition kontrol eder; OS-elevated worker reddedilir. İlk API owner+loopback-only; bütün household/TV desteği değildir. Unprivileged native process + Windows kill-on-job-close/owned PID journal kullanılır; **filesystem AppContainer sandbox uygulanmış değildir**. İlk owner-only/local scope kaldırılmadan sandbox/native attack surface kabulü ayrıca gerekir.

Home mevcut authenticated/CSRF/user/login-bound game leases ve CAS checkpoint'i kullanır. Binary `game.state`/SRAM/RTC managed snapshot files'tır; SQLite küçük `native-save.json` manifest/pointer saklar, binary payload JSON'a gömülmez. Core/runner build version kayıt partition'ına bağlı. Native input sequence+epoch, pause/resume neutral gate ve ayrı Home gameplay ownership korunur. Startup native owned processes recover/close eder; user game otomatik başlatılmaz.

`NativeGameManager` tek host oturumu, explicit start, pause/resume, save/end/discard, timeout/revocation cleanup; `NativeGameEndpoints` owner/local commands ve bearer lease-bound WHEP proxy. MediaMTX management ve ingest yalnız **127.0.0.1**; RTSP/RTMP/HLS/SRT/MoQ/metrics/pprof/playback kapalı. Media okuyucusu ikiyle sınırlı; Location aynı Home route'una çevrilir. Browser native WebRTC reader orijinal upstream SDK'dır; ROM code client'ta çalışmaz.

## Yerel giriş güvenlik duvarı — kullanıcı onaylı

Kullanıcı soru aracında **Evet, kuralı ekle** seçti. `tools/install-native-input-guard.ps1 -Approved` normal UAC ile yalnız **Home.NativePrototype.LocalInput.v034** kuralını ekledi:

- Exact reviewed RetroArch.exe; **Inbound / Block / UDP / 62401,62406 / Any profiles**.
- Remote IPv4 ranges **0.0.0.0–126.255.255.255** ve **128.0.0.0–255.255.255.255**, loopback hariç. Upstream NetCMD ve Remote RetroPad **AF_INET** listener; daha geniş IPv6 input desteği iddiası yok.
- Diğer kurallar değiştirilmedi, port açılmadı, reboot/driver/trust bypass yok. Receipt `%LOCALAPPDATA%\Temp\opencode\native-input-guard-20261019\result.json`.
- Read-only guard MpsSvc/BFE, profile enable/default action/excluded interfaces ve exact executable/port/range block coverage denetler. Firewall değişirse native session durdurulur; güvenlik duvarı otomatik kapatılmaz.

## Gerçek izole native verification

`tests/native/SyntheticGba.cs` özgün, locally generated ARM fixture'dır: green/red video + test tone/key input, commercial ROM/BIOS/logo yok. **Sadece isolated automated test**; normal Home catalog'a eklenmez. Native integration test internet game/media acquisition veya browser suite başlatmaz.

`tools/Home.NativeProbe` actual binaries ile şunları doğruladı:

- Core **0.10.5**; actual H.264 **240×160/yuv420p**, Opus **48 kHz/stereo** (ffprobe).
- MediaMTX actual source **H264 + Opus**, WHEP **201** / SDP negotiation. Browser decode/fullscreen/audio/TV/latency acceptance **yapılmadı**.
- **PLAYING → PAUSED → PLAYING**, native state **528472 byte**, load restore.
- Remote A input original ARM programın VRAM pixel'ini **green 992 → red 31** yaptı; restore pixel **31** ile binary state geri yükleme doğrulandı. Native window handle **0**.
- Headless normal mode audio/vsync yokken unbounded çalışabiliyordu; upstream **FAST_FORWARD + ratio=1** pacing kullanıldı. Bu hızlandırılmış gameplay değil, fixed native frame limiter yoludur. Sürekli input/real game/device acceptance ayrıca yapılmalı.
- Minimal native core metadata eksikliği NetCMD GET_STATUS sırasında frontend access violation'a yol açtı; explicit `libretro_directory` + `libretro_info_path`, exact core/INFO staging eklendi. UInt joypad index **-1** de frontend crash'e neden oldu; valid index kullanılır. User-facing native pipeline bu unsafe values'ı üretmez.
- `quit_press_twice=false` olmadan single QUIT kapanmıyordu; normal controlled exit doğrulandı. Probe, timeout'ta yalnız owned processes'i kapatır.

Başarılı native report: `%LOCALAPPDATA%\Temp\opencode\native-gaming-isolated-tests\attempt-35ac3233b302416aa528fb688f15f8dd\probe.json`. Önceki failure reports korundu.

`tools/Home.NativeHomeProbe` isolated SQLite/real manager/fixture ile create → input → CAS binary snapshot commit → end → relaunch/restore → discard ve **0 active sessions** doğruladı. Report `...\native-home-isolated-tests\attempt-060cc90547254abc9ce966542feb074f\home-native-smoke.json`. Real owner/library/download data kullanılmadı.

Odaklı backend: **67 passed / 0 skipped**, `artifacts/test-results/native-runtime-v034-focused.trx`. Geniş `~Native` filter yanlışlıkla diğer native media tests'i kapsadı; o run 15 env-skipped test içerir ve final focused evidence olarak sunulmaz. Node/build/API/device final kayıtları yayın sonunda eklenecek. Çalışan preview henüz **0.33.0 / şema 40**.

Kaynaklar kontrol **2026-10-19/20**: RetroArch v1.22.2 `command.c`, `input/input_driver.c`, `record/drivers/record_ffmpeg.c`, `audio/audio_driver.c`; mGBA 0.10.5 source/README/CMakeLists/libretro; MediaMTX 1.21.2 config + WebRTC reader. Context7 current docs ve installed binary/types incelendi; `main` stress-doc `p0` örneği yerine pinned source'un gerçek **p1** key'i kullanıldı. Source/runtime/dependency files reviewed cache'te; user ROM edinmesi yalnız kullanıcı komutu.

## Son kabul kontrolü: sürekli giriş bloklayıcısı

2026-10-20 devam kontrolünde, ilk anlık press/save/restore başarısının **basılı tutma** doğrulaması olmadığı görüldü. Native test daha güçlü hâle getirildi: tek press datagramından sonra 250 ms beklenir ve original fixture pixel hâlâ pressed-red olmalıdır. Bu kontrol **başarısız**: state tekrar released-green'e dönüyor.

`attempt-c38889dfd64c44ad9d0509e819c0a347/probe.json` held-input failure'ını kaydeder. Test şartı gevşetilmedi, per-frame datagram flood veya native binary patch uygulanmadı. Windows Remote RetroPad no-data/latched-state davranışı kesin çözülmeden bu girdi yolu user gameplay için kabul edilmez.

Bu yüzden runtime profile **ContinuousInputVerified=false** varsayılanıyla fail-closed; `Verify` ve native card `Playable` kabulü bunu gerektirir. `configure-native-runtime.mjs` hiçbir profile bu flag'i eklemez. Preview **0.33/40** değiştirilmedi, normal runtime'a source profile kurulmadı; kullanıcı ROM'u indirilmiyor/çalıştırılmıyor. UI/manager source build'i tamamlanmış olsa da **native gaming release tamamlandı değildir**. Kalıcı held-input ve multi-button kabulü/uygun battle-tested input backend seçimi sonraki bloke adımdır.

## Upstream kök neden — 2026-10-20

Pinned [v1.22.2 input alıcısı](https://github.com/libretro/RetroArch/blob/v1.22.2/input/input_driver.c#L6456-L6473), boş nonblocking socket sonucunu POSIX `errno/EAGAIN` ile yorumluyor. Windows Winsock socket hatası `WSAGetLastError/WSAEWOULDBLOCK` üzerinden okunmalı; `errno` güncellenmediğinde sonraki poll, latched buttons'ı sıfırlayan kola girebiliyor. İzole actual Windows EXE'de görülen held-input failure bununla uyumlu.

Upstream düzeltmesi bulundu: [cfb1204314dd6b061a7b1cda77221735939bf5ba — Network RetroPad takes all that was sent each poll](https://github.com/libretro/RetroArch/commit/cfb1204314dd6b061a7b1cda77221735939bf5ba), **2026-10-04**. Commit açıklaması Windows `errno`/held-control bug'ını açıkça tanımlar; libretro-common nonblocking receive kullanır, queued changes'ı bounded olarak işler. Sonraki [65331ad1515f69b7ec5554fec401e7765ae128e7](https://github.com/libretro/RetroArch/commit/65331ad1515f69b7ec5554fec401e7765ae128e7) receiver'da `isagain` kullanır ve First Sender Only ekler. Bunlar **v1.22.2'nin parçası değil**: GitHub releases kontrolünde son kararlı sürüm hâlâ v1.22.2. Master/son commit'ler sadece incelendi; development native binary benimsenmedi/çalıştırılmadı.

Home gate güçlendirildi: failing reviewed EXE SHA-256 **81c11b6f24932bf7918f05eee8928035bff3887335fd2a081507c75e9d94d06a**, `ContinuousInputVerified=true` elle yazılsa bile `InputReady=false`. Native catalog doğru held-input blocker'ını gösterir; status ve launch aynı profile gate'i kullanır. Bozuk JSON/missing hash profile'ları kontrollü 409 ile korunur; silinmez. Bu, başka bir binary'nin doğrulandığı iddiası değildir.

**Son focused verification:** **78 backend / 30 Node, 0 skipped**, `artifacts/test-results/native-held-input-gate.trx`; native catalog/status/manager launch reddi, legacy flag default ve handwritten true flag reddi dahil. TypeScript, worker ve frontend/PWA build geçti. Bundler chunk-size/`use no memo` uyarıları var; browser/device kabulü yapılmadı. Home manager'ın önceki başarılı smoke'u tarihsel kanıttır; mevcut known-failing binary ile gate sonrasında yeniden başarılı çalışacağı iddia edilmez.

Sonraki tercih: kararlı RetroArch kaynak tabanına yalnız ilgili upstream input düzeltmesini backport edip **ayrı, açıkça patched prototype** olarak derleme/değerlendirme; veya kararlı fix release'ini bekleme/başka olgun input yolu değerlendirmesi. mGBA 0.10.5 değişmez. Full master/nightly silent upgrade, test gevşetme veya frame başına press-flood yapılmaz. Yeni EXE'yi eski kullanıcı onaylı firewall kuralının kapsadığı varsayılmaz; yeni executable için security guard tekrar doğrulanmadan listener başlatılmaz ve ek firewall değişikliği ayrıca onay ister.

Kullanıcının **“devam et”** yönlendirmesiyle sınırlı upstream backport source/build değerlendirmesine devam edildi; yeni firewall/driver yetkisi çıkarılmadı. [Backport kapsamı ve source-level Windows karşılaştırması](gaming-native-frontend-backport.md): eski receiver held-input failure'ı reproduced; actual upstream fonksiyon idle/multi-button/release testini passed. **Full-core held input hâlâ doğrulanmış sayılmaz**; fail-closed gate korunur.

**Sonraki 2026-10-20 sonuç:** ayrı explicit firewall onayı + normal UAC sonrası exact patched EXE'de full-core held/multi-button/pause/save/restore/H264-Opus/WHEP ve Home manager/proxy/CAS/relaunch kabulü geçti. [Final kanıt ve kalan sınırlar](gaming-native-frontend-backport.md#kullanıcı-onaylı-firewall-ve-tamamlanan-native-input-kabulü). **79 backend/35 Node/0 skipped**. Known-failing official 1.22.2 EXE reddi/default false gate korunur; normal profile/preview etkinleştirilmedi ve gerçek browser/ROM/device kabulü yapılmadı.
