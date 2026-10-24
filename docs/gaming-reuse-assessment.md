# Sunucu emülasyonu: kanıtlanmış bileşenleri yeniden kullanma

İnceleme: **2026-10-19**. Gereksinimler **GAM-04/05/10**; kartlar **GAMING-03 / GAMING-REUSE-01**. Durum: **kaynak/belge karşılaştırması ve öneri; bağımlılık benimsemesi veya çalışan prototip değil**. Önizleme 0.33.0/şema 40, uygulama kodu, mevcut oyunlar/kayıtlar ve paused transferler değiştirilmedi.

**2026-10-23 güncellemesi:** Çalışan 0.44 tabanlı Home/CasaOS akışı üzerinden [gecikme ve yeniden kullanım araştırması](gaming-latency-research.md) yenilendi. Selkies **2.1.0** ve Moonlight Web **v3.0.0** yayımlandı; aşağıdaki 2026-10-19 sürüm tablosu tarihsel snapshot'tır. Güncel ilk karşılaştırmalı prototip adayı Selkies; uygulama/gerçek cihaz performans kararı açık.

## Kullanıcının bağlayıcı ürün yönlendirmesi

- Asıl hedef kullanıcının kendi ROM'larını **Home sunucusunda native emülatörle** çalıştırması; TV oyunu emüle etmez, görüntü/ses alır ve gamepad girdisi gönderir.
- Sunucu donanımını kullanıcı seçer; düşük cihaz gücü bütün sistemleri destekleme sözü veya kendi motorumuzu yazma gerekçesi değildir. Home doğru önkoşul/uyumluluk durumunu göstermeli, herkese PS2 performansı vaat etmemeli.
- Emülatör, yayın protokolü, encoder/decoder ve sanal gamepad motorları sıfırdan yazılmayacak. Önce gerçek örneklerin mimarisi, bakım/sürüm durumu, lisansları ve entegrasyon sınırları incelenecek.
- Tek Home deneyimi korunur. Yardımcı executable/service kullanmak ayrı ürün yönetimini kullanıcıya devretmek değildir; fakat yalnız arayüzleri gizlemek de güvenli entegrasyon sayılmaz.

## Somut uygulamalar ve olgunluk ayrımı

| Bileşen / örnek | Yeniden kullanılabilecek iş | Home'da kalan iş / önemli sınır | Değerlendirme |
|---|---|---|---|
| **RetroArch + denetlenmiş libretro çekirdekleri** | Native emülasyon, timing, audio/video/input sürücüleri, save/load | ROM/profil seçimi, readonly kaynak ve ayrı save/config alanları; CLI/yerel command adapter. API MIT olsa da RetroArch ve her core ayrı lisanslı | İlk retro runner için kendi libretro frontend'imizden daha düşük geliştirme riski [R1] |
| **PCSX2, Dolphin gibi standalone emülatörler** | İlgili sistemin native emülasyonu ve upstream oyun uyumluluğu | Her emulator için ayrı başlat/kaydet/çık adapter'ı. Her biri aynı RPC/save-state davranışını sağlamaz | Upstream uygulamayı kullan; tek hayali ortak emulator API'si varsayma [R2] |
| **Sunshine + native Moonlight** | Capture, encoder, ağ/protokol, ses ve uzak input; platform-native Moonlight decoder yolu | Home oturum/yetkisi, ROM launch, kayıt, pairing yaşam döngüsü. Sunshine web paneli browser oyun istemcisi değil. Capture ekran/desktop kapsamlı; tek uygulamayı başlatmak izolasyon sağlamaz | Uzun süredir kullanılan yayın ekosistemi; native istemci için karşılaştırma referansı. Home/TV kombinasyonu ayrıca kabul edilmeli [R3-R5] |
| **Wolf** | Linux üzerinde on-demand sanal Wayland ekranı/ses/input, ayrı app container'ları, GStreamer encoding ve Moonlight protokolü | Linux/udev/Docker/GPU sınırları; Home profil/kayıt/izinleri. Local UNIX-socket REST API arbitrary execution gücü taşıyor | Headless ve ayrı kullanıcı oturumları için amaca uygun aday; Windows çözümü değil [R6] |
| **Selkies** | Linux desktop capture/encoding + hazır browser client ve input | Browser/TV API desteği, Home session token/rol entegrasyonu, gereksiz desktop yetkilerinin kapatılması. Güncel sürümün default'u WebSocket/WebCodecs, WebRTC opt-in | Browser önceliğinde hazır bileşen adayı; eski WebRTC-only tasvirleriyle seçilmemeli [R7] |
| **RomM + webstation + broker** | Aynı ROM kataloğundan native emulator açma, stream link'i, atomik oturum sahipliği ve save/memory-card sync örneği | RomM Home'un katalog/backend'i olarak kurulmaz. Current webstation amd64 Linux/container; bir display bir oturum. Browser/native ve core save-state formatları ayrı | İstediğimiz akışın somut örneği; yeni entegrasyon ve değişen dağıtım. Uzun yıllar kanıtlanmış bütün-TV çözümü değil [R2] |
| **Moonlight Web** | Sunshine/Moonlight akışını browser'a WebRTC veya WebCodecs yolu ile köprüleme | Yeni ek katman ve auth/session entegrasyonu, TV decoder/input kabulü. Stable v2 ile development v3 farklı | Denenebilir bridge; native Moonlight'ın tüm olgunluk kanıtı bu projeye aktarılmaz [R8] |

