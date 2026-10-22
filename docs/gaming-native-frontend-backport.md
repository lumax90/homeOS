# RetroArch upstream giriş düzeltmesi — izole prototip

Kontrol: **2026-10-20**. GAM-04/05/10/11 / GAMING-RUNTIME-01. Home kaynak sürümü **0.34.0-dev.1**, çalışan preview **0.33.0**. Native kullanıcı oynatması hâlâ kapalı; bu belge release kabulü değildir.

## Sınırlı değişiklik

- Temel: kararlı RetroArch **1.22.2**, `69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576`.
- Yalnız `input/input_driver.c` içindeki Windows network-pad poll yolu: upstream **cfb1204314dd6b061a7b1cda77221735939bf5ba** receiver fonksiyonu değişmeden alınır; kararlı kaynakta eski poll bloğunun yerine çağrısı bağlanır. Commit'in farklı, sonraki frontend API'lerine ait first-press değişiklikleri taşınmaz. Bu bir full cherry-pick veya resmi kararlı binary değildir.
- Core mGBA **0.10.5** ve core hash değişmez. Nightly/master frontend benimsenmez; emülasyon/encoding/RTC/input motoru yazılmaz.
- `tools/build-native-frontend.ps1` her build'i ayrı `retroarch-backport-build-<id>` dizininde tutar; `home-frontend-build.json` source/fix/hash/dependency/config bilgisi ve **ContinuousInputVerified=false** kaydeder. Binary Home profilini kendisi kurmaz veya Oyna'yı açmaz.

`tools/stage-native-frontend-source.mjs` **4015** pinned RetroArch build-source/notice dosyası ve **712** FFmpeg header/notice dosyası aldı. Git blob ve SHA-256 doğrulanır. ROM/BIOS/media test fixture veya bütün kaynak arşivi indirilmez. Eksik `version.all` eklendiğinde ilk source manifest korunarak ayrı expanded manifest yazıldı.

## Mevcut codec/build araçlarını yeniden kullanma

Reviewed RetroArch DLL içindeki `av_version_info` gerçek sürüm **FFmpeg 4.2.3** verdi (`tools/Home.NativeAssets --inspect-ffmpeg`); avutil SHA-256 **38c364d8b2053d46a99df4dde84dc72abf3070d613e7ca0e1c032eaf026c6f7e**. Header'lar matching **n4.2.3 / d3b963cc41824a3c5b2758ac896fb23e20a87875** kaynağından. Yeni codec runtime indirilmedi/derlenmedi. `avconfig.h` Windows x64 little-endian public architecture header'ı olarak oluşturulur; emulator source değildir.

Upstream `Makefile.win` kullanılır. Physical DirectInput/XInput/WinMM input, host audio drivers, GL/D3D ve FFmpeg camera kapalıdır; core recording/threads/network command ve network pad açık. Upstream 1.22.2'nin menu-disabled build koşulları derlenemediği için minimal RGUI/menu **derlemeye** dahil edildi; null-video runtime kullanılır. Bu kullanıcının emulator UI'sini açma izni veya kabulü değildir; main-window=0 kontrolü actual executable testinde hâlâ gerekir.

Mevcut MinGit **2.56.0.2** ZIP'i geliştirme shell'i için yeniden kullanıldı: GitHub release digest **da35e72aa21c005a5a0d298cfbae110bc1609a815730ea0dde84b01a1b3cd3be**, **373** dosya. `tools/stage-native-build-shell.ps1` source archive'a dokunmadan ayrı reviewed cache'e kopyalar; yeni indirme/kurulum/PATH kalıcı değişikliği yok. Git/MinGit shell/runtime notice'ları archive'dadır; yalnız developer araçlarıdır. CMake/MinGW gibi ürün runtime dağıtımına eklenmez.

GCC16 eski FFmpeg API'lerinin `const AVCodec**`/`AVCodec**` nitelik farklarını GCC10'dan farklı olarak default error yapıyor. Yalnız `-Wno-error=incompatible-pointer-types` ile released build'in warning davranışı kullanılır; warning'ler **gizlenmez** ve `build.log` incelemesi zorunludur. Bu gerçek ABI uyumsuzluğunu onaylamak veya bütün uyarıları kapatmak değildir. Başarısız build girişimleri/source/log'lar korunur; codec/camera source patch edilmez.

## Kök neden ve gerçek Windows source regresyonu

