# Gamepad ve TV kumandasıyla gezinme — INPUT-01

Tarih **2026-10-18**, geliştirme sürümü **0.30.0-dev.1**, runtime hedefi **0.30.0**, şema **39**. Gereksinimler **CLI-01/05**, **PLY-06/08**, alt ID **CLI-01-F**. Bu dilim medya uygulamasının gezinmesidir; oyun çalıştırma/streaming veya Gaming Mode eklenmez.

## Kullanıcı kararı ve kapsam

Fare kullanıcısının standart görünümü değişmez. Gamepad bağlamak tek başına mod değiştirmez; gerçek gezinme girdisiyle belirgin odak/tuş ipuçları görünür. Mouse hareketi/touch/pointer veya wheel ile pointer görünümüne dönülür. Normal yazı girişi, caret/Enter/Backspace, Tab, Ctrl/Cmd-K ve mouse/Tab sonrası Video.js klavye kısayolları korunur. **Düzen/boyut/density değişikliği veya Steam UI kopyası yoktur**; konsol benzeri odak gezinmesidir. Ayarlara kapatılabilir Gamepad ve kumanda bölümü eklenir; giriş ekranına/fare kataloğuna sürekli yeni panel eklenmez.

Ortak provider App'in üstündedir: giriş/PIN, online/offline katalog, raf/reader, yönetim formları ve oynatıcı aynı giriş katmanındadır. Sol analog/D-pad geometrik seçim; sağ analog aktif bölgeyi kaydırır. Gerçek DOM üzerinden yalnız görünür/bağlı/izinli/enabled kontrol veya kartlar adaydır; hidden/inert/aria-hidden ve disabled hedefler atlanır. Yatay rafta yerel komşu önce seçilir; dikey gezinme sütunu korur. Focus görünür alana scroll edilir; reduced-motion'da instant scroll kullanılır. Katalog media kimlikleri/route hafızası ve mevcut history/scroll/focus mekanizması birlikte korunur.

## Standart eşleme

| Girdi | Gezinme | Oynatıcı |
|---|---|---|
| Sol analog / D-pad | Yönlü odak | Kontroller arasında gezinme |
| Sağ analog | Aktif liste/sayfayı kaydır | Aktif paneli kaydır |
| A / ✕ | Seç / onay / alanı düzenle | Oynatma yüzeyinde oynat/duraklat |
| B / ○ | Önce açık menü/modal, sonra sayfa geri | Önce menü/panel; fullscreen çıkışı önce, sonra oynatıcı geri |
| X / □ | Oynatıcı dışında komut yok | Oynat/duraklat |
| Y / △ | Sayfada arama / ekran klavyesi | Açık panelin kendi araması dışında global yönlendirme yok |
| LB / RB | Birincil bölüm geçişi | Kişisel kuyrukta önceki/sonraki; ortak odada yerel queue değiştirilmez |
| LT / RT | Aktif uzun sayfa/listeyi kaydır | 10 saniye geri/ileri; port/oda izni uygulanır |
| Start / Options | Daha fazla | Home oynatma ayarları; açık popup önce kendi girdisini tüketir |

TV/klavye `Arrow*`, OK/Enter, Back ve media play/pause/seek tuşları ortak komuta çevrilir. Legacy yön/Enter kodları, Samsung `10009` ve webOS `461` Back kodları desteklenir; **bu, bütün marka/model/firmware'lerde cihaz kabulü iddiası değildir**. Sayfalardaki yön tuşları opt-in açıkken spatial navigation'dır; editable alanlar ve bileşik kontrolün native keyboard davranışı korunur.

## Güvenlik ve yaşam döngüsü

- `GamepadReader` saf, DOM/ağsız state machine'dir; `navigator.getGamepads()` güncel her örnekte okunur, eski Gamepad referansı tutulmaz. Cihaz yokken 1 saniye kontrol; bağlı cihaz/görünür ve odaklı sekmede RAF. Blur/hidden/disable/dispose'da poll ve repeat durur.
- Takılı/held girdiler, bağlantı/index yeniden kullanımı, pointer/keyboard geçişi, ayar/tuş ataması ve tab'a dönüşte **neutral → yeni basış** gerekir. Analog deadzone `.2–.8`, default `.35`; direction için hysteresis vardır. Confirm/Back/Menu held tekrar etmez; direction/scroll ve seek repeat bounded'dır. Bir örnekte yalnız bir controller niyeti alınır; ikinci cihaz held repeat'i sahipliği ele geçirmez.
- Standart olmayan gamepad eksen/tuşları tahmin edilmez. En az yön/seç/geri eşlemesi açık atanır; cihaz başına yerel atama, 15sn timeout/cancel, capped controller/button sayısı. Tuş atama frame'i arka plandaki sayfa komutunu üretmez. Mapping ve deadzone sadece bu tarayıcı/base path'te saklanır; raw cihaz bilgisi sunucuya gönderilmez.
- Popup/modal/player scope ve kayıt sırası/derinliği açıkça ayrılır. Select/navigation popup handler'ları Home-owned seçim komutunu doğrudan çağırır. Video.js popup'ı transport kısayollarını tüketir; Slider için mevcut input/change/key handling kullanılır. Hiçbir sentetik klavye olayının trusted user activation ürettiği varsayılmaz.
- Video.js controller transportu mevcut **HomeMediaPort** üzerinden çalışır; pending/ready, absolute timeline ve room `canControl` kontrol edilir. Gapless/native Live TV/podcast/offline kontrolü kendi mevcut renderer/transportuna bağlanır. Ayrı playback/acquisition/lease/auth yolu açılmaz. Live seek mevcut pencereye, movie/podcast/offline seek sonlu zaman aralığına sınırlandırılır.
- Ekran klavyesi yalnız alanı seçip onaylayınca açılır. English/Türkçe, rakam/sembol/uppercase/space/delete/newline; native setter + input/change ile mevcut controlled form handler'ı çalışır. Apply dışında değişiklik yapılmaz; Back/cancel eski alanı korur. Password preview masked, draft sadece bileşen belleğinde; localStorage/network/log'a yazılmaz. Profil/oturum değişince hedef/draft ve focus geçmişi iptal edilir. Form gönderimi ayrı kullanıcı onayıdır; mevcut validation/CSRF/role sınırları aynıdır.
- Kalıcı episode mapping `multiple` listesi controller edit modunda explicit toggle kullanır; Back önce edit modunu kapatır. Mouse/native klavye seçimi korunur.
- Gelecekte ayrı bir oyuncu yüzeyi `register(..., exclusive=true)` ile Home gezinmesini tüketebilir. Bu bir oyun API'si/emulator/streaming desteği değildir; oyun katmanı ileride kendi raw girdi ve UI'sini kurar.

