# Home içindeki tarayıcı oyunları — adaylar ve ürün akışı

Tarih: **2026-10-19**. Durum: **araştırma ve tasarım önerisi; oyun paketi, katalog entegrasyonu veya çalıştırıcı uygulanmadı**. [Yürütme araştırmasının](gaming-execution-research.md) devamıdır. Steam entegrasyonu ve ROM edinmesi kapsam dışında kalır.

Kullanıcı oyun merkezli giriş önerisini olumlu karşıladı: ana ekran oyunları gösterir; yürütme yöntemi gerektiğinde detay/oynatma seviyesinde seçilir. Aşağıdaki oyun listesi, paket biçimi ve güvenlik tasarımı henüz onaylanmış uygulama kararı değildir. Ürün kabul panosunda tamamlanmış yeni dilim yoktur.

## Tarayıcı oyunu ne demek?

HTML/JavaScript/WebGL/WASM ve varlıklarından oluşan, browser için hazırlanmış bir oyun build'i. Bazı oyunlar doğrudan JavaScript, bazıları bir motorun web export'u olabilir. ROM veya konsol emulator'ı şart değildir. Tarayıcı oyunu olması düşük donanım gereksinimini, gamepad'i, offline kullanımını veya Home profil kayıt eşitlemesini otomatik sağlamaz.

## Somut adaylar — ürünün mevcut oyunları değildir

Upstream sayfa/lisans incelemesi yapılmıştır; oyunlar Home'da veya gerçek TV'de çalıştırılmadı. Geliştiricinin gamepad beyanı, Home cihaz kabulü değildir.

| Oyun | Oynanış / yayınlanmış girdi bilgisi | İlk lisans bulgusu / benimseme sınırı |
|---|---|---|
| **Tiny Crate Classic** | Kasa taşıma/atma, bulmaca-platform; geliştirici sayfası HTML5 ve Xbox/gamepad bildiriyor | `tiny_crate` deposunda Unlicense. Itch sayfası Classic sürümü ayırıyor; yeni Steam sürümüyle karıştırılmamalı. Web build, varlık ve engine notices ayrıca kontrol edilmeli |
| **ROTA: Bend Gravity** | Yerçekimi değiştirme/blok bulmacaları; HTML5, gamepad, yapılandırılabilir kontroller | Güncel `LICENSE` MIT metni; README hâlâ Unlicense diyor ve `addons/` klasörünü hariç tutuyor. Lisans çelişkisi ve web build'e giren addon/varlıklar çözülmeden paket onayı yok |
| **Wrap It Up** | Kısa bölümlü arcade platform; HTML5, Xbox/gamepad beyanı | Güncel LICENSE ve README MIT. Tam varlık/engine notice denetimi ve gamepad-only menü/çıkış kabulü açık |
| **HexGL** | WebGL yarış; source'ta GamepadController ve oyun içinde analog/tuş kullanımı var | MIT; README dosya bazlı istisnaları saklı tutuyor. Eski upstream, sabit controller slotu ve basit mapping; modern browser/menü/gamepad yaşam döngüsü uyarlaması gerekebilir. Ağır 3D'nin zayıf TV'de çalışması garanti değil |

İlk değerlendirme için küçük 2D seti daha uygun; HexGL ikinci aşama adayı. Bu bir öncelik önerisidir, gönderilmiş oyun paketi listesi değildir.

**Falling Lightblocks** olumlu controller/TV örneği fakat hazır dağıtılacak oyun olarak seçilmedi: upstream README, kaynak kodunu Apache-2.0 yaparken `android/assets/` varlıklarını yalnız kişisel kullanım için lisanslıyor ve yeniden dağıtımı yasaklıyor. İzin veya ayrı uygun varlık seti olmadan Home'a orijinal oyun paketi olarak eklenmez. “Ücretsiz/açık kaynak” etiketi tek başına tam oyun dağıtım hakkı değildir.

## Önerilen ekleme yolları

1. **Home için seçilmiş gerçek oyunlar:** Lisansları ve build'leri denetlenen küçük liste; kullanıcı Ekle/Oyna der. Onaylı build Home'dan sunulur; kullanıcının ayrı siteye/account'a gitmesi veya derleme yapması hedef değil. Otomatik internet edinmesi bu araştırmada başlatılmaz.
2. **Kullanıcının kendi uyumlu web paketi:** Yalnız yöneticinin açık ekleme komutuyla; giriş HTML'i, JS/WASM/engine data dosyaları, görsel/ses varlıkları ve Home tanımlayıcı bilgileri içeren sınırlandırılmış ZIP/dizin. Rastgele her itch oyunu/site linki veya EXE kabul edilmiş sayılmaz; desteklenen paket sözleşmesi henüz tasarlanmadı.
3. **Dış sağlayıcı embed'i:** İlk yol olarak önerilmez. Sağlayıcının embed/dağıtım izni, frame politikaları, ağ/reklam/hesap gereksinimleri ve gamepad davranışı ayrıca doğrulanmadan “Home içinde çalışır” denmez. Oyun sayfası iframe'i ile doğrudan yayıncı kontrolündeki embed farklı olabilir.

Paketin kayıt bilgisi: gerçek oyun/ad/sürüm/yayıncı, giriş dosyası, kaynak/lisans, kontrol desteği ve ihtiyaç duyulan browser özellikleri. Bu kayıt bir imzalı veya kullanıcı tarafından doğrulanmış paket kimliğine bağlanmalı; oyunun kendi “gamepad var” iddiası TV verified rozeti üretmemeli.

## Girdi ve güvenlik sınırları

