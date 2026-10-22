# 0.27.0-dev.1 — Birleşik geliştirme ve önizleme

Kullanıcı kararı: kalan yedi grup tek geliştirme çalışmasında uygulanır. Ara sürüm paketleme, update/rollback ve geniş test turları bu çalışmanın sonrasına alınır. Derleme kontrolleri geliştirme sırasında yapılır. Test/kabulü yapılmamış kod, doğrulanmış ürün olarak işaretlenmez.

Güncelleme: **2026-10-17**. Kaynak kod sürümü **0.27.0-dev.1**, şema **39**. Kullanıcının yeni kaynak ekranına erişimi için Windows önizleme paketi üretildi; gerçek yedekte migration ve ayrı ortamda update/rollback doğrulandıktan sonra **http://localhost:5080** güncellendi. Çalışan runtime **0.27.0 / şema 39**, son tam regresyonlu yayın **0.26.0**'dır. [Yayın/veri kanıtı](preview-027.md).

| Grup | Gereksinimler | Geliştirme | Test/kabul |
|---|---|---|---|
| Kişi ve kaynak listelerinden koleksiyon/takip | CAT-08, REQ-04 | Kod ve arayüz eklendi | Sonraki toplu tur |
| ASR, bölgesel zaman eşleme ve resim altyazıları | SUB-06/07, PLY-05 | Kod ve arayüz eklendi | Yerel model/codec ve cihaz turu |
| Cast/AirPlay/DLNA ve cihaz bağlantısı | CLI-03 | Kod ve arayüz eklendi | Gerçek alıcı/oturum turu |
| Intro/recap/credits ve reklam segmentleri | PLY-07, EXT-02 | Kod ve arayüz eklendi | Gerçek dosya/algılama turu |
| Log, ölçüm, bakım, yapılandırma aktarımı | OPS-08/09 | Kod ve arayüz eklendi | Sonraki toplu tur |
| Doğrudan adaptör, proxy, geçmiş ve SDK | IDX-07/08/09, EXT-04, OPS-02 | API/SDK, 12 hazır profil ve SOURCE-02 rehberli kurulum kodu eklendi | 6 doğrudan metadata/RSS + 6 caps geçti; hesap/aktarım/browser kabulü açık |
| Live TV/DVR, fotoğraf/kitap/çizgi roman/sesli kitap/podcast | EXT-01/02/03 | Kod ve arayüz eklendi | Gerçek kaynak, kayıt/okuma/dinleme turu |

Ana kararlar:
- Uzun süren yeni işler Home'un kalıcı, revizyon/lease ve iptal taşıyan iş altyapısını kullanır.
- ASR yerel `whisper.cpp` yürütücüsü ve kullanıcıca seçilen yerel modelle çalışır. Model/yürütücü bulunmadığında durum açık gösterilir. Codec aracı ürün mimarisinin yerine geçmez.
- Cast web gönderici SDK'sı, Safari AirPlay hedef seçicisi ve Home'un DLNA/SOAP denetleyicisi ayrı yeteneklerdir; medya erişimi süreli, iptal edilebilir sunucu izinleriyle verilir.
- Kaynak adaptör SDK'sı sürümlü deklaratif sözleşmedir; JSON/RSS sağlayıcıları ortak kimlik, proxy, kota ve doğrulama hattından geçer.
- M3U/XMLTV ve podcast içerikleri kullanıcı tarafından bağlanır. Geliştirme doğrulamasında internet medya aktarımı başlatılmaz.
- Fotoğraf ve belge varlıkları aynı kütüphane izinlerine tabidir; kaynak dosyalar salt okunur, okuma konumu hesaba özeldir.

## Eklenen akışlar

### Katalog
- TMDB/TVmaze kişi arama, oyuncu/yönetmen/yazar/ekip filmografisi ve yapım katkıları.
- TMDB kullanıcı listesi/film serisi veya kişinin filmografisini kendi Home koleksiyonuna bağlama; 12 saatlik isteğe bağlı yenileme, hesapla istek oluşturma ve kalıcı dışlama istisnaları.
- Keşif ekranı ve **Takip → Koleksiyonlar → Kişi ve kaynak listelerinden ekle** yüzeyleri.
- **DISC-03 / CAT-02, kullanıcı geri bildirimi (2026-10-16):** Keşfet'te eksik olan tür filtresi eklendi. **Filmler / Diziler → Türlere göre keşfet** altında TMDB'nin gerçek, medya türüne ve metadata diline özgü genre listesi gösterilir; film/dizi değişiminde seçim temizlenir. Aksiyon/komedi gibi tür kimlikleri örnek katalogdan veya sabit bir yapım listesinden üretilmez.
- Türle popüler/yüksek puanlı/yaklaşan keşif `discover/movie` veya `discover/tv` ve `with_genres` kullanır. Yüksek puanlı tür keşfi en az 200 oyu; yaklaşan filmler önümüzdeki 90 günü, yayındaki diziler önümüzdeki 7 günü esas alır. Tür + başlık araması veya haftalık trend, sağlayıcının `genre_ids` verisi üzerinden sunucuda filtrelenir; istek başına en fazla 4 tam sayfa/30 saniye ve açık `nextPage` ile boş sayfalardan sonra devam edilir. Ebeveyn/hesap filtresi sonuçlara ayrıca uygulanır.
- Tür listesi 24 saat, keşif sonuçları 15 dakika dil/tür/koleksiyon/sorgu/sayfa anahtarıyla önbelleğe alınır. TVmaze ekranında tür keşfi için TMDB geçişi sunulur. Tür filtresi 2026-10-17 tarihli 0.27 önizleme istemcisinde sunulur.