`tests/native/RetroArchRemoteInput.c` + `tools/test-native-input-source.ps1`: yalnız testte küçük type/settings kabı; actual released `input_remote_parse_packet`, actual released `socket_receive_all_nonblocking` helper ve actual upstream fixed poll fonksiyonu değişmeden derlenir. Eski released poll bloğu aynı testte karşılaştırılır. Header/ABI/Winsock gerçek, sender/receiver **127.0.0.1 + ephemeral UDP**; RetroArch process'i/core/ROM/ürün kütüphanesi yok.

Sonuç: **20-byte Windows ABI**, eski no-data poll'de **held=false**, düzeltilmiş poll'de **held=true**; **32 ardışık idle poll** boyunca tek A ve A+B+Right korunur. Tek tuş bırakmak diğerlerini bırakmaz. Malformed short packet fixed receiver'da ignore edilir; valid release-all kontrolleri bırakır. Report:

`%LOCALAPPDATA%\Temp\opencode\native-gaming-runtime-v034-review\input-source-test-dbe4d4318c2447f3b250509e1bff0320\input-source-test.json`

Bu **source harness** kanıtıdır; `fullCoreVerified=false` açıkça kayıtlıdır. Home `InputReady` gate'ini kaldırmaz ve tam emülatör/H264/Opus/WHEP/gameplay kabulünün yerine geçmez.

Home protocol adapter'ı artık malformed `[0]` datagramına dayanmıyor: `ReleaseAll` yalnız **10 geçerli GBA release paketi** üretir. Core fixture'ı EWRAM'da gerçek key register, frame ve ardışık A-held sayacı tutar. `Home.NativeProbe --frontend-build <receipt>` tek press, **en az 10 kesintisiz native frame**, simultaneous A+B+Right, valid release, pause/save/restore/WHEP testlerini birlikte gerektirir. Repeat/flood ile held-input başarısı yaratılmaz; probe config'inde geçici joypad-index rewrite kaldırıldı.

## Açık güvenlik ve kabul kapıları

- Yeni EXE, eski exact-executable firewall kuralınca kapsanmış sayılmaz. **UDP 62401/62406** actual prototype listener'ları `NativeLocalInputGuard` geçmeden başlatılmaz. Source harness bunu bypass etmez; kendi socket'i explicit loopback-bound'dır.
- Yeni executable için gerekirse **ayrı kullanıcı onayıyla yalnız scoped inbound block** eklenir. İlk build/source-test aşamasında OS değişikliği yapılmadı; sonraki explicit onaylı firewall işlemi ve sonucu aşağıda kayıtlıdır. Driver/service/boot-trust değişikliği yok.
- Frontend build, warning review, port-disabled actual native recording, protected actual held/multi-button/core save/load/RTC ve Home manager recovery/session/save testi gerekir. Sonra kullanıcı kendi lawful ROM'u ile browser/audio/controller/device kabulünü yapar.
- İnternetten ROM/BIOS/media acquisition, automated browser suite, preview restart/update, commit/push yok. Son odaklı kontroller **78 backend / 33 Node / 0 skipped**, `native-upstream-input-adapters.trx`; NativeProbe .NET build temiz. Bu sayılar native gameplay release tamamlandı anlamına gelmez.

