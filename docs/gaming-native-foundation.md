# Native emülasyon — ilk güvenli hazırlık dilimi

**2026-10-19.** Kullanıcı **“başla”** ile Windows sunucu → Home tarayıcı prototipine başlamayı onayladı. Kaynak **0.34.0-dev.1 / şema 41**; çalışan önizleme **0.33.0 / şema 40**, **güncellenmedi**. Gereksinimler **GAM-04/05/10/11**, kart **GAMING-ROM-PREP-01**.

## Gerçekten uygulanan kısım

- Games varsayılan olarak **Kütüphanem** açılır. Web mağazası, mevcut iki oyun ve kayıtları silinmez.
- Home **ROM ekle** dialog'u: ilk kapsam tek Game Boy Advance `.gba`, **192 byte–32 MiB**; kullanıcı tarayıcısının dosya seçicisinden managed server copy. Original dosya değişmez; ZIP/EXE/BIOS, internet ROM sağlayıcısı veya otomatik ROM araması yok.
- Streaming upload, SHA-256 ve standard GBA header reset-vector byte/fixed byte/complement checksum gate. Bu **emulator/game uyumluluğu, özgünlük veya lisans kanıtı değildir**; unfixed/ELF/multiboot bu dilimin dışında. Logo/ROM verisi koda gömülmedi.
- Bir upload lane, 64-KiB buffer, 32-MiB body cap, iki dakika deadline ve profil başına 1000 entry sınırı. Eksik/oversize/cancelled upload metadata oluşturmaz; sadece kendi attempt/partial temizlenir. Kullanıcı library'sine yazma veya geniş cleanup yok.
- Verified staging → idempotent content-addressed publication; mevcut farklı target overwrite edilmez. DB hatası verified orphan bırakabilir; aynı hash retry güvenlidir.
- `041_native_roms.sql` + owner/system/hash uniqueness; ROM + kişisel library state transaction içinde. Retry identity/title/favorite/library-removal tercihini korur. Aynı ROM farklı profillerde ayrı private identity alır; başka profile admin olmak otomatik paylaşım değildir.
- Current login/actor headers/CSRF/edinme ve unrated policy; publication/registration öncesi yetki tekrar kontrol edilir. Read-only content açılırken hash tekrar kontrol edilir.
- Native card/details filename/size/hash kullanır. Generic gamepad placeholder icon; sahte kapak, publisher veya gameplay screenshot yok.
- Native `/play` **başlatılmaz**: UI **Yayın kurulumu bekleniyor**, API `playable=false`, `executionAvailable=false`, `streamingImplemented=false`. Existing web iframe/checkpoint/native ROM'a uygulanmaz.

## Henüz uygulanmayanlar

**Native emulator process'i, capture/encode/stream, virtual gamepad, binary save-state/memory-card ve native player bağlanmadı.** ROM preparation emülasyon değildir. Ayrı emulator/provider UI veya default personal Desktop stream ile başarı taklit edilmedi.

Sunshine stable-tag `docs/configuration.md` Windows DXGI ve beta WGC'nin **display capture** olduğunu söylüyor; WGC ayrıca service ile uyumsuz. Bunlar window-only izolasyon diye varsayılmadı. Kullanıcının Home-only şartı için güvenli oyun display'i ve input sınırı önce kurulmalı.

## Bileşen kimlikleri — aday envanteri, installed runtime değil

`NativeGamingRuntime` reviewed candidate metadata ve varsa archive bütünlüğünü gösterir. Verified ZIP bile configured/working runtime anlamına gelmez; execution false kalır.

| Aday | Sabit kaynak | İnceleme |
|---|---|---|
| RetroArch 1.22.2 | `69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576`, GPL-3.0 | Windows runtime/core binary henüz sabitlenmedi/kurulmadı; mGBA aday. Libretro fork adresinin 404 olması görmezden gelinmedi |
| Sunshine 2026.914.233613 | `63d35f702ee9e362e43263742981836ec0710384`, GPL-3.0 | AMD64 lite ZIP SHA-256 `233008e46f4c0e501a586cbfd6c4fd4a4c0d414a0b5fc7f13c070eb92ec3824b`; metadata. Lite upstream'de genel kullanım için önerilmez |
| Moonlight Web 2.10.0 | `cd9d03cbf9a42b394f7b72a733a2f39cb5f0edd8`, GPL-3.0 | Windows ZIP SHA-256 `1dc3019952c610fbd7deb76dc84e3c4c6f26458ebb44823ea1f02ad883a36da9`; stable source streamer IPC/browser bridge incelendi; entegre edildi denmez |