### Altyazı ve atlama
- Yerel whisper.cpp ile 10 dakikalık ses pencerelerinde CPU ASR; yarım kalan pencere işi toparlama, dil/İngilizce çeviri, önizleme ve açıkça **konuşmadan üretilmiş** VTT sürümü.
- VAD ile bölgesel/monoton zaman eşlemesi; DVB bitmap ve XSUB için mevcut burn-in hattına codec eşlemesi.
- Gerçek chapter/metin işaretleri ve birden fazla bölümdeki ortak ses üzerinden intro/credits/recap önerisi; elle giriş/özet/jenerik/reklam aralıkları, kişisel otomatik atlama tercihleri.
- Yeni altyazı ve atlama kontrolleri dosya ayrıntısı/oynatıcı ayarlarındadır.

### Cihazlar
- Beş dakikalık cihaz kodu, gizli polling anahtarı, mevcut hesabın açık onayı ve tek kullanımlı cookie girişi.
- Google Cast göndericisi, Safari AirPlay hedef seçicisi, IPv4 SSDP/DLNA keşfi ve AVTransport/RenderingControl SOAP kumandası.
- Hesap/giriş ve oynatma lease'ine bağlı süreli alıcı URL'leri; HLS/dosya sunumu ve HLS bildirmeyen alıcı için hazırlanabilen MP4 kopyası.
- Alıcılar için erişilebilir LAN/HTTPS Home adresi ayarı; medya ayrıntısı ve oynatıcıdan **TV veya hoparlöre gönder**.
- Gönderici ve AirPlay medya öğesi oturum kapsamındaki kalıcı istemci katmanındadır; ayrıntı/oynatıcı paneli kapatıldığında kumanda sayfa altında kalır. Çıkış/profil değişimi ve sayfa kapanışı eski göndericiyi sonlandırır. Açık DLNA oturumuna aynı girişten yeniden kumanda bağlanabilir.
- DLNA konum/transport/ses durumu sınırlı sunucu polling'iyle okunur; kişisel konum gerçek gözlemden yazılır. Cihaz başka URL'ye geçtiğinde Home kumandası kapanır. Cihaz adresi ve hazırlanan dosya izin oluşturulurken sabitlenir; yeni keşif/kopya mevcut akışı başka hedefe geçirmez.

### Operasyon ve kaynaklar
- Sınırlı/rotasyonlu, sır redaksiyonlu JSONL günlükleri; gerçek süreç CPU/bellek, disk, veritabanı ve iş/oynatma örnekleri.
- SQLite bütünlük/sıkıştırma komutları, iş günlüğü, zamanlanabilir Home analiz-cache temizliği.
- Genel tercihler, araç ayarları ve kalite profili JSON dışa/içe aktarımı; önizleme hash'i ve idempotent uygulama makbuzu. Tam state/anahtar taşıması mevcut backup/restore akışındadır.
- Internet Archive/Nyaa RSS'ye YTS, EZTV, API Bay ve Torrents.csv doğrudan adaptörleri eklendi; kurulan JSON/RSS manifestleri için sürümlü [Source Adapter SDK](../sdk/README.md) kullanılır.
- 2026-10-16 kullanıcı geri bildirimiyle **SOURCE-02** uygulandı: **site seç → gereken bilgileri gir → test et → kaynağı ekle**. Altı doğrudan torrent ve altı üyelikli Usenet profili; otomatik adres/protokol, koşullu anahtar alanı, test sırasında gizli/devre dışı geçici kaynak, atomik ekleme ve revizyonlu düzenleme. Genel API/adaptör formu Gelişmiş'e taşındı. [Profil listesi, sınırlar ve kanıt](source-catalog.md). Bu 12 profil bütün Prowlarr tanımlarının kapsandığı iddiası değildir.
- HTTP/SOCKS proxy, korumalı cookie/kimlik başlıkları, günlük query/grab HTTP bütçesi ve kaynak istek geçmişi.
- Kayıtlı cookie/proxy parolası/başlıkları açık temizleme komutlarıyla kaldırılabilir; kurulu adaptörler kullanım ilişkisi kontrolüyle kaldırılır. Proxy ayarı değişirken açık HTTP yanıtı kendi istemcisini gövde kapanana kadar tutar.
- Kaynak/transport/adaptör değişimi ortak kaynak revizyonunu yükseltir. Var olan sonuçlar atomik geçersizleşir; değişiklikten önce başlayan arama sonradan eski sonuç/caps yayımlayamaz.