**Olgun proje ≠ olgun her entegrasyon.** Proje yaşı, aktif bakım, release kanalı, platform desteği ve upstream testlerin kapsamı birlikte değerlendirilir. Yıldız/indirme sayısı güvenlik, oyun uyumluluğu veya Home input-to-photon kabulü yerine geçmez.

### 2026-10-19 GitHub metadata snapshot'ı

| Repo | Repo başlangıcı | API'nin latest non-prerelease kaydı | Kullanım notu |
|---|---|---|---|
| LizardByte/Sunshine | 2021-12-15 | `v2026.914.233613`, 2026-09-15 | Araştırmada release-tag getting_started da kontrol edildi; latest docs tek başına pinned dependency değil |
| moonlight-stream/moonlight-common-c | 2014-01-18 | Ayrı latest release kaydı yok | Official Moonlight istemcilerinin ortak C protokol çekirdeği; tüketiciyle eşlenmiş commit/submodule gerekir |
| moonlight-stream/moonlight-android | 2013-09-21 | `v12.2`, 2026-09-12 | Native decoder/input referansı; bütün Android TV modelleri onaylanmış değildir |
| games-on-whales/wolf | 2021-12-25 | `v2024.07`, 2024-07-10 | Default branch **beta**, stable docs/branch ayrı. Eski GitHub release tarihi container bakımının bittiğinin kanıtı değil; seçilecek image digest ayrıca incelenmeli |
| selkies-project/selkies | 2021-09-20 | `2.0.0`, 2026-09-23 | 2.0.0 README ve main ayrıştırıldı; sonraki main özellikleri otomatik stable sayılmaz |
| MrCreativ3001/moonlight-web-stream | 2025-08-24 | `v2.10.0`, 2026-05-29 | Master v3 geçişinde ortak C çekirdeği yerine Rust implementation değişimi bildiriyor; v2/v3 aynı bileşenmiş gibi ele alınmaz |
| romm-streaming/romm-webstation | 2026-09-27 | Ayrı latest release kaydı yok | Önceki docker-webstation RomM dalının devamı; yeni repo yaşı tüm bileşenlerin yaşı değildir. Yine de bu birleşimin uzun dönem TV kabulü kanıtlanmadı |

## Framework kullanmak ile yeni oyun-yayın motoru yazmak arasındaki fark

- **moonlight-common-c:** Resmi istemcilerin kullandığı protokol kütüphanesi; Home native client için yeniden kullanım noktası. TV decoder, rendering, ses ve app lifecycle entegrasyonu yine gerekir. Upstream kendi değiştirilmiş **ENet submodule** sürümünü zorunlu tutuyor; rastgele sistem ENet'iyle bağlamak ABI/API uyumsuzluğu ve crash riski.
- **GStreamer:** Capture/encode/media pipeline ve WebRTC bileşenleri için olgun framework. Wolf bunu zaten kullanıyor. Hazır host'u kullanmak yerine yalnız GStreamer seçmek; input, session isolation, signaling/auth, keyframe/queue yönetimi ve platform entegrasyonunu bize bırakabilir.
- **libdatachannel:** C/C++ WebRTC media transport, data channels ve packetizer/RTCP işleri. Encoded video/audio taşır; tek başına emulator, capture/encoder/decoder veya tamamlanmış game-stream host değildir. Codec packetizer'ı decoded pixel üretimiyle karıştırılmamalı.
- **libretro API:** Callback sözleşmesi emülatör değildir; kendi frontend'imiz audio/video/input/scheduling ve GPU context işleri üstlenir. İlk dilimde bunu sıfırdan yapmak, kullanıcının geliştirme riski sınırına ters.

