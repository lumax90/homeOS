# Özellik kapsamı ve karşılıklar

Kaynak kodları [sources.md](sources.md) içindedir. Başlangıç durumu 0.2 incelemesidir; tamamlanan dilimler [execution.md](execution.md) üzerinde güncellenir.

## A. Gerçek katalog, dosyalar ve metadata

| ID | Gereksinim ve kabul ölçütü | Karşılık / kaynak | Başlangıç |
|---|---|---|---|
| CAT-01 | Film, dizi, sezon, bölüm, sanatçı, albüm, parça, dosya ve sürüm ayrı kimlikler; metadata olmadan da dosya kaybolmaz | Plex/Jellyfin/Lidarr R01,R02,R05 | Kısmi |
| CAT-02 | Dosya bulunmadan gerçek içeriği keşfetme; trend/popüler/yaklaşan listeler, arama, film/dizi tür filtresi, sayfalama, kaynak atfı | Overseerr/Seerr/Plex R01,R08,R09,R35 | 0.3 temel + 0.27 tür keşfi uygulandı; ACCEPT-27-GENRES açık |
| CAT-03 | Birden fazla klasör, gerçek dosya taraması, izleme, artımlı tarama, offline kök, dışlanan klasörler ve manuel yenileme | R02,R03,R04 | Kısmi |
| CAT-04 | Sağlayıcı kimlikleri ve alias'lar; aynı isimli farklı yapımlar ayrılır; çelişkili kimlikler otomatik birleşmez | R02,R09,R10 | Kısmi |
| CAT-05 | Manuel/otomatik eşleştirme, yanlış eşleşmeyi düzeltme, kilitli alanlar ve yeniden taramada korunma | R02,R03,R04 | Kısmi |
| CAT-06 | Film sürümleri, yönetmen/sinema kesimi, çok parçalı film ve dosya seçimi | R01,R02,R03 | Kısmi |
| CAT-07 | Çok bölümlü dosya, özel bölümler, günlük yayın ve anime mutlak numarası; numaralandırma kaynağı açık | R02,R04 | Planlandı |
| CAT-08 | Koleksiyonlar, çalma listeleri, etiketler, favoriler, puanlar; kişi/oyuncu/yönetmen gezinmesi | R01,R02,R03 | Planlandı |
| CAT-09 | Yerel NFO ve afişler, gömülü etiketler, metadata önceliği, isteğe bağlı dışa aktarma | R02,R03,R05 | Planlandı |
| CAT-10 | Ekstralar, fragmanlar, tema medyası, chapter bilgisi ve küçük resimler | R01,R02 | Kısmi |
| CAT-11 | Gerçek erişilebilirlik: eksik, bekleniyor, hazırlanıyor, oynatılabilir ve sorunlu farklı durumlar | R01,R08 | Kısmi |
| CAT-12 | Demo kütüphaneleri normal üründen çıkarılır; boş durumda gerçek bağlantı kurulumu gösterilir | Kullanıcı kararı | Uygulanıyor |

**0.3 uygulama kanıtı:** CAT-12 migration/canlı katalog ile doğrulandı. CAT-02 gerçek TMDB/TVmaze keşfi; REQ-01/02 sahip istekleri; MON-01/02 temel takip/takvim; IDX-01/02/03/05 standart kaynak API'si; QUA-01/02 temel kurallar; ACQ-01 + IMP-02/03 kimliğe bağlı manuel edinme uygulanmıştır. Satırların tüm kapsamı ve kabul kapıları [çalışma panosunda](execution.md), teknik sınırlar [0.3 belgesinde](../milestone-03.md) izlenir. Diğer başlangıç durumları bu tarihsel matriste korunur.

**0.4.1 uygulama kanıtı:** CAT-10 chapter'lar; SUB-01/05/06 iz envanteri, yerel/gömülü metin, yükleme ve WebVTT; PLY-02/03/06/07 direct/HLS/SDR/ses kararı ve oynatıcı kontrolleri; IMP-03 açık kullanıcı import profili incelemesi eklendi. Bu alt akışlar native/backend ile doğrulandı. Altyazı sağlayıcısı, resim altyazısı/HDR/GPU, cihaz kabulü ve ilgili satırların gelişmiş koşulları açık kalır: [0.4 kanıtı](../milestone-04.md).

**0.17 katalog uygulama kanıtı:** CAT-04/05 ortak IMDb/TVDB ile revizyonlu kanonik birleştirme, eski ID çözümü, korunan kişisel veri ve yerel/sağlayıcı adları; CAT-06 sıralı film parçası ve tek film konumu; CAT-07 çok bölüm üyeliği, açık zaman aralıkları, günlük tarih ve açık mutlak numaralandırma eklendi. CAT-11/MON-01/02/ACQ-02 hesapları bölüm üyeliğini ve tüm film parçalarını kullanır. **185/185 backend/native, 5/5 ses zamanlayıcı testi**, gerçek FFmpeg çoklu import/tekrar ve şema 19→20 update/rollback geçti. Kullanıcı cihaz/katalog kabulü, canlı TMDB mutlak grup importu ve ileri eşleme durumları ACCEPT-17/CATALOG-04 üzerinde açık: [0.17](../milestone-17.md).

## B. Kaynak/indexer yönetimi

| ID | Gereksinim ve kabul ölçütü | Karşılık / kaynak | Başlangıç |
|---|---|---|---|
| IDX-01 | Torznab/Newznab bağlantısı, kategori ve arama yeteneklerinin `caps` ile doğrulanması | Prowlarr R06,R11,R12 | Planlandı |
| IDX-02 | Kimlik, API/RSS anahtarı, cookie/oturum bilgisi; sırların korumalı saklanması ve maskeli yanıt | R06 | Planlandı |
| IDX-03 | Film/dizi/bölüm/müzik/kitap sorguları; desteklenen dış kimlikler, q fallback, sayfalama | R11,R12 | Planlandı |
| IDX-04 | RSS/scheduled sorgu; release GUID/infohash tekrarlarının ayıklanması ve provenance | R03,R04,R06 | Planlandı |
| IDX-05 | Query/grab kotası, Retry-After, backoff, timeout, sağlık ve son hata; bir kaynak bozulunca diğerleri çalışır | R06,R11,R12 | Planlandı |
| IDX-06 | Kaynak önceliği, kategori eşlemeleri, etiket, min seeders, freeleech ve seed koşulları | R06,R11 | Planlandı |
| IDX-07 | Site listesinden rehberli bağlantı; standart dışı doğrudan sağlayıcı adaptörleri, sürümlü yetenek/tanım kaydı ve yetkili kullanıcı oturumu | R06,R36 | 0.27 SOURCE-02: 12 hazır profil + SDK; hesap/aktarım kabulü açık |
| IDX-08 | HTTP/SOCKS proxy ayarları, TLS ve kullanıcı doğrulama gereksinimlerinin görünür olması | R06 | Planlandı |
| IDX-09 | Sağlayıcı arama/grab geçmişi, kullanım limiti, erişim süresi ve bozuk sonuç teşhisi | R06 | Planlandı |
| IDX-10 | Dış *arr uygulamalarına ayar kopyalama gereksiniminin ortak iç bağlantı/komut modeliyle karşılanması | R06 | Mimari olarak karşılanır |