## Gerçek cihaz sınırları

Bluetooth/USB eşleştirmesi cihaz işletim sistemine aittir. Gamepad API için güvenilir HTTPS/secure context ve tarayıcı permissions policy gerekir; LAN IP'de HTTP gamepad varsayılmaz. Remote keyboard olayları HTTP'de çalışabilir. Browser/model/controller mapping gerçek cihazla doğrulanmalıdır. Browser'ın ilk ses/oynatma, fullscreen, PiP, dosya/klasör seçimi ve native media-controls shadow UI izin/kısıtları gamepad polling veya `.click()` ile aşılamaz; gerekirse gerçek TV kumandası/klavye etkileşimi kullanılır. Her native sistem ekranının gamepad-only çalıştığı iddia edilmez.

## Doğrulama ve durum

Saf reader, scoped DOM/event contract fixture, locale ve mevcut Video.js/room/offline/frontend kontrolleri çalıştırılır. `tests/helpers/controller-dom.mjs` izole unit fixture'dır; **gerçek browser, React Aria render kabulü veya TV/gamepad donanım testi değildir**. Kullanıcı browser/device kabulünü yapar; otomatik browser suite ve internet medya edinmesi yoktur. Final build/imzalı önizleme/HTTP/veri korunumu aşağıya eklenir. VERIFY-27 ve gerçek cihaz kapıları açık kalır.

Kaynaklar **2026-10-18**: Context7 [MDN Gamepad API](https://developer.mozilla.org/en-US/docs/Web/API/Gamepad_API), [user activation](https://developer.mozilla.org/en-US/docs/Web/Security/Defenses/User_activation), [React external stores](https://react.dev/reference/react/useSyncExternalStore), [HeroUI v3 Select](https://heroui.com/en/docs/react/components/select) ve kurulu HeroUI/React Aria types/usePress okunmuştur. Yeni dependency veya dataset eklenmez; mevcut [lisans envanteri](dependencies.md) geçerlidir.

## Final derleme — 2026-10-18

**79/79 Node, 0 atlanan**, bunun **33'ü** yeni input/state machine/scoped DOM contract kontrolüdür. Diğer kontroller önceki gerçek Video.js store/port, room/offline, navigation/genre ve locale davranışıdır. Kanıt `artifacts/controller-v030.tap`. Fake DOM/hardware örnekleri izole unit testtir; gerçek TV/gamepad/browser kabulü değildir.

Frontend/worker tip kontrolü, Vite production build ve .NET publish başarılı; npm audit **0 açık**. Bu UI diliminde backend business logic/migration değişmez; geniş backend regresyonu çalıştırılmadı. Son tam regresyonlu sürüm **0.26.0**, VERIFY-27 açıktır.

İstemci `/assets/index-BEvmhRU7.js`, player `/assets/Player-quZfxFgJ.js`, CSS `/assets/index-Bt4ZbPXh.css`, PWA **216a412a329ec2b2**. İmzalı Windows paket `artifacts/packages/preview-v030/home-0.30.0-dev.1-win-x64.zip`: **837 doğrulanmış dosya / 74.884.116 byte**, SHA-256 **fb3baf0cba0dea1ab6def4bb3976603c469a762fb6364327d8b36298e339dd21**. Güvenilen imza kimliği değişmedi. Canlı yayın/HTTP/veri korunumu aşağıya eklenir.

## Çalışan önizleme

**2026-10-18 21:56 yerel saat**: imzalı **0.29.1 → 0.30.0** tamamlandı; sahip oturumu korundu. **http://localhost:5080**, PID **3468**, release `c2498a130a6248fe8d383f456b7a9b7a`. Canlı HTTP'de yeni controller/keyboard kodu, ortak seçim renderer'ı ve CSS sunumu doğrulandı. Bu bir DOM/device acceptance değildir.

Şema **39**, **0 FK ihlali**; güncelleme öncesi medya/istek/indirme/kişisel kayıtlar korundu. Son salt-okunur sayım **7 medya / 2 istek / 2 indirme / 5 kişisel kayıt**, metadata dili **tr-TR**. YTS transferi kullanıcının paused durumunda kaldı. Doğrulama **0 oynatma / 0 edinme** komutu verdi.

Kanıt `artifacts/preview-before-v030.json`, `artifacts/update-live-0.30.0-preview.json`, `artifacts/live-controls-v030.json`. Rollback **0.29.1**, yedek `47ed337429d1454281c2043dd229cc52`; önceki release/yedekler korundu. **Ctrl+F5**, ardından Ayarlar → **Gamepad ve kumanda** ile gerçek cihaz denemesi yapılır. Gaming Mode/oyun ve gerçek bütün-TV uyumluluğu tamamlanmış ilan edilmez.