### Canlı TV ve raflar
- Kullanıcı M3U/IPTV/radyo kaynağı ve XMLTV; kanal sıralama/yaş tercihi ve elle EPG ID eşlemesi.
- Oturum/hesapla korunan HLS canlı oynatma, 30 dakikalık zaman kaydırma; kaynak tuner sınırını canlı ve DVR arasında paylaşma.
- Program veya zaman/süreyle kayıt; yeni-bölüm kuralı, ön/son pay, öncelik, çakışma, tekrar engeli, iptal ve parça üzerinden restart toparlama. Kural değişimi gelecekteki planları atomik iptal eder.
- TV kaynağı adı, M3U/XMLTV bağlantısı, etkinlik, LAN ve tuner kapasitesi düzenlenebilir. Saklanan bağlantı sırları arayüze geri verilmez. Kanal etkinliği/EPG eşlemesi ile kayıt payı, öncelik ve yalnız-yeni tercihi arayüzden değiştirilebilir; çevrimdışı kaynağın kayıt kuralı da kapatılabilir.
- XMLTV tekrar bilgisi üç durumlu tutulur: tekrar, açıkça yeni, bilinmiyor. `previously-shown` yokluğu tek başına yeni yayın sayılmaz; yalnız-yeni kurallar açık `new` bilgisiyle çalışır.
- PDF.js PDF, temizlenmiş EPUB/metin, CBZ/CBR resim sayfaları; hesap/fingerprint/client-sequence ile okuma konumu.
- Salt-okunur fotoğraf/klasör albümleri, slayt/favori, ayrı içerik yaş ayarı. Sesli kitap kütüphanesi ve M4B, mevcut chapter/konumlu oynatıcıya bağlandı.
- Kişisel RSS/Atom podcast aboneliği, gerçek bölüm/enclosure verisi, yetkili Range proxy'si ve dinleme konumu.
- **Canlı TV** ve **Raflar** ana gezinmeye eklendi. Yerel, canlı ve podcast oynatmaları hesap/cihaz slot hesaplarına birlikte girer.

## Entegrasyon yaşam döngüsü — DEV-27-INTEGRATION

- **EXT-01/02, OPS-01:** DVR yakalama denemeleri yeniden başlamalar dahil altıyla sınırlıdır. Okunabilen parçalar finalizasyon aşamasına geçer; süre bitmiş kesintili kayıt da toparlanır. `publishing` aşamasında kayıt/medya kimliği, hedef, boyut, SHA-256 ve probe sonucu kalıcı günlüğe bağlanır. Hedef dizinde geçici kopya doğrulanıp atomik taşınır; dosya taşındıktan sonra kesilse de aynı katalog kimliğiyle tamamlanır. Çakışan dosya korunur. Yayın başlamadan iptal kazanabilir; atomik yayın başladıktan sonra günlük tamamlanır. Kütüphane taraması bu aşamayla çakıştırılmaz.
- **CLI-03, USR-02/03:** Alıcı komutları payload hash'i/komut kimliği/revizyonla korunur. Aynı DLNA hedefinin iki aktif Home sahibi olamaz. Playback kapatılması alıcı iznini aynı veritabanı işleminde kapatır. Uyumlu kopya işi kaynak erişimini yayın anında yeniden denetler ve kopya kaydıyla iş bitişini birlikte kaydeder; dosya taşıma sonrası kesinti yayın günlüğünden tamamlanır. Range/HEAD/koşullu yanıtlar izin ve sabit dosya kimliği kontrolünden geçer.
- **IDX-08/09, EXT-04:** Ağ istemcisi ömrü yanıt gövdesine bağlıdır; yapılandırma sırasında eski yanıt kesilmez. Aynı-origin adaptör yolu ile kaynak revizyonu, sorgu ve sonuç yayını sınırlarında denetlenir. Yeni kaynak revizyonu medya edinmesini kendiliğinden başlatmaz.

Başlıca uygulama noktaları: `LiveTv/DvrPublisher.cs`, `LiveTv/DvrRecorder.cs`, `Infrastructure/MediaStore.DvrPublication.cs`, `Infrastructure/VerifiedFilePublication.cs`, `Playback/ReceiverService*.cs`, `Infrastructure/MediaStore.ReceiverSessions.cs`, `Sources/SourceTransportService.cs`, `web/src/components/ReceiverPlayback.tsx`, `LiveTv.tsx`, `SourceExtensions.tsx`.