Kendi libretro frontend/encoder/Moonlight protocol motoru yazılmadı; upstream code fork/kopyası alınmadı. Public Home/TV-client lisansı ve corresponding-source/notice dağıtım yükümlülükleri açık.

## Windows / driver artifact incelemesi — kurulum değil

- `tools/inspect-native-gaming.ps1` read-only monitor/process/driver envanteri. İlk `artifacts/native-gaming-host-20261019.json`: bir primary **1920×1080** display; ilgili process/system-driver eşleşmesi yok, detector exhaustive değil. Ayrı PnP sorgusunda Microsoft inbox **VHF HID device** var; bu LizardByte licensed gamepad veya VDD kuruldu kanıtı değildir.
- **VirtualDrivers/Virtual-Display-Driver** repo MIT; bundle **25.7.23** (2025-07-23), `VirtualDisplayDriver-x86.Driver.Only.zip` **132.118 byte**, SHA-256 **e24210692b442b39af763536330ce78b423f19342b7a7792c26de3944e418b3a**.
- Sadece software artifact approved temp'e indirildi/açıldı; **ROM/media download değil**. `tools/stage-native-display-review.ps1` device/service/certificate kurmaz, test-signing/Secure Boot/ekran önceliği değiştirmez.
- Gerçek INF **NTamd64**, DriverVer **12/24/2024,11.30.4.434**; bundle adı/version ile driver binary architecture/version ayrıştırıldı.
- DLL SHA-256 **c9ca837f57a98fbd43bc416a7f535a95843626e7759eaf85cf0cd7ce334dbb05**; CAT SHA-256 **08a0093fc9b2e32b287a6f8a77ca4de0a31830d29fc33d2b13a918dc859468f6**. Authenticode: **Valid / SignPath Foundation**. INF/XML sonucu signed DLL sonucu gibi sunulmadı. Bu kontrol WHQL veya bu makinede driver load/device kabulü değildir.
- Kanıt: `%LOCALAPPDATA%\Temp\opencode\native-display-review-25.7.23\review-summary.json`. İlk PS5 raw-content report ETS metadata'yı fazla genişletti; ayrı plain-string summary oluşturuldu, eski report silinmedi. Stager source'u extracted-file/pinned-archive eşleşmesi kontrolüyle de sertleştirildi.
- Upstream GPU/chipset driver update sırasında black-screen/display-priority çatışması uyarıyor. **Driver kurulumu ayrı kullanıcı onayı gerektirir**; signature enforcement/test-signing/Secure Boot bypass yapılmaz.

Gamepad ayrı kapı: stable Sunshine EOL ViGEmBus veya ücretli machine-license gerektiren LizardByte Virtual HID Driver belgeler. `libvirtualhid` library MIT; driver/broker artifacts **LB-SAL 1.0 source-available**. Yıllık/lifetime seçenekleri var; price estimate/purchase veya kurulum yapılmadı. Yeni alternatifler doğrulanmış Sunshine backend'i gibi seçilmedi.

## Giriş noktaları

- `Gaming/NativeRomLibrary.cs`, `NativeGameModels.cs`, `NativeGamingRuntime.cs`; `Infrastructure/MediaStore.NativeGames.cs` ve migration `041_native_roms.sql`, `LatestSchema=41`.
- `POST /api/games/roms`: raw `application/octet-stream`, encoded `X-Home-Rom-Name`, existing actor/login/CSRF headers. Hak teyidi UI'de; copyright/provider integration inference yok.
- `GET /api/games/native/runtime`: authenticated candidate status; internal paths/secrets/execution URL yok.
- Existing `/api/games` / addressable details içinde private native entry. Arbitrary filesystem-path upload veya raw ROM download endpoint'i yok.
- `web/src/gaming/NativeGames.tsx` ve Games/GamePlayer integration: aynı Home route/dialog, native entry web session'a düşmez.

