# TV'yi Home içinde oyun istemcisine dönüştürme — araştırma

Araştırma tarihi: **2026-10-19**. Durum: **yalnız araştırma ve öneri; mimari/dependency seçimi onaylanmadı, oyun desteği uygulanmadı**. Steam/PC oyun mağazası ve ROM edinme bu tartışmanın kapsamı dışında. Kullanıcı kendi oyun dosyalarını sağlar. Çalışan önizleme, indirmeler, tercihler ve ürün kabul panosu değiştirilmedi; bu belge yeni bir tamamlanmış gereksinim dilimi değildir.

**Sonraki yönlendirme:** Kullanıcı ROM + sunucuda native yürütmeyi ana hedef yaptı; server donanım seçimini kullanıcıya bıraktı ve battle-tested runner/yayın bileşenlerini yeniden kullanmayı şart koştu. [Yeniden kullanım karşılaştırması](gaming-reuse-assessment.md) bu yönü, hazır ürün/kütüphane ayrımını, gerçek service modelini ve Windows virtual-gamepad lisans/bakım engelini kaydeder. Bu ürün yönü dependency/platform veya uygulama kodu onayı değildir.

## Sonuç

Zayıf TV hedefinde yalnız EmulatorJS'ye dayalı tarayıcı emülasyonu güvenilir bir genel çözüm değildir. Sunucuda native emülasyon + düşük gecikmeli yayın piyasada uygulanmış bir yöntemdir. Ancak **normal TV tarayıcısı, paketlenmiş TV uygulaması ve native istemci aynı yeteneklere sahip değildir**. Sunucunun güçlü olması, TV'nin decoder/input/API eksiklerini çözmez.

Öneri: tek Home oyun kütüphanesi ve Oyna akışı altında, **sunucu emülasyonu/yayın öncelikli ve istemciye göre farklılaşan bir oynatıcı** değerlendirmek. Yerel tarayıcı emülasyonu güçlü istemciler için yardımcı yol; desteklenmeyen TV'lerde bir HDMI istemcisi son seçenek. Bu öneri henüz kullanıcı kararı değildir. Hiçbir yol için bütün TV'lerde uyumluluk veya Home üzerinde ölçülmüş gecikme iddiası yoktur.

## Piyasadaki uygulamalar

| Ürün / bileşen | Belgelenen yöntem | Home için ders / sınır |
|---|---|---|
| RomM, EmulatorJS | RetroArch çekirdeklerini WASM olarak istemcide çalıştırır; kayıtları sunucuya eşitler [S1] | ROM kütüphanesi ve web oyunu birleştirilebilir; zayıf cihazlarda performans uyarısı var |
| RomM, Emulator Streaming | Sunucuda RetroArch, PCSX2, Dolphin vb. native emulator; webstation/Selkies masaüstü yayını ve broker üzerinden başlat/kaydet/çık [S2, S3] | İki yürütme yolu aynı katalogda bulunabilir; bir container bir oturum tutar. Belgelenen RomM imajı amd64-only. Yeni/migrasyon yaşayan entegrasyon, bütün TV'lerde kabul kanıtı değil |
| webЯcade | Gamepad merkezli tarayıcı emülasyonu [S4] | PS1 için modern PC/Mac, yeni ve hızlı telefon veya Xbox gibi cihazlar öneriyor; ucuz TV garantisi vermiyor |
| Sunshine + Moonlight | Host uygulamasını yakalama, düşük gecikmeli görüntü/ses kodlama, istemciden gamepad girdisi [S5, S6] | Steam gerektirmez; native emulator da çalıştırılabilir. Windows/Linux için mevcut protokol ekosistemi; web yönetim ekranı browser oyun istemcisi değildir |
| Moonlight TV / Tizen | LG webOS topluluk native portu; Samsung Tizen için WASM ve vendor API kullanan paketlenmiş port [S7, S8] | TV decoder'ına uygun entegrasyon mümkün. Sideload/developer-mode, controller ve bakım sınırları kullanıcı kurulumunu etkiler |
| Wolf | Moonlight protokolü; Linux/Docker-first, ihtiyaca göre sanal ekran ve ayrı kullanıcı oturumları [S9] | Headless/multi-session host için aday; Home'un mevcut Windows dağıtımına doğrudan taşınmış çözüm değil |
| GeForce NOW TV | Oyun uzak sunucuda, seçilmiş Samsung/LG TV'lerde mağazadan kurulan istemci [S10] | Ticari örnek, TV işlemcisinin oyunu çalıştırması gerekmediğini gösterir. NVIDIA'nın model/firmware ve mağaza desteği Home'a otomatik aktarılmaz |
| Batocera | TV'ye bağlı bilgisayar/SBC üzerinde işletim sistemi ve native emulator [S11] | En öngörülebilir yerel yürütme yolu; kullanıcıyı Home yerine ayrı ürün arayüzüne taşımak bu ürünün hedefi değil |

