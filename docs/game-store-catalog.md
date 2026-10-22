# İlk oyun mağazası — beş tarayıcı oyunu

Tarih **2026-10-19**, kaynak hedefi **0.33.0-dev.1**, SQLite **40** (yeni migration yok). Gereksinimler **GAM-07/08/09**, **GAM-02/03**, **CLI-01/05**, **OPS-01/04/09**; kart **GAME-STORE-01**.

Kullanıcı mağaza/katalog modelini onayladı; ana dağıtım sunucusu istemedi ve ilk beş oyunu seçmemizi, gerçekten bilinen/oynanan oyunlara yönelmemizi istedi. Emülasyon ve Steam beklemede. Mevcut Tiny Crate Classic/Wrap It Up kayıtları veya payload'ları kaldırılmaz.

## Seçki ve gerçek kaynaklar

| Oyun | Neden / kaynak | Sabit source commit | Lisans |
|---|---|---|---|
| **2048** | Gabriele Cirulli'nin bilinen browser klasiği; https://play2048.co/ ve official GitHub | `478b6ec346e3787f589e4af751378d06ded4cbbc` | MIT |
| **A Dark Room** | doublespeak games minimalist macera; official live browser oyunu ve source | `1fada4620b6c66bd07bf15a3f1eb8223df8bc1d7` | MPL-2.0 |
| **HexGL** | BKcore'un 2012 WebGL yarış oyunu; source README static local server ile oynatmayı tarif ediyor | `6addc95a2fce3bf05f4d751823cc054c61a16d68` | MIT, dosya bazlı notices korunur |
| **Hextris** | 2014'ten beri yayımlanan renk/altıgen arcade puzzle; official GitHub Pages | `3f4847dc8fd7dab3d1c87e6324b9159d92fbd396` | GPL-3.0-or-later |
| **0h h1 Classic** | Q42/Martin Kool Takuzu oyununun açık kaynak 2014 sürümü; original live game https://0hh1.com/ | `25910e580d6ea6bc1dfbfd29b2637e47844a96f9`, `florisluiten/0hh1` preserved MIT fork | MIT; Apache-2.0 index.js notice de korunur |

0h h1'in eski Q42 GitHub adresi artık 404; eski açık lisanslı kaynak fork'u kullanılır. Bu, güncel original uygulama sürümü veya Q42 entegrasyonu gibi sunulmaz; **Classic** etiketi açık.

2026-10-19 GitHub snapshot'ları: 2048 **13.442**, A Dark Room **8.340**, HexGL **1.745**, Hextris **2.443** proje yıldızı. Bunlar **oyuncu/indirme sayısı değildir** ve UI'de active-player veya download ranking olarak gösterilmez. 0h h1 seçim dayanağı original live browser/mobile oyunu; preserved fork yıldızı popülerlik metriği sayılmaz. Katalog seçilmiş browser classics'tir, canlı popülerlik algoritması değil.

Friday Night Funkin'in official sayfası code Apache-2.0, assets **All Rights Reserved** diyor; 12.423 rating veya büyük kullanıcı ilgisi lisans hakkı sağlamaz. Kataloğa alınmadı. Celeste commercial kaynak örneklerinin MIT lisansı oyunun kendisini/assets'ı kapsamaz; alınmadı.

## Catalog ve kurulum ayrı

`Gaming/Store/catalog.json` yalnız gerçek metadata, source provenance, ordered scripts, pinned Git blob kimlikleri ve küçük original icon artwork içerir. Oyun source arşivleri ve tüm payload **Home ZIP'e preinstall olarak eklenmez**. `tools/create-game-store-catalog.mjs` yalnız metadata/code entry/licence ve küçük artwork'i inceler; oyun arşivi indirmez veya kurulum komutu çalıştırmaz.