## C. İstek, takip ve edinme kararı

| ID | Gereksinim ve kabul ölçütü | Karşılık / kaynak | Başlangıç |
|---|---|---|---|
| REQ-01 | Kullanıcı, dosya olmadan film/dizi/sezon/bölüm isteyebilir; yinelenen aktif istek birleştirilir | Seerr R08 | Planlandı |
| REQ-02 | İstek sahibi, durum, onay/ret/iptal, not ve geçmiş; kullanıcı rolüyle kontrollü komutlar | R08 | Planlandı |
| REQ-03 | Otomatik onay tercihi, kullanıcı/medya kotaları, yasaklanan içerik/istekler | R08 | Planlandı |
| REQ-04 | İzleme listesi, takip listeleri ve koleksiyon/kişi/listeden ekleme; istisnalar korunur | R01,R03,R05,R08 | Planlandı |
| MON-01 | Tüm/gelecek/eksik sezon-bölüm takip modları, takibi durdurma ve kaynak güncellemesi | Sonarr R04 | Planlandı |
| MON-02 | Yayın takvimi, yayınlanmış eksikler, saat dilimi/tarih hassasiyeti, dijital/fiziksel film uygunluğu | R03,R04 | Planlandı |
| MON-03 | Yeni albüm/sanatçı takibi, metadata profili ve release türleri | Lidarr R05 | Planlandı |
| QUA-01 | Release adı ayrıştırma: kimlik/yıl, sezon/bölüm/pack, kalite, kaynak, codec, HDR, ses/dil, grup | R03,R04,R05 | Planlandı |
| QUA-02 | Kalite profili ve cutoff; min/max boyut, dil, kaynak, seeders; reddin açıklanabilir nedeni | R03,R04 | Planlandı |
| QUA-03 | Custom-format koşulları, zorunlu/yasaklı/tercihli kelimeler ve süre sınırlı regex | R03,R04,R05 | Planlandı |
| QUA-04 | PROPER/REPACK ve sürüm sırası; daha iyi sürüm, aynı sürüm ve downgrade ayrılır | R03,R04 | Planlandı |
| QUA-05 | Delay/minimum-age profili, protokol önceliği ve release tarih/retention kararı | R03,R04,R05 | Planlandı |
| QUA-06 | Yanlış release, başarısız indirme ve bozuk dosya blocklist'i; yeniden arama geçmişi | R03,R04,R05 | Planlandı |
| ACQ-01 | Arama → aday değerlendirme → kullanıcı seçimi → indirme komutu; provenance ve profil snapshot'ı saklanır | R03,R04,R08 | Planlandı |
| ACQ-02 | Kullanıcı etkinleştirdiğinde otomatik edinme/yükseltme; aynı içerik/kalite için çifte iş oluşmaz | R03,R04,R05 | Planlandı |

## D. İndirme, import ve depolama

| ID | Gereksinim ve kabul ölçütü | Karşılık / kaynak | Başlangıç |
|---|---|---|---|
| DL-01 | Dahili BitTorrent v1/v2, magnet metadata, parça hash doğrulama ve fast resume | MonoTorrent R13 | Backend doğrulandı |
| DL-01-A | Kamu kaynaklarında izleyicili magnet, YTS HTTPS metadata ve açıklamalı timeout | Kullanıcı, 2026-10-17 | 0.27.1 TRANSFER-03: 40 odaklı backend; kullanıcının mevcut YTS indirmesinde gerçek byte akışı doğrulandı |
| DL-02 | Kuyruk, öncelik, duraklat/devam/iptal, per-job/global hız, bağlantı ve kaynak sınırları | R13 | Kısmi |
| DL-03 | Seed ratio/time/pack-time, freeleech muhasebesi ve kontrollü seed sonrası temizleme | R06,R11,R13 | Planlandı |
| DL-04 | Dosya seçimi/önceliği, aynı torrentin farklı istekleri karşılaması, metadata/peer hataları | R13 | Kısmi |
| DL-05 | Usenet/NZB, NNTP TLS/hesaplar/bağlantılar, yEnc, parça eksikleri ve resume | R03,R04,R05,R12 | Planlandı |
| DL-06 | PAR2 doğrulama/onarım, arşiv açma, parola/eksik blok/CRC sonuçları | R03,R04,R05 | Planlandı |
| IMP-01 | Yönetilen staging, hardlink/copy, atomik yayın, kesinti sonrası idempotent import | R02,R03,R04 | Backend doğrulandı |
| IMP-02 | İstek/release/indirilen dosya kimliği bağlanır; salt dosya adına dayanarak yanlış yapım yayımlanmaz | R03,R04 | Planlandı |
| IMP-03 | Hash + medya probe + beklenen dil/kalite kontrolü; başarısız dosya karantinada, eski iyi sürüm korunur | R03,R04 | Kısmi |
| IMP-04 | Adlandırma şablonları, dizin/dosya çakışmaları, özel karakterler, çoklu disk, metadata yan dosyaları | R02,R03,R04,R05 | Planlandı |
| IMP-05 | Salt-okunur ve yönetilen kök ayrımı; kaynak izni/boş alan/çevrimdışı disk tanısı | R02,R03,R04 | Kısmi |
| IMP-06 | Geri dönüşüm kutusu, sürüm retention, kullanıcı kontrollü geçmiş/payload/cache temizleme | R03,R05 | Planlandı |
| IMP-07 | Kütüphane arşivleme/geri bağlama ve taşınan dosyayı kimliğiyle yeniden bulma | R02 | Planlandı |