Bu yüzden ilk tercih **hazır çalışan runner + hazır çalışan yayın stack'ini Home'dan yönetmek**; çıplak kütüphanelerden yeni bir oyun-yayın motoru kurmak değil. Upstream'i büyük ölçüde değiştirmek/fork'lamak da bakım maliyetini bize taşır; küçük, sürüm-testli adapter sınırları tercih edilir.

## Önceki servis açıklamasının düzeltilmesi

“Oyun yokken ayrı servis kesinlikle çalışmayacak” bir benimseme öncesi varsayımdı; garantilenemez.

- Emülatör/game process'i kullanıcı Oyna dediğinde başlatılıp çıkışta kapatılabilir. **Sunshine'ın Windows installer'ı varsayılan olarak background service kurar**; Linux'ta user service de belgelenir. Wolf/örnek webstation container'ı da istek bekleyen host olarak kalabilir.
- Sürekli açık host ile aktif capture/encode/oyun çalışması aynı şey değildir. Idle memory/CPU ve session sonrası cleanup ölçülmeden “sıfır kaynak” denmez.
- Host'u demand-start yapmak teknik olasılıktır, ürün koşulu değildir; upstream'in desteklediği service/session modelini bozup kendi launcher sorunlarımızı üretmemeliyiz. Home kurulum/teşhis/update akışını sahiplenebilir; OS driver/service izinleri veya yeniden başlatma ihtiyacı gizlenmez.

## Windows için somut benimseme engeli: sanal gamepad

Sunshine **release-tag** belgesi Windows'ta iki backend tarif ediyor [R3]:

1. **ViGEmBus:** Xbox 360/DS4 için mevcut alternatif; **end-of-life**. Battle-tested olması aktif bakım riskini ortadan kaldırmaz.
2. **LizardByte Virtual HID Driver:** Aktif geliştirilen daha geniş cihaz backend'i; **driver-backed cihazlar için ücretli machine license** gerektiriyor. Sürücü lisansı, dağıtım/kurulum ve update koşulları Sunshine GPL lisansıyla çözülmez. Araştırmada ödeme/kurulum yapılmadı, ücretli bileşen benimsenmedi.

Bu nedenle “Windows'ta Sunshine ücretsizdir, tüm input zinciri de ücretsiz/uzun vadeli garantilidir” sonucuna varılamaz. Windows önizlemesini Linux'a taşımak da otomatik karar değildir. Linux Wolf/inputtino/uinput yolunun avantajı ayrı değerlendirilebilir; platform kapısı kapatılmadan kesin host seçimi yapılmaz.

## Önerilen yeniden kullanım sınırı — karar değil

```text
Home: ROM kataloğu + profil + yetki + Oyna/kaydet/çık + kurulum/teşhis
                                |
          hazır native emulator: RetroArch / uygun standalone emulator
                                |
      hazır yayın host'u: native-client referansı Sunshine; Linux aday Wolf
                                |
       Home istemcisi: official Moonlight bileşenleriyle native player
             browser gerekiyorsa ayrıca Selkies / bridge değerlendirmesi
```

- Native Moonlight yolunu **referans** almak en az yeni media/input motoru işini bırakır; desteklenen TV/native Home player için aday. İlk stable web/PWA hedefi değiştirildi veya Home TV app dağıtımı onaylandı sayılmaz.
- **Browser zorunluysa:** Linux/Selkies ve Sunshine/browser bridge karşılaştırılmalı. “Tarayıcıda da native kadar battle-tested, her TV'de hazır” sonucu mevcut kaynaklardan çıkmıyor. Vendor TV sınırları [yürütme araştırmasında](gaming-execution-research.md).
- Home'un kendi katmanı gerçekten gerekir: actor/device/game-bound session, oturum sahipliği, izin iptali, managed save publication, restart recovery ve gamepad'in Home navigation/gameplay arasında sahiplik değişimi. Upstream PIN pairing bu profil izinlerinin yerine geçmez.
- Upstream yönetim/API soketleri normal kullanıcıya veya LAN'a genel yetkiyle açılmaz. Wolf local API arbitrary command çalıştırabiliyor; Selkies clipboard/file transfer/mic/webcam/command gibi masaüstü yetenekleri oyun için gereksizse kapatılır. Sunshine desktop capture'ın özel host masaüstünü gösterebilmesi kabul öncesi güvenlik kapısıdır.
- ROM/BIOS kullanıcıdan, read-only kaynak alanından gelir; kayıtlar emulator/core/build/profile kapsamında ayrı tutulur. Kill/disconnect sonrası son kareyi kesin save-state'e dönüştürme veya bütün emulator save formatlarını ortaklaştırma sözü verilmez.