- **Mağaza:** bu beş entry; kurulu değil/kuruluyor/kurulu durumu.
- **Kütüphanem:** mevcut iki başlangıç oyunu ve kullanıcının kurdukları; eski favorite/save kayıtları korunur.
- **İndir ve kur:** authenticated + CSRF + expected account/login ve edinme yetkisiyle açık kullanıcı komutu. Sayfayı gezmek, kart/detail açmak transfer başlatmaz.
- Kurulum kalıcı `game-install` product job; bounded tek worker lane, cancel ve restart recovery mevcut iş motoruna bağlı.
- Payload geliştiricinin/preserved open-source repository'nin **codeload.github.com/{repo}/zip/{commit}** adresinden kullanıcının Home sunucusuna gelir. Home ana sunucusu veya yayımlanmış Home release mirror'ı yok; public GitHub upload/push yapılmadı.

## Paket güvenliği / doğrulama türü

- HTTP yalnız sabit HTTPS codeload host'u; redirects/cookies/proxy kapalı. DNS bağlanırken private/loopback/link-local adresler reddedilir. URI kullanıcıdan alınmaz. Header streaming, 128-MB archive/256-MB reviewed extracted upper bounds, timeout ve cancellation açık.
- ZIP root adı exact source commit ile eşlenir. Traversal, symlink ve duplicate paths reddedilir. Yalnız catalog-pinned static web/source entries işlenir; EXE veya installation scripts çalıştırılmaz.
- Her kaynak entry **Git blob SHA-1 identity** (blob-length prefix dahil) ile pinned tree'ye karşı kontrol edilir; bu publisher signature veya trusted SHA-256 archive manifest'i diye sunulmaz. Kurulan dosyalar ayrıca SHA-256 manifesti alır ve runtime stream'de tekrar doğrulanır.
- Managed staging → same-volume atomic directory publish. Var olan farklı target korunur; overwrite/source-library deletion yok. Başarısız kaynak kurulu işaretlenmez; staging data kalır.
- Oyun hazır static web source'tur; kullanıcının server'ında compiler/Godot export/native oyun process'i çalıştırılmaz.

## Browser runtime

HTML script girişleri bilinen sırayla profile initialization'dan sonra yüklenir. External analytics/ad scripts giriş HTML'inden çıkarılır; runtime CSP dış ağları da kapatır. Required local libraries source tree'de vardır. Current 2048'in official-site iddiası Home adapted web edition olarak düzeltilir; author credit ve licences kalır.

- **Opaque iframe + CSP sandbox** korunur; allow-same-origin yok. Local cookie/storage API'leri real Home cookie/API erişimi değil, **yalnız profile-backed compatibility store** ile sağlanır. Raw host cookies veya browser localStorage okunmaz.
- `web-profile.json` mevcut bounded/CAS save pipeline'ıyla user+game+source-build kapsamında saklanır. Bu emulator save state değildir. Büyük/uyumsuz save veya source failure UI'de açıklanır; ilerleme kaybı sessizce gizlenmez.
- Gameplay input Home navigation'dan ayrıdır. 2048/Hextris dijital yön, 0h h1/ADRoom DOM spatial controls, HexGL original native browser gamepad yolu kullanır. Per-game physical mapping henüz user/device kabulündedir; generic keyboard-to-gamepad dönüşümü her oyunda başarılı diye sunulmaz.
- Game scheduling pause gate ve virtual clock yalnız game frame'indedir; Home timers/diğer medya etkilenmez. Original game systems/native OS izinleri bypass edilmez.
- System fonts kullanılır; unreviewed font binaries indirme/install manifestinde seçilmez. SVG/icon/social art only original source assets; fake gameplay screenshot oluşturulmadı.
- ADarkRoom original `ignorebrowser=true` opt-in parametresi Home player içinde kullanılır; bunun olması actual TV performance acceptance değildir.

## Lisans sınırları

Source licences/embedded notices kurulu pakette korunur; modified source HTML/CSS ve adapter plaintext olarak sunulur. Hextris GPL-3.0-or-later, A Dark Room MPL-2.0 programları kaynak provider'dan kullanıcı komutuyla gelir; kurulu source files ve corresponding source URL tutulur. Public Home/adapter/game distribution license kararı **henüz açık**; private local prototype doğrulaması public/store licensing clearance değildir. Hiçbir proprietary oyun/assets veya ROM içeriği alınmadı.