## Doğrulama ve canlı durum

**58 backend / 89 Node, 0 skipped**; .NET/TypeScript/frontend/PWA/worker build başarılı. Üç driver-script güvenlik kontrolü son focused Node setine eklendi. Existing chunk-size/third-party directive warnings bastırılmadı.

Kanıtlar: `artifacts/test-results/native-gaming-foundation.trx`, `artifacts/native-gaming-foundation.tap`, `artifacts/native-gaming-foundation-build.log`.

Backend: actual fixture stream/API actor/CSRF, standard header/renamed EXE, truncation/size/unknown Content-Length, single lane, cancellation/revocation, conflict/idempotence, profile ownership, integrity re-read, 40→41 migration ve önceki **persisted JSON checkpoint/favorite** korunumu. Node: native/web ayrımı/non-playable route, explicit submit/abort, honest artwork, mevcut gaming/controller/navigation/i18n kontrolleri.

**Gerçek kullanıcı ROM'u okunmadı/aktarılmadı/çalıştırılmadı.** Synthetic header fixture yalnız isolated testte. Browser/device suite, screenshot polish, emulator/GPU/capture/stream/gamepad testi yok. Driver/emulator/service/container kurulmadı; paused transferler/live credentials/preview 0.33/40 değişmedi. Signed package/public yayın, commit veya push yok.

## Sonraki kapılar

1. Sanal ekran: bu geliştirme makinesinde kullanıcı onaylı kurulum/PnP/display kontrolü tamamlandı (aşağıda). Home dağıtım/otomatik setup/update/rollback ve capture isolation kabulü açık; OS trust bypass yok.
2. Verified RetroArch + izinli GBA core binary/source; kullanıcının kendi yetkili ROM'u. Developer verification internet ROM/BIOS acquisition başlatmaz.
3. Yalnız ayrılmış oyun display/input'una bağlı supervised Home launch + hazır Sunshine/browser bridge; private Desktop ve ayrı emulator UI yok.
4. Home-only pause/save/exit/reconnect/restart ve **binary native save** adapter'ı; küçük web JSON checkpoint bunun yerine geçmez.
5. Computer-browser/TV/gamepad/input-to-photon kabulü kullanıcıda. Server hardware kullanıcı seçimi; desteklenmeyen önkoşullar gizlenmez.

Kaynaklar **2026-10-19**: [yeniden kullanım](gaming-reuse-assessment.md), [TV/yürütme](gaming-execution-research.md); https://github.com/VirtualDrivers/Virtual-Display-Driver ve `/releases/tag/25.7.23`; https://github.com/LizardByte/libvirtualhid ve `LICENSES/license-map.md`; stable Sunshine `docs/configuration.md`, Moonlight Web `streamer/src/main.rs`, RetroArch `v1.22.2`; mGBA 0.10.5 `src/gba/gba.c`/GBATEK cartridge header. ASP.NET raw-body size/streaming, HeroUI v3 modal/button ve kurulu TypeScript/React/HeroUI tipleri incelendi.

## Sanal ekran kurulumu onayı / girişimi

Kullanıcı soru aracında **“Kurulumu onaylıyorum”** seçti. Yetki yalnız incelenen ücretsiz VDD kurulumu; reboot, primary display değişikliği veya ücretli gamepad driver kapsam dışı. Agent process'i elevated admin değil; `tools/install-native-display.ps1 -Approved` normal **RunAs/UAC** ile OS onayı talep eder. UAC tamamlanmadan driver kuruldu denmez.

Upstream wiki manual route root-enumerated device gerektiriyor; sadece INF staging yeterli değil. Eski community installer scripts unpinned third-party installer/latest asset matching kullandığından çalıştırılmadı. **Microsoft signed DevCon**, official WDK CAB içinden exact entry çıkarılarak doğrulandı; third-party installer executable çalıştırılmadı. Microsoft driver docs Context7 ile root-device/PnPUtil ayrımı ve administrator requirement incelendi.