## E. Oynatma ve istemci

| ID | Gereksinim ve kabul ölçütü | Karşılık / kaynak | Başlangıç |
|---|---|---|---|
| PLY-01 | Direct play, HTTP range, seek, pause/resume ve konum kaydı | Plex/Jellyfin R01,R02 | Kısmi |
| PLY-02 | İstemci codec/container/bitrate/dil/subtitle yetenekleri; direct/remux/audio-transcode/video-transcode kararı | R02 | Planlandı |
| PLY-03 | HLS/DASH segment yaşam döngüsü, seek/reconnect, iptal, oturum izolasyonu ve cache temizliği | R01,R02 | Planlandı |
| PLY-04 | QSV/VAAPI/NVENC ve desteklenen GPU profilleri; yazılıma fallback, kaynak/slot limiti | R01,R02 | Planlandı |
| PLY-05 | HDR/Dolby Vision bilgisi, tone mapping, renk ve altyazı yakma uyumluluğu | R01,R02 | Planlandı |
| PLY-06 | Ses/video/altyazı izi seçimi, kanal downmix, varsayılan dil ve ses seviyesi tercihi | R01,R02 | Planlandı |
| PLY-07 | Hız, bölüm/chapter gezinmesi, sonraki bölüm, kuyruk, intro/credits/recap atlama | R01,R02 | Kısmi |
| PLY-08 | Senkron izleme ve uzak oynatıcı kumandası | R01 | Planlandı |
| PLY-09 | Kullanıcıya özel izlenme, puan ve cihazlar arası devam; sürüm değişse de içerik konumu korunur | R01,R02 | Kısmi |
| CLI-01 | Web/PWA kurulum, responsive ve klavye erişimi, focus, reduced motion, gerçek hata/boş/yükleniyor durumları | Kullanıcı; R01,R02 | Kısmi |
| CLI-02 | PWA offline kabuk, medya indirme kuyruğu, yerel kota ve hesaplar arası izolasyon | R01 | Planlandı |
| CLI-03 | Cast/AirPlay, cihaz keşfi/DLNA ve Quick Connect benzeri kolay oturum bağlama | R01,R02 | Planlandı |
| CLI-04 | Android/Google TV, Apple TV/iOS, Tizen/webOS, Roku istemci ve mağaza dağıtımı | R01,R02 | Sonraki istemci hedefleri; web/PWA öncelikli |
| CLI-05 | English/Türkçe arayüz seçimi, kalıcı tarayıcı tercihi, tarih/sayı ve erişilebilir etiketler | Kullanıcı, 2026-10-17 | 0.27.1-dev LOCALIZE-01: uygulandı; odaklı Node/build doğrulandı, kullanıcı kabulü açık |
| CLI-01-A | Chromium HLS ipucu yerine doğrulanmış MSE/hls.js seçimi ve native-only fallback | Kullanıcı, 2026-10-18 | 0.27.2 PLAY-02: 8 Node/17 backend-native kontrolü; kullanıcıda sürekli oynatma kabulü açık |
| CLI-01-B | Adresli film/dizi/dosya ayrıntıları ve Video.js tabanlı izleme/miniplayer sayfası | Kullanıcı, 2026-10-18 | 0.28 PLAY-03/DETAIL-PAGE-01: uygulandı; 27 Node/26 backend-native + canlı HTTP/veri korunumu doğrulandı; kullanıcı kabulü açık |
| CLI-01-C | Üç birincil hedef, gruplu/izin filtreli Daha fazla menüsü ve dar ekran dock'u | Kullanıcı, 2026-10-18 | 0.28.2 NAV-01: 20 Node/build ve canlı HTTP/veri korunumu doğrulandı; kullanıcı kabulü açık |
| CLI-01-D | UI genre dili, raflı gerçek keşif, kişisel geçmiş/listeler ve responsive lazy cursor yükleme | Kullanıcı, 2026-10-18 | 0.29 DISC-04: 28 backend/39 Node/build ve canlı gerçek genre/hesap-sahipli raf/veri korunumu doğrulandı; kullanıcı kabulü açık |
| CLI-01-E | Bütün uygulamada ortak açılır seçim/menu görünümü, keyboard/focus ve locale/değer/disabled korunumu | Kullanıcı, 2026-10-18 | 0.29.1 UI-CONTROLS-01: 92 seçim/38 dosya, 46 Node/build, canlı ortak renderer/materyal ve veri korunumu doğrulandı; kullanıcı görsel/klavye kabulü açık |
| CLI-01-F | Mouse görünümünü koruyan gamepad/TV kumandası gezinmesi, scoped focus/Back, text input ve guarded player komutları | Kullanıcı, 2026-10-18 | 0.30 INPUT-01: 79 Node/build, imzalı canlı Windows önizleme/controller HTTP/veri korunumu doğrulandı; gerçek TV/gamepad kabulü açık |
| CLI-01-G | Lazy discovery yüklenirken root yatay scrollbar/animation geometry taşması olmaması | Kullanıcı, 2026-10-18 | 0.30.1 DISC-05: canlı cause/isolation DOM, 81 Node/build/imzalı yayın/HTTP/veri korunumu doğrulandı; kullanıcı scroll kabulü açık |
| CLI-01-H | Gruplu aranabilir ayarlar ve scoped viewport/rail/focus korunarak listeye dönüş | Kullanıcı, 2026-10-18 | 0.31 SETTINGS-01/NAV-03: 91 Node/build, imzalı canlı Windows/HTTP/veri korunumu doğrulandı; kullanıcı kullanım kabulü açık |
| CLI-01-I | UI mesajlarında ASCII dahil zorunlu seçili dil kapsamı ve sekmeden bağımsız settings frame ölçüsü | Kullanıcı, 2026-10-18 | 0.31.1 LOCALIZE-02/SETTINGS-02: 96 dosya/0 missing, 95 Node/build/imzalı yayın, 13 English kategori ve sabit 937×636 frame DOM kanıtı; kullanıcı kabulü açık |
| CLI-01-J | Zile bağlı bildirim popover'ı, okunmamış/okundu/boş-hata durumları; keşifte kesilmeyen controller poster odağı, reduced-motion ve scope/Back korunumu | Kullanıcı 2026-10-21 | **0.40.1/42 canlı**: anchored HeroUI popover + scoped styles, inset/elevated card focus and padded rails. 79 Node/build/served client checks passed; kullanıcı gerçek browser/cihaz kabulü açık. [Dilim](../games-experience-040.md) |
| CLI-01-K | Çalışan controller sistemini koruyarak yakın hedef/sütun/görünürlük iyileştirmesi ve alt oynatıcıya R3 geçiş/dönüş | Kullanıcı, 2026-10-21 | **0.41.1 canlı**; geometri/odak/oyun-modal sahipliği, 74 Node/build/served client doğrulandı; gerçek gamepad kabulü ACCEPT-41-REFINE |