Kaynaklar: official GitHub repos/commit trees, original developer pages, https://ninja-muffin24.itch.io/funkin?download ; https://github.com/NoelFB/Celeste README. Installed ASP.NET/.NET types ve Context7 HTTP streaming/ConnectCallback/cookie/redirect seçenekleri incelendi.

## Doğrulama / açık kabul

İlk focused kontrol **36/36 backend**, **108/108 Node**, 0 atlanan. Fixture-based source transport, ZIP path/hash, explicit queue/idempotence, publish conflict, cancellation, installed catalog restart ve viewer/unrated izinleri test edilir. Gerçek registry metadata/scripts/artwork/licence SHA-256 envanteri Node ile kontrol edilir.

**Geliştirme doğrulamasında gerçek oyun arşivi/download/install başlatılmadı.** Network acquisition ve beş oyunun gerçek cihazdaki gameplay/kurulum kabulü kullanıcı komutudur. Browser suite ve screenshot polish yapılmaz. İlk iki installed Godot oyununun önceki kabulü, yeni beş static web oyununun kabulü sayılmaz.

Şema 40 korunur; son full regression 0.26.0, VERIFY-27 ve native TV/ROM/streaming kapıları açık kalır.

## Canlı yayın — 0.33.0

- **36 backend / 108 Node, 0 atlanan**; frontend/worker build ve .NET publish başarılı. Kanıt: `artifacts/test-results/game-store-boundaries-v033.trx`, `artifacts/game-store-v033.tap`, `artifacts/game-store-build-v033.log`.
- Windows signed ZIP **892 dosya / 115.822.882 byte**, `artifacts/packages/preview-v033/home-0.33.0-dev.1-win-x64.zip`; SHA-256 **63f40c16dcf1e8523a0e4162fc4970c31919a61c1dc90c8b3b82fd59e5d1a86f**. Mevcut trusted key ile executable package verify geçti.
- **http://localhost:5080/games**, runtime **0.33.0**, schema **40**, PID **19356**, release `9604a3ebd6d84b6eae4cb0ea017fd2cb`. PWA **dc5b363a28cdec6b**, main client `/assets/index-DS-bKVQy.js`.
- Pre-update backup **0a7cc8980406401badd1fe01fb7920c9**; apply backup **87307aa037ac434eb80a972052afd68a**, rollback **0.32.1**. Owner session retained; `artifacts/update-live-v033.json`.
- Canlı GET-only kontrol: **5 store entry, hepsi installed=false**, mevcut iki Godot oyunu installed=true/inStore=false; gerçek PNG artwork ve source commit metadata sunuluyor. **7 media / 2 request / 3 download / 5 personal / 2 game state**, en-US metadata ve **0 FK violation**; `artifacts/live-games-v033.json`.
- Hiçbir yeni store download/install/gameplay komutu doğrulama tarafından başlatılmadı. Üç transfer paused/archived durumunda korundu; resume/edinme komutu verilmedi. Mevcut Home owner adı/parolası ve UI tercihleri değişmedi.

Bu yayın catalog + explicit install workflow'dur. Yeni beş oyunun gerçek provider acquisition/payload/browser/device kabulü, kullanıcının **Download and install** komutundan sonra yapılır; bunların tümünün Home'da oynanışı doğrulanmış gibi sunulmaz.

## Sonraki kullanıcı yönlendirmesi — 2026-10-19

Kullanıcı web oyun mağazasının asıl hedefini karşılamadığını belirtti; hedef kendi ROM'larını emülatörle, zayıf TV'ye emülasyon yükü bindirmeden Home sunucusunda çalıştırmak yönünde. Mağaza genişletmesi beklemede. Mevcut oyunlar/kayıtlar silinmedi; bu geri bildirim native runner/streaming implementation yetkisi veya seçilmiş dependency kararı değildir. Host emulation + düşük gecikmeli Home istemcisi için dar prototip ve gerçek TV decoder/input kabulü önce değerlendirilmelidir.