RomM streaming belgesi 2026-10-10'da güncellenmiş; incelenen romm-webstation deposu GitHub metadata'sına göre 2026-09-27'de oluşturulmuş. Bu, yöntem için somut referanstır, uzun süreli cihaz/ürün doğrulaması değildir. Topluluk Tizen portunun README'si bakımın son aktif yılına yaklaşma ve issue kısıtları bildiriyor [S8]; Home'un bakım sorumluluğu ayrıca düşünülmeli.

## TV platformları: belirleyici bulgular

### Samsung Tizen

- Resmi Cloud Game Applications belgesi WebRTC tabanlı Game Mode'u **2022 ve sonraki modeller / M85+ web engine** için tarif ediyor [S12].
- Mod uygulamanın `config.xml` metadata'sıyla etkinleştiriliyor. Normal tarayıcı sekmesinden Home sayfası açmanın bu paketlenmiş uygulama yetkisini sağladığı varsayılamaz.
- SDP'de çözünürlük/FPS sınırları, tek video + ses track'i, foreground/background davranışı ve FIR cevabı gibi TV'ye özgü gereksinimler var.
- Samsung ayrıca WASM Player ile elementary stream paketlerini donanım decoder'ına besleme ve **Ultra Low Latency** modunda görüntü iyileştirmelerini kapatma API'si sunuyor [S13].
- Samsung'un kendi Moonlight portlama örneği, özel raw TCP/UDP socket uzantıları ve WASM Player kullanıyor [S14]. WASM burada oyunu emüle etmek için değil, yayın istemcisini TV'ye taşımak için kullanılıyor.
- Topluluk Tizen Moonlight portu Tizen 5.5 / 2020+ diyor [S8]. Bu, resmi WebRTC Game Mode'un 2022+ sınırıyla çelişmez: farklı aktarım/API yolları.

### LG webOS

- LG geliştirici forumunun **2026-01-07** tarihli yanıtı: “WebRTC is only available on contracted partner apps” [S15]. Soru yalnız alma/oynatma içindi; kamera/mikrofon istemiyordu.
- Dolayısıyla sıradan Home web/PWA uygulamasında LG WebRTC yayını genel olarak vaat edilmemeli. Bu yanıt her firmware'in tüm API'leri için eksiksiz bir matris değildir; model/firmware ve dağıtım yolu ayrı doğrulanmalı.
- Moonlight TV topluluk native portunun varlığı başka bir teknik yolun mümkün olduğunu gösteriyor [S7]. Bu, LG Content Store erişimi veya resmi Home ortaklığı anlamına gelmez.
- RetroArch'ın upstream platform listesinde LG webOS da var [S16]; native emülasyon mümkündür ama TV CPU/GPU sınırları ve kurulum/dağıtım koşulları ortadan kalkmaz.

### Android / Google TV, mobil ve HDMI cihazı

- Moonlight'ın Android ve iOS/tvOS native istemcileri mevcut [S6]. Android/Google TV Home uygulaması için native decoder + input köprüsü değerlendirilebilir; her kutu/model/controller kabul edilmiş sayılmaz.
- Mobil Home uygulamasını yalnız WebView'a sarmak, eksik decoder/gamepad API'lerini veya işletim sistemi izinlerini otomatik çözmez. Yerel emülasyon ya da yayın için native oyuncu modülü gerekebilir.
- Eski/kısıtlı TV'de HDMI kutusu/mini PC, TV işletim sistemi bağımlılığını azaltır. Basit streaming istemcisi için ağır emulator donanımı gerekmez; yerel PS2 yürütmesi için ise kutunun CPU/GPU'su yeterli olmalıdır.
- Cast/mirroring veya film HLS hattı, düşük gecikmeli gamepad kontrollü yayın yerine geçmiş kabul edilmez.

## Yeni / alternatif teknolojiler