## Lisans / dağıtım kapısı

Araştırma özeti: RetroArch GPLv3, API MIT; core lisansları ayrı. Sunshine ve Moonlight ortak C/Android GPLv3; ortak C lisansında **App Store Distribution Exception** var, bütün Home uygulamasını otomatik izinli yapmaz. Wolf MIT; Selkies ve libdatachannel MPL-2.0. GStreamer çekirdek LGPL; plugin/codec build koşulları ayrı. romm-webstation GPL-3.0 ve **romm-broker AGPL-3.0**; bunu kopyalamak “yalnız genel fikir” değildir [R2, R9].

Home dağıtım lisansı, source/notice yükümlülükleri, kernel-driver/code-signing ve TV store koşulları çözülmeden paketleme veya component embedding onaylanmaz. Ayrı process kullanmak lisans yükümlülüklerini kendiliğinden kaldırmaz.

## Koda geçmeden çıkış kapısı

Seçilecek **tek** referans kombinasyonu için: desteklenen host/istemci, immutable build/digest ve lisanslar; command/session sınırı; desktop/input izolasyonu; emulator save semantics; idle/start/exit/recovery davranışı açıklanmalı. Sonra açık kullanıcı onayıyla kendi sağladığı yetkili oyun üzerinden küçük deneme yapılır. Donanım seçimi kullanıcıda; Home destek/önkoşul göstergelerini doğru sunar. Gerçek browser/TV/input-to-photon kabulü kullanıcıdadır.

**Bu tur yalnız belge/kaynak araştırmasıdır.** Emulator/service/container/ROM/BIOS kurulmadı veya çalıştırılmadı; internet media acquisition, otomatik browser suite, build/test, preview güncellemesi, commit/push yapılmadı. Kaynakların bütünlük ve güvenlik testleri Home entegrasyonunun yerine geçirilmedi.

## Kaynaklar

Tümü **2026-10-19** kontrol edildi. GitHub metadata için her listed repo'nun `/repos/{owner}/{repo}` ve `/releases/latest` API'si kullanıldı; release tarihi/maturity notu bu snapshot'tır. Context7 üzerinden RetroArch, GStreamer, libdatachannel ve Selkies scope/API belgeleri sorgulandı; örneklerin main branch özellikleri kurulu stable özellik gibi sunulmadı.

- **R1:** https://docs.libretro.com/development/licenses/ ; https://github.com/libretro/RetroArch/blob/master/retroarch.c ; https://github.com/libretro/RetroArch/blob/master/command.h
- **R2:** https://docs.romm.app/latest/using/emulator-streaming/ ; https://github.com/romm-streaming/romm-webstation ; https://github.com/romm-streaming/romm-broker
- **R3:** https://github.com/LizardByte/Sunshine/blob/v2026.914.233613/docs/getting_started.md ; https://docs.lizardbyte.dev/projects/sunshine/latest/md_docs_2getting__started.html
- **R4:** https://docs.lizardbyte.dev/projects/sunshine/latest/md_docs_2app__examples.html ; https://docs.lizardbyte.dev/projects/sunshine/latest/
- **R5:** https://github.com/moonlight-stream/moonlight-common-c ; https://github.com/moonlight-stream/moonlight-common-c/blob/master/LICENSE.txt ; https://github.com/moonlight-stream/moonlight-android
- **R6:** https://games-on-whales.github.io/wolf/stable/dev/how-it-works.html ; https://games-on-whales.github.io/wolf/stable/dev/api.html
- **R7:** https://github.com/selkies-project/selkies/tree/2.0.0 ; https://github.com/selkies-project/selkies/blob/main/docs/components/index.md ; https://github.com/selkies-project/selkies/blob/main/src/selkies/settings.py
- **R8:** https://github.com/MrCreativ3001/moonlight-web-stream/tree/v2.10.0 ; https://github.com/MrCreativ3001/moonlight-web-stream/blob/master/README.md
- **R9:** https://github.com/romm-streaming/romm-webstation/blob/main/README.md ; https://github.com/romm-streaming/romm-broker/blob/master/LICENSE
- **R10:** https://github.com/paullouisageneau/libdatachannel ; https://github.com/paullouisageneau/libdatachannel/blob/master/DOC.md ; https://gstreamer.freedesktop.org/documentation/webrtc/index.html