## Veri ve derleme

Yeni migration'lar: **031 product work, 032 segments, 033 receivers, 034 operations, 035 source adapters, 036 live TV, 037 shelves, 038 integration lifecycle, 039 source catalog**. Kütüphane tablosu ek türlerle yeniden kurulur; mevcut kimlik/ilişkiler taşınır. 038; DVR yayın günlüğü/deneme sayacı, alıcı cihaz/dosya snapshot'ları/komut makbuzları ve kaynak revizyonu tetikleyicilerini ekler. 039; kaynak profil kimliği, geçici bağlantı ayrımı ve oturum/revizyonlu kurulum kaydını ekler. Source media klasörleri üzerinde yazma/silme işlemi yoktur. Yeni süreçler Home'a ait çalışma/staging alanlarını kullanır.

- Frontend TypeScript, worker TypeScript, Vite/PWA üretim derlemesi geçti.
- .NET çözümü derlendi; derleme hatası/uyarısı yok.
- İlk şema 37 geliştirmesindeki **10 odaklı veri/izin/sınır kontrolü**: `artifacts/test-results/development-boundaries-v027.trx`.
- Şema 38 entegrasyonunda **30/30 odaklı backend kontrolü, 0 atlanan** geçti; önceki 10 kontrol bu sayıya dahildir. DVR rename→DB kesintisi/tek katalog kimliği/tarama kilidi/çakışma/iptal, kalıcı yakalama bütçesi, alıcı kopyası/komut replay/gerçek gözlem/cihaz sahipliği, HTTP Range/HEAD/iptal önceliği, kaynak revizyonu ve gerçek loopback HTTP proxy yanıtının ayar değişiminde korunması kapsandı. Bunlar izole fixture/API kontrolleridir. Kanıt: `artifacts/test-results/integration-lifecycle-v027-final.trx`.
- İlk entegrasyon turu 29/30 geçti; adaptör isteği yolunun kurulum doğrulamasıyla aynı şekilde ters eğik çizgiyi reddetmesi tamamlanıp tur tekrarlandı. İlk sonuç korundu: `artifacts/test-results/integration-lifecycle-v027.trx`.
- Tür keşfi diliminde frontend/worker/PWA ve .NET derlemesi, **8/8 odaklı metadata/API kontrolü, 0 atlanan** geçti: ayrı film/dizi tür kimlikleri, dil/önbellek/token sınırı, sağlayıcı `with_genres`/sıralama/tarih parametreleri, boş arama sayfalarından devam cursor'u ve giriş gereksinimi. Kanıt: `artifacts/test-results/genre-discovery-v027.trx`. Bu ayrı turda tüm 0.27 regresyonu yeniden çalıştırılmadı; gerçek tür kataloğu/browser kabulü açıktır.
- Kaynak kataloğu/kurulum diliminde **42/42 odaklı backend kontrolü**, 0 atlanan; frontend/worker/PWA, .NET çözümü ve worker sözdizimi geçti. Bu sayı önceki 30 entegrasyon kontrolünü içerir. Home sorgu/ayrıştırıcısıyla 6 gerçek doğrudan kaynak ve 6 üyelikli kaynağın caps'i salt okunur doğrulandı. Kanıtlar: `artifacts/test-results/source-catalog-v027-complete.trx`, `artifacts/source-catalog-readonly-v027*.json`; [ayrıntı](source-catalog.md).
- Worker JavaScript sözdizimi kontrolü geçti. PDF.js 6.4.299 kurulum denetimi 0 bilinen açık bildirdi.
- Windows önizlemede gerçek yedeğin 111 tablosu/103 eski satırı korunarak 30→39 migration, ayrı süreçte 0.26→0.27→0.26 update/rollback ve canlı 1 isteğin korunumu doğrulandı. Yeni kaynak/tür istemcisi sunuluyor: [kanıt](preview-027.md).
- Geniş backend/native regresyon, ASR modeli, Cast/AirPlay/DLNA alıcıları, gerçek IPTV/DVR, sağlayıcı hesabı/edinme, proxy ve tarayıcı/okuyucu kabulü **henüz yapılmadı**. Bunlar sonraki test/kabul çalışmasıdır.

Bu kayıt bir üretim desteği/uyumluluk sertifikası değildir. ASR yürütücüsü/modeli, kaynak hesapları ve gerçek cihazlar yapılandırıldıktan sonra doğrulanır. Native TV/mağaza istemcileri, Linux/Docker runtime ve dağıtım/imzalama/lisans kararları mevcut yayın kapılarında kalır.