Kaynaklar: [released frontend](https://github.com/libretro/RetroArch/tree/v1.22.2), [upstream input fix](https://github.com/libretro/RetroArch/commit/cfb1204314dd6b061a7b1cda77221735939bf5ba), [FFmpeg n4.2.3](https://github.com/FFmpeg/FFmpeg/tree/n4.2.3), [MinGit release](https://github.com/git-for-windows/git/releases/tag/v2.56.0.windows.2). GNU Make Windows shell/override kullanımında güncel Context7 docs ve installed make/types/source kontrol edildi.

## Build ve port-disabled native kayıt sonucu

Frontend build tamamlandı: `...\retroarch-backport-build-141558f2e6c242caae7da1515f93d95a\home-frontend-build.json`.

- EXE **3,506,400 bytes**, SHA-256 **94218764273a3fa42c6785083b292b91bf2ab25c076cf687049812322d75e609**.
- Patched input source SHA-256 **c48d37aa535bd3eec3818cd8dcc29e25db028314605f6137b88bbee6809471e4**; tek değiştirilmiş emulator source dosyası `input/input_driver.c`.
- CLI kimliği **1.22.2 (Git 69a4f0e+home-cfb12043), MinGW 16.2.0**; patched prototype etiketi açık. Final `build.log` warning/error kontrolünde eşleşme yok. Önceki başarısız konfigürasyon/log'lar korunur.
- Aynı released mGBA **0.10.5 / e5b1c0d…** ile upstream FFmpeg recorder, UDP command/input kapalıyken original fixture'dan **101332-byte** TS üretti. `ffprobe`: **H264 240×160 yuv420p + Opus 48000 Hz stereo**.
- Native probe `attempt-fd2fd39d6a8e470295a8f5ea6ee76b4c/probe.json` yeni EXE için **firewall precondition**'da doğru şekilde durdu; native command/pad listener başlatılmadı. Bu attempt WHEP/actual-core held-input success olarak sunulmaz.
- Son etkilenmiş native backend kontrolü **42/0 skipped** (`native-input-backport-gate.trx`), geniş oyun/ağ regression önceki **78/0 skipped**; güncel Node **34/0 skipped**. Source/build/safety checks browser/ROM kabulünün yerine geçmez.

`tools/install-native-frontend-guard.ps1` yalnız **ayrı explicit approval** sonrası bu bir EXE için **Inbound Block / UDP 62401,62406 / Any profile / external IPv4 ranges** kuralı hazırlar. Önceki kural ve diğer ayarlar korunur, port açılmaz. Henüz bu script çalıştırılmadı; izin beklenir. Home preview/version ve `ContinuousInputVerified=false` değişmedi.

## Kullanıcı onaylı firewall ve tamamlanan native input kabulü

Kullanıcı soru aracında **“Evet, yalnız bu kuralı ekle”** seçti. Normal UAC ile yalnız **Home.NativePrototype.UpstreamInput.141558f2e6c242caae7da1515f93d95a** eklendi: exact EXE **94218764…**, Inbound Block / UDP **62401,62406**, Any profile, dış IPv4 aralıkları; loopback hariç. Önceki kural/diğer ayarlar korunur; port açma, driver/service kurulumu veya reboot yok. Receipt:

`%LOCALAPPDATA%\Temp\opencode\native-frontend-input-guard\retroarch-backport-build-141558f2e6c242caae7da1515f93d95a\result-c4268a4bf52c4b9582f298151c6972b3.json`

Readonly effective-policy guard geçtikten sonra **actual patched EXE + released core** kontrolü geçti:

- Tek press paketiyle A basılı kalır; **10 ardışık original fixture sample** (her biri VBlank bekler, en az 10 native frame) sırasında reset yok. **A+B+Right** aynı anda ve sonrasında basılı kalır; geçerli release diğer held kontrolleri bozmaz.
- **PLAYING → PAUSED → PLAYING**, native state **528472 bytes**; restore pixel **31**. MediaMTX gerçek source **H264 + Opus**, **WHEP 201**, main window handle **0**.
- İlk 250 ms kontrolü paint-loop sample sayısının 10'a ulaşmasını varsaydığı için `attempt-32e7…` başarısızdı; testin 10-sample/never-reset şartı azaltılmadı. Full Mode-3 paint birden çok native frame sürer: artık bounded süre boyunca counter/keys/monotonic held samples izlenir. Press-flood/tekrar paketi yok; observation JSON korunur.
- Başarılı actual report: `...\native-gaming-isolated-tests\attempt-cb3d31e7df834d13bef3dbf26c42782e\probe.json`. Önceki failed/source-only reports tarihsel kanıt olarak korunur.

**Home manager/transport/snapshot** gerçek binary ile isolated SQLite'da da geçti: ticket ve yabancı reader reddi, Home-scoped WHEP **201**/reader delete, idempotent launch/current input epoch **1**, CAS snapshot commit, end → relaunch/restore → discard, **0 active sessions**. Report:

`...\native-home-isolated-tests\attempt-24100bd19e474ec5997a38d4d72067b2\home-native-smoke.json`

Manager, bir input sample'ın changed controls'unu bounded/release-first batch olarak gönderir; değişmeyen held input için paket tekrarlamaz. Command/neutral ve input batch aynı gate ile serileştirilir. Unit test + actual manager smoke geçti. Son focused regression **79 backend / 35 Node / 0 skipped**, `artifacts/test-results/native-input-backport-final.trx`.

**Kapsam açık:** native held/multi-button teknik bloklayıcısı bu exact patched prototype için çözüldü. `ContinuousInputVerified=true` yalnız matching full-core proof isteyen **isolated Home probe** profilinde kullanıldı; normal Home state'e runtime profile kurulmadı. Çalışan preview **0.33.0**, normal ROM/game/media/saves ve paused downloads değiştirilmedi. Gerçek kullanıcı ROM'u, browser decode/audio/gamepad/fullscreen/device, fault/restart kabulü ve Home-managed enable/setup akışı hâlâ açık. Tam ürün/native gaming release tamamlandı sayılmaz.