1. **WebRTC:** Browser'da medya ve ayrı input kanalı için yaygın yol; LAN başlangıcı için aday. ICE, güvenilir HTTPS, codec görüşmesi ve TV vendor davranışı ayrıca gerekir. UDP kullanımı kuyruk/paket kaybı yönetimi avantajı sağlayabilir; düşük gecikme tek başına protokol adından çıkarılamaz.
2. **WebCodecs + WebSocket:** Selkies'in incelenen `main` dokümanı bunu varsayılan, WebRTC'yi opt-in taşıma olarak tarif ediyor [S17]. WebCodecs varsa video paketlerinin decoder'a doğrudan verilmesi mümkün. TCP paket kaybında bekleme oluşturabilir; kuyruk ve frame düşürme yönetimi önemlidir. WebRTC'siz yol olması eski TV uyumluluğunu garanti etmez. JPEG/software fallback'in “ekran açıldı” sonucu oyun için yeterli değildir.
3. **Moonlight Web köprüsü:** Sunshine trafiğini WebRTC ile tarayıcıya aktaran bağımsız topluluk projesi var [S18]. İncelenen `master` geliştirme belgesinde WebSocket/WebCodecs yolu da var; README kararlı belgelerin `v2.10.0` dalında olduğunu söylüyor. Geliştirme dalı özellikleri kararlı Home bağımlılığı gibi sunulmaz.
4. **WebGPU + WASM / yeniden derleme:** İstemcide bazı emulator işlerini hızlandırabilir; sunucuya taşımaz. `ps2-web-recomp` oyun makine kodunu önceden C++/WASM'a çevirip WebGPU runtime kullanıyor [S19]. README iki oyun üzerinde çalışma bildiriyor, başka oyunlarda eksik syscall/IOP/GS özellikleri beklenmesini söylüyor. Genel ROM yükle/oyna motoru veya zayıf TV çözümü olarak seçilmez; oyunlardan türetilmiş kodun hakları ayrıca var.
5. **Play!.js:** Browser PS2 deneyi gerçekten var [S20]; resmi sayfası desktop/mobile sürümün desteklediği bütün oyunları oynatmayı hedeflemediğini açıkça söylüyor. EmulatorJS'de PS2 olmaması, internette hiçbir browser PS2 deneyi olmadığı anlamına gelmez.
6. **Tizen özel düşük gecikmeli decoder API'leri:** Bu hedef için WebGPU'dan daha doğrudan ilgili; zayıf TV'de emülasyon değil hazır görüntünün verimli oynatılması ihtiyacını karşılıyor [S12-S14]. Vendor/model/dağıtım sınırı nedeniyle evrensel web çözümü değil.

WebCodecs `hardwareAcceleration` bir tercihtir; destek sorgusu actual düşük gecikmeli donanım decoder performansını kanıtlamaz [S21]. Yeni codec (HEVC/AV1) seçimi de iki uçta donanım desteği, profil ve ölçüm gerektirir. İlk araştırma denemesinde H.264 8-bit/4:2:0, 720p/60 ve sonra 1080p/60 gibi yaygın bir başlangıç profili önerilebilir; bunlar kabul kriteri/sunum garantisi değildir.

## Home'a uygun, henüz onaylanmamış mimari

```text
Home oyun kütüphanesi / Oyna / kayıt / profil
                 |
         oturum ve yetenek seçimi
            /              \
Home sunucusunda         yeterli istemcide
native emulator         yerel WASM/native emulator
      |
düşük gecikmeli yayın
      |
browser player VEYA Home TV/native player
```

- Katalog, ROM kimliği, yetki, emulator seçimi, kayıtlar ve oturum yaşam döngüsü Home'a aittir. RomM veya Batocera'yı ürünün arkasına kurup kullanıcının ayrı yönetim ürünlerini birleştirmesi istenmez.
- Native emulator/protokol bileşenleri, lisans ve dağıtım koşulları çözülürse Home kontrollü worker/player olarak kullanılabilir. Her şeyi sıfırdan yazma veya her paketi tek process'e gömme zorunluluğu yoktur.
- Mevcut Windows-first dağıtımda **native RetroArch/seçilmiş emulator + Sunshine/Moonlight yolu** ilk değerlendirme adayıdır; browser için bridge gerekir. Sunshine'ın capture/input/interactive desktop gereksinimleri Home API process'inden ve Windows service/session sınırlarından ayrılmalıdır [S5].
- Linux/headless ürün hedefi için **Wolf** ve browser önceliğinde **Selkies** ayrıca değerlendirilebilir. Linux desktop/container çözümü mevcut Windows sunucusunda native .NET bileşeniymiş gibi sunulmaz.
- Doğrudan libretro callback'lerinden video/ses alıp kendi publisher'ımıza beslemek teknik alternatif [S16]; desktop capture'ı azaltabilir ama GPU çekirdekleri, ses/input/scheduling ve streaming stack geliştirme yükü vardır. İlk seçim olarak henüz gerekçelendirilmedi.
- TV için sunucuda yürütme öncelikli; güçlü telefon/PC için yerel emülasyon isteğe bağlı. Kullanıcıya sürekli motor seçtirmek yerine uygun yolu seçmek hedef, kör/sessiz uyumsuz runner veya kayıt değişimi değil.
- Başlangıç aday kapsamı tek ev içi oturum ve doğrulanmış küçük 2D/PS1 setidir. PS2 için PCSX2 ayrı native runner adayı [S22]; oyun ve sunucu donanımı temelinde değerlendirilir. Zayıf server + zayıf client'a PS2 garantisi verilmez.