## F. Müzik

| ID | Gereksinim ve kabul ölçütü | Karşılık / kaynak | Başlangıç |
|---|---|---|---|
| MUS-01 | Gömülü etiketlerden sanatçı/albüm/parça/disc/track ve çoklu sanatçı ilişkileri | Jellyfin/Lidarr R02,R05 | 0.26 uygulandı; ACCEPT-26 açık |
| MUS-02 | MusicBrainz release-group/release/recording kimlikleri, derleme ve yeniden basım sürümleri | R05 | Planlandı |
| MUS-03 | Lossless/lossy kalite, bit depth/sample rate/bitrate, metadata ve release profili | R05 | Planlandı |
| MUS-04 | Güvenli etiket yazma, yeniden adlandırma, albüm kapağı ve metadata geri alma | R02,R05 | 0.16/0.26 format matrisi uygulandı; ACCEPT-16/26 açık |
| MUS-05 | Albüm/sanatçı/listeden takip, yeni yayın ve sürüm yükseltmesi | R05 | 0.16/0.26 uygulandı; gerçek kaynak/listeler ACCEPT-16/26 |
| MUS-06 | Gapless, shuffle/repeat, sıra, favoriler, ReplayGain/loudness ve müzik odaklı gezinme | R01,R02 | 0.41 workspace/dock/sırayı kesintisiz büyütme ve grouped ses UI canlı; gerçek cihaz kabulü ACCEPT-41 |
| MUS-07 | LRC/ELRC/metin sözler, zamanlı söz gezinmesi, müzik videosu ve ilişkili albümler | R01,R02 | 0.26 uygulandı; ACCEPT-26 açık |
| MUS-08 | Ses benzerliği/sonic analysis ve kişisel öneriler | R01 | Planlandı |
| MUS-09 | Spotify-esintili kişisel müzik alanı, kalıcı dock, şarkı/favori/geçmiş/listeler, gruplu gear ayarları | Kullanıcı, 2026-10-21 | 0.41 canlı; PCM/Video.js, 64 backend/64 Node/build/HTTP korunumu doğrulandı; cihaz kabulü ACCEPT-41 |
| MUS-10 | Anahtarsız isteğe bağlı otomatik albüm bilgisi ve şarkılarda kullanılan kapaklar; kimlik/etiket/önbellek korunumu | Kullanıcı, 2026-10-21 | 0.41 canlı MusicBrainz/CAA, bounded restart sweep, tam ad/sanatçı; public text probe doğrulandı; gerçek koleksiyon/kapak kabulü ACCEPT-41 |
| MUS-11 | Kategorili radyo/favoriler ve müzik/radyo üzerinde tam ekran şömine/yağmur/akvaryum/denizaltı/sanat modu | Kullanıcı, 2026-10-21 | 0.41 canlı Radio Browser/owned direct-HLS/shared quotas + offline/optional licensed visuals; 18 Türk jazz metadata kaydı canlı okundu, gerçek yayın/cihaz/görsel kabulü ACCEPT-41 |
| MUS-12 | Kullanıcı geri bildirimi: çizilmiş doğa animasyonlarını sil, plak fallback, ek ücretsiz stoklar, uzun başlık sınırları ve belirgin anlık MusicBrainz araması | Kullanıcı, 2026-10-21 | **0.41.1 canlı**; 10 lisanslı video + gerçek prefix probe, 23 backend/74 Node/build/signed publish/HTTP preservation geçti; cihaz/görsel kabulü ACCEPT-41-REFINE |

**0.16 müzik uygulama kanıtı:** MON-03 ve MUS-05 revizyonlu sanatçı/albüm metadata profili, ayrı opt-in arama/edinme, gerçek dosya cutoff/yükseltme, iptal/kota/blocklist ile uygulandı. MUS-06 aynı ses saatinde PCM parça planlama, ReplayGain/peak, favori ve kişisel konum; MUS-08 gerçek LUFS/FFT ve erişime göre kişisel öneri; MUS-04 yönetilen FLAC/MP3/M4A kopya üzerinde doğrulanmış etiket/kapak/adlandırma/undo eklendi. **165/165 backend/native ve 5/5 saf ses zamanlayıcı testi**, 17→19 şema update/rollback, canlı MusicBrainz Radiohead kataloğu doğrulandı. Cihaz/gerçek kaynak kabulü ile çoklu sanatçı, liste/ELRC/müzik videosu ve diğer konteynerler açık: [0.16](../milestone-16.md), MUSIC-04 ve ACCEPT-16.

**0.26 müzik tamamlayıcı kanıtı:** MUS-01 kimlikli albüm/parça katkıları ve konuk sanatçı gezinmesi; MUS-04 dokuz uzantıda çok-değerli etiket, tam tarih/kapak, metadata hash'i, ses hash'i ve undo; MUS-05 kalıcı koleksiyon/çalma listesinden seçimli toplu takip; MUS-07 ELRC/kelime seek ve yerel video/gerçek albüm-recording ilişkileri uygulandı. **326/326 backend/native + 40/40 Node**, şema 29→30 korunumu, üç imzalı paket, update/rollback ve gerçek MusicBrainz Radiohead/OK Computer/Airbag okuması geçti. Cihaz ve gerçek kaynak/koleksiyon kabulü **ACCEPT-26**: [0.26](../milestone-26.md).

## G. Altyazı

