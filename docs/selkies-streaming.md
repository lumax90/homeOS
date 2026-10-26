# Deneysel ikinci oyun yayını — SELKIES-01

Tarih: **2026-10-24**. Gereksinimler **GAM-05/10/11/14/19, CLI-01/05, OPS-09**. **0.45.0 / stable ile deneysel seçenek olarak yayımlandı; gerçek cihaz kabulü bekliyor.** [Yayın kaydı](playback-streaming-batch-045.md). Kullanıcı ikinci yöntemi onayladı; iyi cihaz sonuçları alınırsa Selkies'i daha sonra varsayılan yapma kararı ayrıca verilecek. Kullanıcı PC tarayıcısında gecikmenin TV'den daha az olduğunu bildirdi.

**2026-10-25 yönlendirmesi:** Kullanıcı Selkies'in zaten kurulduğunu hatırlattı ve gerçek denemeleri kendisi yapıp sorunları bildireceğini belirtti. Kurulum/entegrasyon eksik iş olarak yeniden açılmıyor; tekrar otomatik oturum/latency benchmark turları yapılmıyor. Cihaz kabulü kullanıcıya ait, geliştirme onun raporunu bekleyen bir kuyruk değil. Standart/Selkies varsayılanı bu mesajla değiştirilmedi.

**2026-10-26 cihaz raporu:** Kullanıcı Selkies'i seçince viewer açılışı signaling **404** ile kesildi. [SELKIES-SIGNAL-02](selkies-signalling-followup.md) yayımlı tarayıcının trailing-slash adresini Home'un allowlist'inin reddettiğini belirledi; iki tam adres biçimi ve normal HTTP probe yerelde düzeltildi/derlendi. **Henüz yayımlanmadı**; eski oturum kanıtları bu yeni cihaz hata raporunun yerine geçmez.

## Kullanıcıya sunulan seçim

- **Oyunlar → Ayarlar → Emülasyon ayarları → Gelişmiş yayın tercihi → Yayın altyapısı**.
- `Standart · varsayılan` mevcut yolu kullanır. `Selkies · deneysel` Linux x64 paketinde ikinci seçenektir.
- Tercih bu tarayıcı, Home alt yolu ve profil kapsamında saklanır. Sonraki oyun açılışında uygulanır. Çalışan oyunun yayın yolu kendiliğinden değiştirilmez.
- İlk Selkies kapsamı mevcut **GB/GBC/GBA, NES, SNES, Mega Drive ve PS1** çekirdekleridir. Başka core/binary, eşleşen doğrulama kapsamına alınmadan bu yola geçirilmez. Citra/3DS ve Windows native yolu standart altyapıda kullanılmaya devam eder.
- Her iki yöntemde kumanda, dokunmatik, klavye/fare, pause, core ayarları, disk ve kaydet/çık işlemlerinin sahibi Home'dur. Selkies ilk dilimde **görüntü/ses ve hazır browser player** sağlar; ikinci bir gamepad okuyucusu başlatılmaz.

## Kaynak tüketimi ve yaşam döngüsü

| Durum | Aktif oyun/yayın süreçleri |
|---|---|
| Oyun kapalı | RetroArch, MediaMTX, Selkies, PulseAudio ve oyun Xvfb'si: **0** |
| Standart software-core oturumu | Tek RetroArch + tek MediaMTX; Selkies/PulseAudio: **0** |
| Selkies oturumu | Tek RetroArch + tek Selkies + o oturumun PulseAudio/Xvfb'si; MediaMTX: **0** |

Birlikte kurulmaları iki kez kodlama veya boşta Selkies servisi anlamına gelmez. Selkies yalnız seçili oyun açılışında başlar, çıkışta süreçleri beklenerek kapatılır. Oyun açıkken seçilen yolun kendi CPU/RAM maliyeti vardır; iki yolun aktif performansı aynı kabul edilmez.

Ölçülen kurulu dizinler: `/app/SelkiesRuntime` **18,440 KiB**, `/opt/selkies` **264,944 KiB**; birlikte yaklaşık **277 MiB**. Bu, ilgili iki dizinin disk boyutudur; ayrıca sistem paketleri bulunur. Kaynak dosyaları/disk alanı ile boşta çalışan süreçlerin RAM/CPU tüketimi ayrı metriklerdir.