## Yayının gerçek maliyetleri ve korunacak sınırlar

- Girdi → emulator → capture → encode → ağ → decode → panel/ses gecikmesi birlikte ölçülmeli; ping veya encoder süresi toplam input-to-photon süresi değildir. Ağ bufferbloat, Bluetooth input, TV görüntü/ses işleme de önemlidir. Kaynaklardaki gecikme/FPS rakamları Home sonucu değildir.
- Donanım encoder'ı için ayrı gaming GPU her zaman şart değildir; uygun iGPU yardımcı olabilir. Emulator render yeteneği ve encoder yeteneği ayrı kontrol edilir. Software encode küçük retro yayında değerlendirilebilir, zayıf sunucuda otomatik kabul edilmez.
- Film indirme sırasında ağ/disk/CPU yükü, upload kaynaklı kuyruklar ve varsa video transcoding kaynak paylaşımı düşünülmeli. Home'un indirmeleri kullanıcı izni olmadan durdurması/resume etmesi çözüm değildir.
- Oturum token'ı profil/cihaz/oyun yetkisine bağlı, kısa ömürlü ve iptal edilebilir olmalı. Yayın worker'ı masaüstü, clipboard, file transfer veya yönetim console'unu genel kullanıcıya açmamalı. ROM ve BIOS read-only; yalnız yönetilen profil kayıt alanına yazma.
- Gamepad gezinme ve gameplay ayrı sahiplik katmanlarıdır; bağlantı kopuşunda tüm held input neutral yapılır. Var olan controller navigation'ın olması emulator input desteğinin uygulanmış olduğu anlamına gelmez.
- Save file/memory card ile save state ayrı tutulmalı. Runner/core/build kimliği kayıtla eşlenmeli; browser/native arasında bütün snapshot'ların uyumlu olduğu varsayılmamalı [S1, S2]. Kopuş/crash/restart ve eşzamanlı kayıt yazımı açıkça ele alınmalı.

## Lisans ve benimseme kapısı

Bu araştırmada paket/kod/BIOS/ROM indirilip ürüne eklenmedi. Aşağıdaki kimlikler upstream lisans özeti ve 2026-10-19 GitHub repo metadata kontrolüdür; seçilecek pinned build, transitif bileşen, kaynak sağlama, codec ve mağaza koşullarının tam dağıtım denetimi değildir.

| Aday | İlk lisans bulgusu | Açık iş |
|---|---|---|
| libretro API / RetroArch | API MIT; RetroArch GPLv3 [S23] | Kullanılan frontend ve her çekirdek ayrı değerlendirilir |
| EmulatorJS | GitHub metadata GPL-3.0 | WASM/çekirdek paketlerinin ayrı lisansları ve Home birleşim biçimi |
| Sunshine / Moonlight Android / Tizen / Moonlight Web | GPL-3.0 metadata; Tizen README GPLv3 [S8] | Kaynak/notice/dağıtım ve değiştirilmiş istemci koşulları |
| Wolf | MIT [S9] | Container, emulator, GStreamer/codec ve diğer bileşen lisansları ayrı |
| Selkies | MPL-2.0 | pixelflux/codec build seçimi; x264/x265 gibi GPL bileşenler ayrı |
| romm-webstation | GPL-3.0 metadata | Yalnız referans; Home'a benimsenmedi |
| ps2-web-recomp | MIT ve Play! BSD-2-Clause notice [S19] | Oyunlardan türetilen C++/WASM'ın hakları bu lisanslarla verilmez |

Libretro lisans tablosu bazı yaygın çekirdeklerde non-commercial koşullar gösteriyor; bütün SNES/Genesis/Arcade paketlerini güvenli tek lisans altında varsaymak yanlış [S23]. Ürün dağıtım lisansı hâlâ kararsızdır. Kullanıcı ROM sağlar; gerektiğinde kendi BIOS'u da gerekir. Emulator lisansı oyun/BIOS dağıtım hakkı sağlamaz.