| ID | Gereksinim ve kabul ölçütü | Karşılık / kaynak | Başlangıç |
|---|---|---|---|
| SUB-01 | Gömülü/harici iz envanteri, dil, forced/HI, codec ve mevcut-altyazı değerlendirmesi | Bazarr/Jellyfin R02,R07 | Planlandı |
| SUB-02 | Dil profilleri, audio-language exclusions, cutoff ve medya bazlı override | R07 | Planlandı |
| SUB-03 | Sağlayıcılar, API/hesap/token yenileme, kotalar, backoff ve kaynak atfı | R07 | Planlandı |
| SUB-04 | Manuel/otomatik arama, hash/sürüm/dil puanı, eksik listesi ve daha iyi altyazıya yükseltme | R07 | Planlandı |
| SUB-05 | Güvenli indirme, adlandırma, UTF-8 dönüşümü, cache veya izinli yan dosya yayını | R07 | Planlandı |
| SUB-06 | SRT/VTT/ASS ve resim altyazı oynatımı; ayarlanabilir offset, stil ve gerektiğinde burn-in | R01,R02,R07 | Planlandı |
| SUB-07 | Otomatik zaman eşleme; speech/VAD tabanlı senkron ve isteğe bağlı konuşmadan üretim | R07 | Planlandı |

**0.18 altyazı uygulama kanıtı:** SUB-02/04 dosya override, ses dili dışlaması, cutoff/puan artışıyla opt-in yükseltme ve geri alınabilir sürümler; SUB-05 korumalı geçici link, kota rezervasyonu ve idempotent yayın; SUB-06 PGS/DVD bitmap ve ASS stilini görüntüye işleme/seek/delay; SUB-07 yerel Silero/ONNX VAD ile offset/fps önerisi ve WebVTT sürümü eklendi. **197/197 backend/native, 5/5 ses testi**, gerçek çıktı pikselleri ve offline konuşma fixture'ı geçti; canlı model CPU'da başlatıldı. ASR/metin üretimi, doğrusal olmayan retiming, geniş HDR/GPU profili ve gerçek hesap/cihaz kabulü açık: [0.18](../milestone-18.md), SUB-PROVIDER-02 ve ACCEPT-18.

## H. Hesap, operasyon ve ek ürün alanları

| ID | Gereksinim ve kabul ölçütü | Karşılık / kaynak | Başlangıç |
|---|---|---|---|
| USR-01 | Admin/üye profilleri, davet/hesap yönetimi, parola değiştirme ve oturum iptali | R01,R02,R08 | Kısmi |
| USR-02 | Kütüphane/medya/işlem izinleri, ebeveyn denetimi ve profil/PIN geçişi | R01,R02,R08 | Planlandı |
| USR-03 | Kullanıcı kotası, istek/oynatma/downloading sınırları ve denetlenebilir geçmiş | R01,R08 | Planlandı |
| OPS-01 | Kalıcı işler, iptal, retry/backoff, leasing, idempotency, öncelikler ve gözlemlenebilir sonuç | Tümü | Kısmi |
| OPS-02 | Kurulum sihirbazı, disk/izin/bağlantı/codec tanısı; ayarları kullanıcı adına üretme | Tümü | Kısmi |
| OPS-03 | SQLite online backup, anahtarlar, şema yükseltmesi, doğrulama ve geri yükleme | Tümü | Kısmi |
| OPS-04 | Sürümlü imzalı/güvenilir paketler, güncelleme ön kontrolü ve geri dönüş | Tümü | Planlandı |
| OPS-05 | Linux x64/ARM64 + Docker ve Windows paketleri; varsayılan volume/UID/GPU yolları | Tümü | 0.43 Windows signed + Linux x64 headless Docker/CasaOS local image passed; ARM64 native, public store/distribution and physical-host acceptance open |
| OPS-06 | HTTPS, reverse proxy/base URL, kontrollü uzaktan erişim ve bağlantı teşhisi | R01,R02 | HTTPS/proxy/base-path temelleri + 0.43 IP/LAN erişimi ve device-owned native WebRTC doğrulandı; gerçek ağ/cihaz ve DNS kabulü açık |
| OPS-07 | Bildirimler, webhooks, SMTP/push, olay filtreleri; sır redaksiyonu | Tümü | Planlandı |
| OPS-08 | Kaynak/transfer/oynatma ölçümleri, log görüntüleme, disk kapasitesi ve housekeeping | Tümü | Kısmi |
| OPS-09 | Yapılandırma içe/dışa aktarma, profil/yetenek sürümleri ve bağımlılık lisans envanteri | Tümü | Planlandı |
| EXT-01 | Live TV/IPTV/radyo, M3U/XMLTV, kanal eşleme, EPG ve zaman kaydırma | R01,R02 | 0.42/43 canlı; HTTP/HLS, kaynak/rehber/grup/favori ve gerçek native fixture doğrulandı; gerçek yayın/cihaz kabulü ACCEPT-42 |
| EXT-02 | DVR, tuner/çoklu kayıt çakışması, ön/son pay, tekrar kaydı engelleme ve reklam segmentleri | R01,R02 | 0.27 temelleri/0.42 yenilenen UI ve paylaşılan tuner/lifecycle doğrulandı; gerçek DVR ve ileri reklam kapsamı açık |
| EXT-03 | Fotoğraf/albüm, kitap/çizgi roman/sesli kitap/podcast ve okuma/dinleme konumu | R01,R02 | Planlandı |
| EXT-04 | Sağlayıcı/plugin SDK, capability sözleşmesi, sürüm uyumluluğu ve kullanıcıca yönetilen uzantılar | R02,R06 | Planlandı |
| EXT-05 | İçerik odaklı Live TV, public ücretsiz liste seçimi, güvenli IPTV/M3U/tek kanal ekleme, favori/geçmiş/rehber ve büyük/miniplayer | Kullanıcı, 2026-10-22 | **0.42/43 canlı**; 12 public liste/181 Türkiye metadata, 63 backend-native/74 Node/build/signed publish/data preservation geçti; cihaz/yayın kabulü ACCEPT-42 |