## Entegrasyon

- [Resmî güncel doküman](https://docs.selkies.io/latest/) **2.1.0** üzerinden incelendi; `deploy/selkies/runtime.json` aynı release'in Ubuntu 24.04 amd64 paketini tam boyut/SHA-256 ve source commit ile sabitler. İndirme build aşamasındaki yazılım edinimidir.
- Native runtime kilidi ve mevcut beş core binary'si aynen kullanılır. Selkies'in ekran/ses kaynağı için aynı RetroArch 1.22.2 + mevcut Home adapter kaynağından **ayrı SDL2/PulseAudio çıkışlı frontend** derlenir. Standart frontend yeniden üretilmez.
- Alternatif frontend yalnız eşleşen canonical frontend/core hash'leriyle açılır. Save/settings kimliği standart paketin kimliğidir; iki yayın yöntemi arasında ayrı boş kayıt bölümü oluşturulmaz.
- Standart yol doğrudan recorder/FFmpeg/MPEG-TS/MediaMTX kullanır. Selkies yolu RetroArch'ın hazır **SDL2 software renderer** çıktısını private Xvfb + PulseAudio üzerinden **Selkies/pixelflux/pcmflux ile bir kez** kodlar; `--record` başlatılmaz ve MediaMTX süreci açılmaz. İlk dilim CPU H.264/8-bit/4:2:0'dır; donanım kodlama desteği iddia edilmez.
- Bu software-core grubunun ekranı SDL2'nin mevcut `software` çizicisidir; `SDL_VIDEODRIVER=x11`, `SDL_RENDER_DRIVER=software` ve `SDL_FRAMEBUFFER_ACCELERATION=0` yalnız seçili oturumun ortamıdır. Son bayrak, SDL'nin ekran yüzeyi için ayrıca 3D hızlandırma açmasını da engeller. Core/emülasyon motoru veya yeni renderer yazılmadı.
- Capture 640×480 ile sınırlıdır; 4K TV viewport'u sunucuda 4K encoder açmaz. Frame-rate core'un raporundan alınır. Upstream congestion control açıktır. Sanal X ekranının screensaver/DPMS blanking'i kapalıdır.
- Hazır Selkies web core Home'un aynı origin'inde, oturum kapsamlı iframe içinde çalışır. Home adapter'ı receiver istatistiklerini mevcut **Yayın ölçümü** paneline bağlar; kendi SDP/RTP/decoder motoru oluşturmaz. Kaynak web core değiştirilmez.
- Home'un kumanda sahipliği çift girdi üretmemek için korunur. Selkies viewer token'ı controller/desktop input yetkisi taşımaz. Dosya/clipboard/command/mikrofon/webcam ve paylaşım yüzeyleri oyun akışına açılmaz; proxy yalnız viewer assets, gerekli okunur API'ler ve signaling'i taşır.
- Selkies HTTP/control portu loopback'tir. WebRTC mevcut **62405 UDP/TCP** yayın portunu kullanır; ikinci CasaOS servisi veya yeni LAN port aralığı gerekmez. STUN/TURN listesi açıkça boştur; LAN adresi Home ağ ayarından/talep host'undan türetilir.
- Başlatma, viewer beklemesi, signaling mesajları ve reader sayısı sınırlıdır. Yanlış login, oturum sonu ve iptal mevcut Home lease'iyle uygulanır. `engine/media/selkies/sound/display` süreç günlükleri başlangıçta toparlanır. Updater'ın mevcut native-busy kontrolü bu oturumu da kapsar.
- Home kendi kalıcı cihaz/profil tercihlerini saklar. Oturum URL'sine bağlı upstream viewer cache'leri iframe kapsamında geçicidir; çıkışta peer bağlantıları ve o oturumun Selkies cookie'si bırakılır.

## Geçiş testinde bulunan pause hatası

Gerçek GBA frame-counter tanısı, eski `LOAD_STATE` hotkey yolunun Home duraklatılmış beklerken core'u tekrar ilerletebildiğini gösterdi. Aynı oturumda released RetroArch'ın **`LOAD_STATE_SLOT 0`** action'ı ile state yüklendiğinde pause ve frame counter sabit kaldı.

`RetroArchControl.LoadStateAsync` artık bu upstream action'ın cevabını doğrular; managed state slot açıkça **0**, auto-index kapalıdır. Save dosyası yine `game.state`'tir. GBA'da **standart → Selkies → standart** geçişinde gerçek RAM frame-counter'ın kaldığı değere dönmesi doğrulandı. Bunun için mevcut native binary/core/save kimlikleri değiştirilmedi.

Bu action cevabı yükleme isteğinin kabulüdür; genel bir yeni upstream task-completion API'si olduğu iddia edilmez. Gerçek state/load davranışı native protokol kontrollerinde ayrıca gözlenir.

### Native başlatma sırasında bulunan iki ayrı sorun

- Hazır olmayan loopback UDP portu hemen ICMP refusal döndürdüğünde on denemelik sınır yaklaşık bir saniyede dolabiliyordu. `WaitReadyAsync` artık gerçek **30 saniyelik süre/iptal bütçesi** kullanır; socket refusal'ın send/availability aşamasında gelmesi de aynı geçici hata yoluna girer. Geç bağlanan gerçek UDP listener ile regresyon kontrolü vardır.
- Native GDB stack'i, SDL2'nin kendi X bağlantısını kullandığı durumda RetroArch 1.22.2'nin `x11_suspend_screensaver` fonksiyonunda **başlatılmamış `g_x11_dpy`** ile XScreenSaver sorgusuna girdiğini gösterdi. Ek frontend'in bu tek çağrısına null-display guard eklenir. Patch yalnız bu ek frontend build'inde, birebir source-site doğrulamasıyla uygulanır; değiştirilmiş `x11_common.c` ve build tarifi corresponding-source kapsamında paketlenir. Standart frontend/core binary'leri korunur.

## Doğrulama durumu

- **57 farklı backend/native/backup-admission kontrolü**, **10 Node/yerelleştirme kontrolü**, TypeScript/worker/PWA build, viewer syntax ve actionlint geçti. Son native-control/display değişiklikleri kendi odaklı alt kümelerinde yeniden doğrulandı.
- Ayrı native frontend ve üretim Linux imajı build edildi. **Yedi sistem × standart/Selkies/standart = 21 gerçek oturum** geçti: görüntüde girdi değişimi, Opus negotiation, aynı-session retry, yanlış login, aynı save kimliği ve sıfır artık süreç. GBA'da gerçek RAM frame-counter iki yönde de tam olarak geri yüklendi; NES/GBA tonları sıfırdan farklı PCM'ye çözüldü.
- **Aktif Selkies oyunu sırasında graceful stop → aynı data volume ile container recreation** geçti; yeni checkpoint, yedi ROM/favori/kayıt ve boş native süreç durumu korundu. `verify-selkies.py` / `verify-selkies-container.py` yayın workflow'unda zorunlu kontrol olarak bağlıdır.
- Hızlı otomasyon hesap başına command sınırını doldurduğu için probe komutları aynı hesabı kullanan iki login arasında tempolandı. Üretim hız sınırı değiştirilmedi. İlk dört başarılı sistem tekrarlanmadı; kalan üçü aynı izole image/volume üzerinde tamamlandı ve önceki hata kaydı kanıtta korundu.
- **0.45.0 hosted yayını:** tempolu probe yedi sistemin **21 oturumunu tek çalıştırmada** tamamladı; active stop/recreate, checkpoint ve sıfır artık süreç yeniden doğrulandı. Yayın öncesi backend/server-lifetime kontrolleri de geçti.
- Tüm oyunlar izole, yerelde üretilmiş `ORIGINAL-ISOLATED` fixture'lardır. Kullanıcı ROM'u veya internet medyası geliştirme doğrulamasında çalıştırılmadı. Browser suite çalıştırılmadı.
- Bu fixture'lardan yalnız NES/GBA konsolun ses donanımında ton üretir; onlarda çözülmüş PCM'nin sıfırdan farklı olduğu da aranır. Diğerleri sessizdir; Opus negotiation doğrulanır, Selkies'in sessiz PCM'yi göndermemesi ses arızası sayılmaz.
- İlk **OpenGL ön denemesinin** GBA tanısında standart yol yaklaşık **64–68 ms**, Selkies **42–56 ms** input→decoded-frame örnekleri verdi. **VM + aiortc tanısıdır; çözünürlükler de farklıdır (240×160 / 640×480).** Son SDL2 yolunun eşdeğer kalite benchmark'ı, fiziksel TV gecikmesi veya Selkies'in her durumda üstün olduğu sonucu değildir. Varsayılan **standart** kalır.

Yerel kanıtlar `artifacts/selkies/` altında tutulur. Gerçek TV/PC'de seçim, fullscreen, ses, kumanda, uzun kullanım, gecikme ve yeniden açma kabulü **ACCEPT-SELKIES-01**'dir.

## Lisans ve başvurular

- Selkies **MPL-2.0**; pakette pixelflux/pcmflux MPL-2.0, vendored aiortc/aioice BSD-3-Clause + MPL değişiklikleri, python-xlib LGPL, Python ve diğer bağımlılık bildirimleri bulunur. Default pixelflux wheel'i **GPL-2.0-or-later x264/x265** taşır; bütün paketi yalnız MPL diye nitelemiyoruz.
- Ek RetroArch frontend **GPL-3.0** ailesindedir. Mevcut corresponding-source arşivi ve ek build tarifi imajda tutulur. Build, kurulu Python paket/lisans envanterini `SelkiesRuntime/licenses/` altında kaydeder. Home'un nihai dağıtım lisansı kararı açıktır.
- SDL2 **Zlib** lisanslıdır; sistem paket sürümü ve dağıtım copyright metni imaj envanterindedir. [SDL2 renderer seçimi](https://wiki.libsdl.org/SDL2/SDL_HINT_RENDER_DRIVER) ve [released RetroArch SDL2 sürücüsü](https://github.com/libretro/RetroArch/blob/69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576/gfx/drivers/sdl2_gfx.c) incelendi.
- SDL framebuffer seçimi: [SDL_HINT_FRAMEBUFFER_ACCELERATION](https://wiki.libsdl.org/SDL2/SDL_HINT_FRAMEBUFFER_ACCELERATION). X11 çağrı noktası: [released `x11_suspend_screensaver`](https://github.com/libretro/RetroArch/blob/69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576/gfx/common/x11_common.c#L268-L289). GDB yalnız izole tanıda kullanıldı; ürün paketine eklenmedi.
- Kontrol **2026-10-24**: [Selkies native kurulum](https://docs.selkies.io/latest/native/), [settings](https://docs.selkies.io/latest/settings/), [secure mode](https://docs.selkies.io/latest/secure-mode/), [port mux / firewall](https://docs.selkies.io/latest/firewall/), [lisanslar](https://docs.selkies.io/latest/licensing/), [developer reference](https://docs.selkies.io/latest/development/).
- Sabit Selkies kaynağı: [`6ee68a7a815956e6745e916741f13335e0b3ff53`](https://github.com/selkies-project/selkies/tree/6ee68a7a815956e6745e916741f13335e0b3ff53); [web core embedding](https://github.com/selkies-project/selkies/blob/6ee68a7a815956e6745e916741f13335e0b3ff53/addons/selkies-web-core/README.md), [signaling istemcisi](https://github.com/selkies-project/selkies/blob/6ee68a7a815956e6745e916741f13335e0b3ff53/addons/selkies-web-core/lib/signaling.js).
- RetroArch released action: [`command_load_state_slot`, 1.22.2 kaynağı](https://github.com/libretro/RetroArch/blob/69a4f0ea1e8aaf442ae4858f2e7f2b31a1776576/command.c#L740-L770). Context7 Selkies/RetroArch belgeleri installed/pinned kaynakla karşılaştırıldı; current master'daki yeni API'ler released binary'ye ait varsayılmadı.