## Karardan önce önerilen dar doğrulama — çalıştırılmadı

1. Kullanıcının gerçek TV model/firmware'i, gamepad'i ve Home sunucu CPU/GPU/işletim sistemi kaydedilsin; normal browser ile kurulabilir TV uygulaması ayrıştırılsın.
2. Kullanıcı onayıyla, aynı yetkili küçük oyun üzerinden tek native oturum ve iki istemci yolu karşılaştırılsın; ticari ROM/BIOS veya internet medya indirmesi geliştirme doğrulamasında başlatılmasın.
3. 720p/60 temel profilde toplam girdi/ekran gecikmesi, p95 kuyruk/jitter, görüntü kaybı, ses senkronu ve controller mapping incelensin. Sonra 1080p/60 ve eşzamanlı indirme yükü. Mutlak performans eşiği önceden kullanıcıyla kararlaştırılsın.
4. Pause/menu/input sahipliği, güvenli çıkış, save/restore, controller/network kopuşu ve worker restart koruması doğrulansın. Browser/device kabulünü kullanıcı yapar; izinsiz otomatik browser suite yok.
5. Sonuca göre bir streaming transport/runner ve sınırlı cihaz listesi seçilsin. Desteklenmeyen TV'ye açık uyarı veya HDMI istemcisi seçeneği; “her TV konsol oldu” iddiası yok.

## Kaynaklar

Kaynakların tümü **2026-10-19** tarihinde incelendi. `latest`, `main`, `master` belgeleri değişkendir; bu araştırma pinned ürün build'i değildir. Context7 üzerinden Selkies ve MDN belgeleri ayrıca sorgulandı.

- **S1:** https://docs.romm.app/latest/using/in-browser-play/emulatorjs/
- **S2:** https://docs.romm.app/latest/using/emulator-streaming/
- **S3:** https://github.com/romm-streaming/romm-webstation ve https://romm-streaming.github.io/romm-broker/docs/container/
- **S4:** https://docs.webrcade.com/apps/emulators/psx/
- **S5:** https://docs.lizardbyte.dev/projects/sunshine/latest/
- **S6:** https://github.com/moonlight-stream/moonlight-android
- **S7:** https://github.com/mariotaku/moonlight-tv ve https://github.com/mariotaku/moonlight-tv/wiki/Gamepad-Setup
- **S8:** https://github.com/brightcraft/moonlight-tizen
- **S9:** https://games-on-whales.github.io/wolf/stable/
- **S10:** https://nvidia.custhelp.com/app/answers/detail/a_id/5410/~/how-can-i-setup-my-smart-tv-with-geforce-now
- **S11:** https://wiki.batocera.org/start
- **S12:** https://developer.samsung.com/smarttv/develop/guides/cloud_gaming/cg_implement_webapp.html
- **S13:** https://developer.samsung.com/smarttv/develop/extension-libraries/webassembly/tizen-wasm-player/overview.html
- **S14:** https://developer.samsung.com/smarttv/develop/extension-libraries/webassembly/game-streaming-with-wasm.html
- **S15:** https://forum.webostv.developer.lge.com/t/inquiry-regarding-webrtc-support-on-webos-tv/28059 (yanıt 2026-01-07; topic JSON üzerinden tam metin de kontrol edildi)
- **S16:** https://github.com/libretro/RetroArch/blob/master/README.md ve https://github.com/libretro/RetroArch/blob/master/pkg/emscripten/README.md
- **S17:** https://github.com/selkies-project/selkies/blob/main/docs/components/index.md
- **S18:** https://github.com/MrCreativ3001/moonlight-web-stream (incelenen README: `master`, geliştirme dalı)
- **S19:** https://github.com/snuri00/ps2-web-recomp
- **S20:** https://playjs.purei.org/
- **S21:** https://developer.mozilla.org/en-US/docs/Web/API/VideoDecoder/configure ve https://developer.mozilla.org/en-US/docs/Web/API/VideoDecoder/isConfigSupported_static
- **S22:** https://pcsx2.net/docs/setup/requirements/
- **S23:** https://docs.libretro.com/development/licenses/

Doğrulama durumu: **belge ve upstream kaynak araştırması yapıldı; yerel emulator/streaming çalıştırılması, TV performans testi veya kod/build testi yapılmadı.** Uygulama kodu, bağımlılıklar, schema ve runtime sürümü değişmedi.