**0.19 oynatma kotası kanıtı:** USR-03 hesap/etkin cihaz/toplam eşzamanlı sınır ve denetlenebilir oturum geçmişi; USR-01/02 girişe bağlı stream yetkisi ve etkin iptal; PLY-02/03 ve MUS-06 direct/HLS/PCM ortak lease'i eklendi. **207/207 backend/native, 7/7 ses testi**; HTTP Range/HEAD/koşullu yanıt, açık aktarımın iptali, tek slotta protokol değişimi ve gerçek süreç restart/çökme doğrulandı. İmzalı 22→23 update/rollback ve canlı 0.19 sınır okuması geçti. Kişi bazlı edinme/indirme bütçeleri ve cihaz kabulü ACCOUNT-BUDGETS-01/ACCEPT-19 üzerinde açık: [0.19](../milestone-19.md).

**0.20 bildirim kanalı kanıtı:** OPS-07 webhook/HMAC ve SMTP/TLS/STARTTLS, olay filtreleri, şifreli bağlantı ayarı ve kişisel teslim geçmişi; OPS-01/03 transaction içi outbox, kalıcı saatlik deneme bütçesi, iptal/lease/restart/idempotency eklendi. **221/221 backend/native**, gerçek yerel TLS/SASL/Türkçe MIME, sertifika/downgrade reddi, HMAC/retry/kota ve hesap izolasyonu geçti. Üç imzalı paket, 23→24 update/rollback ve canlı 0.20 doğrulandı. Web/PWA push ve gerçek hedef kabulü NOTIFY-01B/ACCEPT-20 üzerinde açık: [0.20](../milestone-20.md).

**0.21 push kanıtı:** OPS-07/CLI-01/02 web/PWA izin/abonelik, VAPID/AES128GCM, olay filtresi, kalıcı outbox ve kota; USR-01/02 kullanıcı/giriş/cihaz bağlama ve çıkışta iptal; OPS-03 önceki kanal/delivery/attempt verisini koruyan şema 25 eklendi. **240/240 backend/native + 8/8 worker**, bağımsız alıcı kriptografisi, private endpoint/CSRF reddi, stale profil, anahtar yenileme ve bütçe korunumu geçti. Üç imzalı paket, update/rollback ve canlı 0.21 doğrulandı. Gerçek push servisi/tarayıcı cihaz kabulü **ACCEPT-21** üzerinde açık: [0.21](../milestone-21.md).

**0.22 offline kanıtı:** CLI-02 şifreli cihaz kütüphanesi, parça kuyruğu/devam/kota ve yerel Range; USR-02/03 hesap/cihaz/izin süresi; PLY-02/03/SUB-06 native MP4-AAC/WebVTT/burn-in hazırlığı; PLY-09 çatışma korumalı idempotent konum eşitleme eklendi. **248/248 backend/native + 27/27 Node**, byte/hash/altyazı, gerçek çıktı, AES/IndexedDB quota rollback, seek ve konum çatışmaları geçti. Üç imzalı paket, 25→26 update/rollback ve canlı 0.22 doğrulandı. Gerçek tarayıcı depolama/codec ve arka plan kabulü **ACCEPT-22** üzerinde açık: [0.22](../milestone-22.md).

**0.23.1 oda/kumanda kanıtı:** PLY-08 hesap davetli odalar, ortak hazır bariyeri/zaman çizelgesi, drift düzeltmesi ve kişisel uzak kumanda; USR-02/03 her üyede ACL/giriş/oynatma kotası; PLY-03/OPS-01/03 kalıcı CAS/receipt, kaynak epoch'u ve geç hazırlık iptali eklendi. **259/259 backend/native**, 11 oda dahil **38 Node kontrolü**; iki bağımsız direct/HLS akışı, migration'da offline kayıt korunumu, gerçek restart/çökme ve aynı epoch'ta kapalı lease'i yeniden açmama doğrulandı. Üç imzalı 0.23.1 paket, 26→27 ve 0.23.0→0.23.1 update/rollback, canlı şema 27 geçti. Çoklu gerçek cihaz kabulü **ACCEPT-23** üzerinde açık: [0.23](../milestone-23.md).

**0.24 hesap bütçesi kanıtı:** USR-03 aylık yeni edinme/paket boyutu, eşzamanlı aktarım ve kullanım geçmişi; USR-02 kalıcı sahiplik ve üye/yönetici sınırı; ACQ-01/02 manuel/video/müzik bağlamı ve bütçe sonrası tekrar; OPS-01/03/08 kalıcı slot/rezervasyon, kapanış sayaçları ve migration eklendi. **277/277 backend/native + 38/38 Node**; gerçek loopback torrent/magnet/NNTP, süreç çökmesi, eski kayıt korunumu, üç imzalı paket, 27→28 update/rollback ve canlı 0.24 geçti. PLY-03 hızlı FFmpeg tamamlanmasının geç `ready` tarafından ezilmesi düzeltildi. Gerçek kaynak/cihaz kabulü **ACCEPT-24** üzerinde açık: [0.24](../milestone-24.md).

**0.25 bölüm kataloğu kanıtı:** CAT-04/07 birden-çoğa sağlayıcı karşılığı ve kalıcı-ID ile numara/kimlik onarımı; REQ-01/02 açık bölüm-ID kapsamı ve korunmuş istek geçmişi; PLY-07/09 dosyadan sınır önerisi ve doğru bölüm konumu; ACQ-01/02/IMP-03 etkin DVD/dijital sırası ve dondurulmuş import eşlemesi eklendi. **296/296 backend/native + 38/38 Node**, gerçek görüntü/ses analizi, eski FK/alias/sıra/konum korunumu, üç imzalı paket, 28→29 update/rollback ve canlı 0.25 geçti. Gerçek katalog ve cihaz kabulü **ACCEPT-25** üzerinde açık: [0.25](../milestone-25.md).

## I. Oyunlar — kullanıcının 2026-10-19 kapsam genişletmesi