- Official CAB: `https://download.microsoft.com/download/7/b/f/7bfc8dbe-00cb-47de-b856-70e696ef4f46/wdk/Installers/787bee96dbd26371076b37b13c405890.cab`
- CAB SHA-256 **6ecfaf7042c525ddb6c0bf0501becfbce85cbdf2a33b1bca67734fea567af3b8**; exact member `filbad6e2cce5ebc45a401e19c613d0a28f` binary SHA-256 **a92d4bc174079df703512e778e3192f524e4d0e33fc0ef718acc43c29e3d6e30**. İki Authenticode **Valid / Microsoft Corporation**. İlk tool metadata hash'i CAB'a aitken binary ile karşılaştırma fail-closed durdu; CAB identity ve byte-extracted binary fingerprint ayrıştırıldı, denetim kapatılmadı.
- Bu Microsoft development helper Home'a paketlenmedi/yeniden dağıtılmadı; WDK/Microsoft araç kullanım koşulları ayrı. Binary temp review alanında, native gameplay veya media acquisition değil.
- Installer kendi verified dört driver file'ını, bir root device'i ve receipt'lerini hedefler. Var olan VDD device/C:\VirtualDisplayDriver veya eski receipt üzerine yazmaz. Trust/certificate/boot bypass veya restart yok; process exit + PnP result ile kurulum ve reboot ihtiyacı ayrı kaydedilir. Primary display **change requested=false**, son gerçek durum ayrıca incelenmeli.

**İlk girişim:** `tools/install-native-display.ps1 -Approved` normal-UAC helper'ına iletildi; **Windows elevation kullanıcı iptali yanıtıyla kapandı**, parent exit **1**. Elevated installer çalışmadı; o aşamada VDD kurulmadı veya otomatik yeniden denenmedi. Önceden verilmiş session onayı, iptal edilmiş OS elevation'ı aşma yetkisi sayılmadı.

İlk girişim sonrası rapor **`artifacts/native-gaming-after-uac-cancel-20261019.json`**: aynı tek **\\.\DISPLAY1 / primary / 1920×1080**, matching driver/process/PnP listeleri boş. Detector exhaustive değildir; capture/input capability çıkarılmaz. O aşamada reboot/certificate/boot veya display-priority değişikliği yapılmadı.

### Kullanıcı “yeniden aç” — başarılı ikinci girişim

Kullanıcı açıkça yeniden açmayı istedi. Aynı pinned driver/DevCon girdileri yeniden SHA-256 ve Authenticode kontrolünden geçti; normal Windows UAC üzerinden elevated helper çalıştı. **2026-10-19T11:10:37Z**, DevCon exit **0**, `Drivers installed successfully.`

- Device **Virtual Display Driver**, **ROOT\DISPLAY\0000**, PnP **OK**; provider **MikeTheTech**, driver **11.30.4.434**, installed INF **oem5.inf**.
- Post-install read-only rapor **`artifacts/native-gaming-after-vdd-install-20261019.json`**: original **\\.\DISPLAY1 / primary / 1920×1080** korundu; yeni **\\.\DISPLAY5 / non-primary / 800×600** eklendi. Resolution/primary değiştirme komutu verilmedi; yeni display boyutu paketin ilk varsayılan modudur, oyun-yayın hedefi kabulü değil.
- `rebootRequired=false`, `rebootPerformed=false`; gamepad driver/ücretli bileşen veya certificate/boot/test-signing değişikliği yok. Bu sonuç sadece display driver/device hazırlığını doğrular; **captureIsolationVerified/nativeExecutionVerified/streamingVerified=false**.
- Driver config/input files **C:\VirtualDisplayDriver** altında dört verified dosya; before/receipt/stdout **%LOCALAPPDATA%\Temp\opencode\native-display-install-20261019** altında. `before.json`, `result.json`, `devcon.stdout.log` korundu. Eski/başka driver veya config overwrite edilmedi.
- Uninstall/rollback yapılmadı. İleride istenirse exact device/INF kimlikleri hedeflenir; geniş display/driver reset işlemi yetkilendirilmiş sayılmaz.

Home source/version/schema ve test sonuçları aynı: 0.34 source/41, 58 backend/89 Node/build. Preview **0.33/40** güncellenmedi; ROM/BIOS/emulator/streaming/gamepad kurulumu veya oyun çalıştırılması yapılmadı. Native runner/browser bridge, binary saves ve gerçek cihaz oyun kabulü açık.