- İlk TV kataloğunda hedef yalnız gameplay değil **başlatma → menü → oyun → pause → çıkış** akışının gamepad ile tamamlanabilmesi. Klavye-only oyunlar bütün TV kataloğunda uygunmuş gibi sunulmaz.
- Gamepad API için güvenilir secure context ve browser/OS/controller desteği gerekir. Frame içi oyuna explicit permissions policy/delegation gerekir; yalnız iframe eklemek girdi sağlamaz.
- Oyunun native gamepad'i veya Home'a özgü küçük adapter kullanılır. Kaynak kod erişimi olan uygun oyunlarda adaptasyon mümkün; bütün kapalı oyunlara gamepad→sentetik klavye ile eksiksiz destek sözü yok. Analog, mouse, trusted user activation ve sistem ekranları bu yöntemle genel olarak çözülemez.
- Home gezinmesi ve gameplay exclusive sahiplikte ayrılır. Pause overlay'e geçerken, controller/tab/network kaybında held girdiler neutral olmalı; A/B oyun oynarken katalogda işlem yapmamalı. Ayrı origin/frame girdi sahipliği gerçek cihazda ayrıca incelenmeli.
- **Kullanıcı yüklediği HTML/JS aktif kod** sayılır. Uygulama origin'inde Home cookies/API/storage erişimiyle çalıştırılmaz. Ayrı izole game origin'i, asgari frame izinleri, CSP, network sınırı ve exact origin/session kontrolüyle dar `postMessage` köprüsü değerlendirilir. “Sandbox var” tek başına auth/API erişimine karşı tam güvenlik kanıtı değildir.
- ZIP path traversal, symlink, boyut/dosya/adet sınırları ve çakışma davranışı açık olmalı; kaynak ROM/medya klasörlerine yazılmaz. Kayıtlar yalnız profile/game/version kapsamındaki yönetilen alana yazılabilir.
- Offline ve profil bazlı save sync yalnız çalışan adapter/engine yolu varsa gösterilir. Browser storage'ın varlığı otomatik cloud/profile sync değildir. Oyun UI metni third-party içeriktir; Home başlıkları/menüleri English/Türkçe kalır, oyun dil desteği ayrı dürüst gösterilir.

## Kütüphane sunumu önerisi

**Tek Oyunlar sayfası ve ortak kart/detay modeli**; web oyunları ve ROM oyunları ayrı ana menü ürünlerine bölünmez. Devam et, hızlı oyunlar, favoriler ve kütüphane rafları farklı yürütme türlerini birlikte içerebilir. Filtreler gerektiğinde Tarayıcı oyunları, Konsol oyunları, platform, gamepad ve mevcut cihazda oynanabilirlik üzerinden daraltır. Genre (platform/bulmaca/yarış), teknik kaynak/yürütme türüyle karıştırılmaz.

Detayda Oyna ana eylemi ve uygun yürütme bilgisi; birden çok gerçek seçenek varsa Bu cihazda / Home sunucusunda menüsü. Browser oyunu için EmulatorJS açılmaz. Web build'in sunucuda render edilip streaming yapılması teknik olarak düşünülebilir ama mevcut destek veya ilk kapsam kararı değildir.

## Kaynak ve snapshot kaydı

İnceleme tarihi **2026-10-19**; yalnız read-only HTTP/source sorguları. Oyun build'leri indirilmedi, yeni bağımlılık/ROM/native executable eklenmedi; runtime ve indirmelere komut verilmedi.

- Tiny Crate Classic: https://harmonymonroe.itch.io/tinycrate ; source `HarmonyHoney/tiny_crate`, `master2`, incelenen head **c9b835c46ac5a98cf2aaaa0ee824378af14ff003**; https://github.com/HarmonyHoney/tiny_crate/blob/c9b835c46ac5a98cf2aaaa0ee824378af14ff003/UNLICENSE
- ROTA: https://harmonymonroe.itch.io/rota ; source `HarmonyHoney/ROTA`, `master4`, head **ce872d57fbbab01317da2e64cb6dd505bc4a3abd**; https://github.com/HarmonyHoney/ROTA/blob/ce872d57fbbab01317da2e64cb6dd505bc4a3abd/LICENSE ; README Unlicense/addons istisnası ayrıca okundu.
- Wrap It Up: https://harmonymonroe.itch.io/wrap-it-up ; source `HarmonyHoney/wrap_it_up`, `master`, head **ac8bb8d0586e49cfd7a1b0039ce540b6fdda3f1a**; https://github.com/HarmonyHoney/wrap_it_up/blob/ac8bb8d0586e49cfd7a1b0039ce540b6fdda3f1a/LICENSE
- HexGL: https://github.com/BKcore/HexGL ; `master`, head **6addc95a2fce3bf05f4d751823cc054c61a16d68**; LICENSE ve `bkcore.coffee/controllers/GamepadController.js` pinned head üzerinden okundu; `bkcore/hexgl/ShipControls.js` gamepad kullanımına da bakıldı.
- LG HexGL örneği: https://github.com/webOS-TV-app-samples/HexGL ; README webOS24+ ve Magic Remote modlarını anlatıyor. Örneğin varlığı bütün TV/gamepad'lerde Home kabulü değildir.
- Falling Lightblocks: https://github.com/MrStahlfelge/lightblocks/blob/master/README.md ; controller beyanı https://mrstahlfelge.itch.io/lightblocks
- Context7 MDN Gamepad/Permissions Policy: https://developer.mozilla.org/en-US/docs/Web/API/Gamepad_API ve https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Permissions-Policy/gamepad

**Açık kapılar:** tam oyun/varlık/transitif engine lisansları, seçilen build, Home input/save adapter'ı, auth isolation, secure-context dağıtımı, gerçek TV/controller performansı. Bunlar uygulanmış veya doğrulanmış ilan edilmez.