| ID | Gereksinim ve kabul ölçütü | Kaynak | Durum |
|---|---|---|---|
| GAM-01 | Music'in sağında Games/Oyunlar; ortak kapaklı kütüphane, arama/kişisel filtre ve adresli detay/oyun sayfası | Kullanıcı; R37 | GAMING-01: 0.32 kodu; kullanıcı yerleşim/geri dönüş kabulü açık |
| GAM-02 | Seçilmiş gerçek web oyunları Home'da çalışır; ayrı site/hesap yok; gamepad gameplay ve gezinme ayrıdır | R37, CLI-01/05 | Tiny Crate Classic/Wrap It Up kaynak build'i ve oyuncu kodu; gerçek browser/TV/gamepad kabulü açık |
| GAM-03 | Profil bazlı kütüphane/favori/geçmiş ve build'e bağlı checkpoint; stale oturum/CSRF/yol kaçışı eski kaydı bozamaz | R37, USR-02, OPS-03 | Şema 40 ve odaklı backend/birim kontrolleri; gerçek cihaz kayıt kabulü açık |
| GAM-04 | Kullanıcının kendi ROM'larını ekleme ve uygun tarayıcı/native çekirdeği ile oynatma | Araştırma, kullanıcı | 0.37.0/42 canlı: 111 sistem import'u, seçili çekirdek/BIOS klasörü, private GPU/native host; mevcut 3DS için Citra kurulu. Gerçek ROM/browser kabulü kullanıcıda |
| GAM-05 | Home sunucusunda native oyun/emülasyon ve düşük gecikmeli Home istemcisi; Moonlight/Sunshine kullanıcı kurulum zorunluluğu yok | Kullanıcı; yürütme araştırması | 0.43 Linux x64 headless container, 7 original system WebRTC/Opus/input/save-relaunch ve fresh-volume/recreate geçti; Windows 0.37 host korunur. Fiziksel TV/browser/oyun gecikmesi kullanıcı kabulünde |
| GAM-06 | Kullanıcı web paketi import'u; aktif kod izolasyonu, boyut/yol/lisans ve adapter sınırları | R37 | Sonraki dilim; yalnız paketle gelen denetlenmiş oyunlar |
| GAM-07 | Beş seçilmiş gerçek web oyunu metadata/indirilebilirlik kataloğu; installed/catalog ayrı, sahte player/download istatistiği yok | Kullanıcı 2026-10-19; developer sources | GAME-STORE-01 / 0.33 source; acquisition/gameplay kabulü açık |
| GAM-08 | Açık kullanıcı komutuyla pinned provider'dan bounded download, cancellation/restart, checksum/path ve idempotent managed publish | Developer source trees, OPS-01 | GAME-STORE-01; focused fixture tests; gerçek indirme user-owned |
| GAM-09 | Source HTML/JS oyunu isolated runtime'da, profile-backed save ve game-specific input; public licensing scope açık | Developer licences, GAM-03 | GAME-STORE-01 code; gerçek controller/gameplay kabulü açık |
| GAM-10 | Sunucu emülasyonunda olgun upstream emulator/yayın/input bileşenlerini önce analiz edip yeniden kullanma; kendi motorunu yazmama, hardware seçimi kullanıcıda | Kullanıcı 2026-10-19 | [Kaynak karşılaştırması](../gaming-reuse-assessment.md) sonrası RetroArch/mGBA/MediaMTX + [sınırlı upstream input backport](../gaming-native-frontend-backport.md) isolated actual prototype testinde geçti. Yeni motor yok; patched vs official release ayrımı açık. Lisans/platform/gerçek browser-device kabulü açık |
| GAM-11 | Home içinden kendi tek GBA ROM'unu bounded/authenticated import; private metadata/idempotent verified copy, native runtime hazır değilse Oyna yok | Kullanıcı “başla”; GAM-04/05/10 | GAMING-SETUP-01 / 0.34.1/41 canlı; **94 backend/37 Node/0 skipped**, user-source/save/paused transfer korunumu ve proof-bound explicit enable. Kullanıcı gerçek ROM/device kabulü açık |
| GAM-12 | ROM-first oyun kütüphanesi, gerçek sistem/kategori/alfabe gruplama; ücretsiz oyunlar ayrı; açık ve az sayıdaki emülasyon ayarı/kapak işlemleri | Kullanıcı 2026-10-20 | GAMING-LIBRARY-02 / 0.35.0/41 canlı; build checks/data preservation, actual provider/device/UI acceptance kullanıcıda. Current GBA yalnız; DS/PS/Wii destek ilanı değil |
| GAM-13 | RetroArch’ın tüm emülasyon kapsamını Home yönetimli sistem/çekirdek/ROM/BIOS/görüntü/giriş/kayıt yollarına bağla; yalnız katalog etiketini çalışır destek sayma | Kullanıcı 2026-10-20 | NATIVE-HOST-01 **0.37.0/42 canlı**, 194 Windows adapter / 111 sistem, GPU/analog/pressure/pointer/keyboard/four-port/disc/options/native-save yolları bağlı; Citra kurulu ve mGBA korunuyor. 84 backend/17 Node + native/Home proofs. ALL-RETROARCH-01 yalnız dış platform/fiziksel çevre birimi/gerçek core-game-device/distribution kabulü için açık; [kanıt](../gaming-native-host-037.md) |
| GAM-14 | Varsayılan kapsam GB/GBC, GBA, NES, SNES, Mega Drive ve PS1: az sayıda olgun çekirdek, açık kurulum/lisans, görüntü/giriş/kayıt testleri ve ayrı gerçek-oyun kabulü. Eski ROM/kayıt/çekirdekler korunur; geniş katalog gelişmiş erişimde kalır; PS3-class kapsam hedefi değil | Kullanıcı 2026-10-21 | BASIC-ROM-SETUP-01 **0.38.0/42 canlı**: beş seçili çekirdek, yedi varsayılan import sistemi ve yalnız dört eksik yazılım kurulumu. 70 backend/28 Node/build; **7 core-check passed / 5 host-stream passed / SNES+Mega Drive host açık**. Gerçek oyun/browser/cihaz/gecikme kabulü açık; BASIC-ROM-01 tamamlandı sayılmaz. [Kapsam/kanıt](../gaming-basic-selection-038.md) |
| GAM-15 | ROM eklemede dosyadan bounded otomatik sistem önerisi; ortak disk biçiminde kesin olmayan konsolu sorma, CUE/BIN'i birlikte ve CUE girişli gönderme, eksik track adını gösterme, sistem değişince dosyaları koruma; kaynak dosyaları değiştirmeme | Kullanıcı 2026-10-21 | ROM-IMPORT-02 **0.38.1/42 canlı**: read-only header/extension probe, dosya-önce/otomatik dialog ve case-safe disk import/runtime references. **67 backend/14 Node/build/signed publish/data preservation**, gerçek izole HTTP multipart/CSRF/actor/source-byte kontrolü geçti. Kullanıcının gerçek CUE/BIN/browser kabulü açık. [Kapsam](../gaming-rom-import-0381.md) |
| GAM-16 | Genel / profil-emülatör varsayılanı / bu oyun kapsamı ve görünür miras/reset; oyun başına varsayılan düzenleme, açık emulator-default komutu; ortak gruplu detay/pause ayar yüzeyi, core-relevant temel seçenekler ve ayrı Gelişmiş; ROM/kayıt kimliği korunur | Kullanıcı 2026-10-21 | **0.39.0/42 canlı**: version-bound sparse game/core scopes, revision/ownership/reset, grouped shared settings, native schema cache and corrected default order; legacy/save IDs preserved. **127 backend/16 Node/build/signed publish/HTTP data preservation**. User browser/real-game acceptance separate. [Scope/proofs](../gaming-settings-metadata-039.md) |
| GAM-17 | ROM metadata/kapak sağlayıcısı: fingerprint/system/region/edition kanıtı, belirsiz eşleşme seçimi, dil/kaynak/manual-field korunumu, güvenli credentials ve bounded durable enrichment; import/play bloke olmaz | Kullanıcı 2026-10-21 | **0.39.0/42 canlı ilk public dilim**: keyless Hasheous contract + repaired libretro directory art; verified isolated file/hash/size/platform matching, manual alternatives, private cache/jobs and field/save preservation. Not full keyed IGDB/ScreenScraper or guaranteed Turkish/description coverage; live user match/art acceptance open, no development media acquisition. [Implementation/licences/acceptance](../gaming-settings-metadata-039.md) |
| GAM-18 | Koleksiyon çapında varsayılan otomatik metadata/kapak, bounded backfill/retry/pause; Games gear ile merkezi sekmeli ayarlar, temalı konsol rafları, yenilenmiş oyun detay/ayar yüzeyi; güvenilir disk algılama ve belirsiz biçimde açıklamalı seçim | Kullanıcı 2026-10-21 | **0.40.1/42 canlı**: actual Hasheous -32000 recovery + matching-quote artwork parser; 205-ROM bounded refill/backoff, regional art, ISO9660 range detection and UI redesign. **138 backend/79 Node + final 15 targeted checks/build**; 4 ROM/10 states/10 saves/native enable/3 paused preserved. Production auto-matched/fetched Tony Hawk and Mario covers; genuine demo/mod ambiguity remains reviewable. [Kanıt/kapsam](../games-experience-040.md) |
| GAM-19 | Ekransız Linux x64/CasaOS sunucusu, IP ile LAN web erişimi, device-owned uzak ROM görüntü/ses/kumanda, dokunmatik kontrol, kalıcı volume/restart ve taşınabilir native yazılım paketi | Kullanıcı 2026-10-22 | **0.43 yerelde teslim edildi**; 109 backend/68 Node, 7 gerçek izole WebRTC sistem akışı, range/HLS, shutdown/save/recreate ve veri korunumu geçti. Fiziksel CasaOS/TV kabulü ACCEPT-43. [İş kaydı](../headless-lan-043.md) |

**2026-10-21 kapsam kararı:** GAM-14, GAM-13'ün bütün-emülatör genişletmesini ilk kararlı sürüm için **öncelik dışına** alır. Mevcut geniş metadata/adapter ve Citra kullanıcı içeriği silinmez; katalogda bulunmak doğrulanmış oyun desteği sayılmaz. Sunucu emülasyonu/istemci gecikmesi sorunu kapsamı daraltınca kendiliğinden çözülmüş sayılmaz.

GAMING-01 bütün Gaming Mode'un tamamlandığı anlamına gelmez. [Uygulama ve kabul sınırları](../gaming-first-slice.md).

**0.32.1 GAMING-FIX-01:** GAM-02/03 için native PWA/sandbox başlangıç, eksik font ayar kaynakları, Window receiver, false active-session guard ve page-exit save/close düzeltildi. **27 backend / 104 Node**; iki engine gerçek izole Chromium frame'inde bootstrap etti. Fiziksel TV/gamepad/sustained play kabulü açık: [kanıt](../gaming-startup-fix.md).

## Eşdeğerlik sınırları

**0.27-dev geliştirme kaydı:** CAT-08/REQ-04 kişi/listeden koleksiyon; SUB-06/07 ASR/bölgesel zaman eşleme; CLI-03 kodla cihaz girişi ve Cast/AirPlay/DLNA; PLY-07 atlama; OPS-08/09 ileri operasyon; IDX-07/08/09/EXT-04 adaptör/SDK; EXT-01/02/03 canlı TV/DVR/raflar/podcast backend ve arayüzleri eklendi. Şema 38 entegrasyonu DVR atomik yayın/restart, kalıcı alıcı kumandası/gerçek konum ve kaynak revizyonu/proxy yönetimini tamamladı. Derlemeler ve önceki 10 dahil 30 odaklı backend kontrolü geçti. **Bu kayıt kapsamlı test veya gerçek cihaz/sağlayıcı kabulü değildir**; birleşik doğrulama sonraki turda: [durum](../development-completion.md).

**SOURCE-02 / şema 39:** 12 hazır kaynak profili ve rehberli test/ekleme/düzenleme tamamlandı. 42 odaklı backend kontrolü, 6 doğrudan kaynağın gerçek metadata/RSS ve 6 üyelikli kaynağın caps okuması geçti. Profil kapsamı, lisans/başvuru ve kabul sınırı [kaynak kataloğunda](../source-catalog.md) kayıtlıdır.

Plex'in lisanslı yayın/kiralama kataloğu, LyricFind gibi ticari veri servisleri, tuner cihazları ve mağaza hesapları ayrı dış kaynak bağımlılıklarıdır. Bunların yerine sahte katalog veya var olmayan hizmet sunulmaz. Yazılım yeteneği ile veri/cihaz/hesap erişimi ayrı kabul kapılarıdır.

Bir upstream özelliği keşfedildiğinde bu tabloya yeni ID, kaynak ve kabul koşulu eklenir. Kullanıcı kararı olmadan bir gereksinim sessizce listeden çıkarılmaz.
