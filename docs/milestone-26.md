# 0.26 — Müzik katkıları, toplu takip, sözler ve video bağlantıları

Uygulama/doğrulama: **2026-10-15/16**, çalışan yayın **0.26.0**. Gereksinimler **MUSIC-04 / MUS-01/04/05/07**; ilişkili **MUS-02/03/06, MON-03, USR-02/03, OPS-01/03**. Kod ve yayın kontrolleri tamamlandı; gerçek cihaz/katalog kabulü **ACCEPT-26** üzerinde açık.

## Müzik kataloğu

- Sanatçı, credited name, birleştirme metni (`feat.`, `&` vb.), sıra, sort name ve MusicBrainz kimliği ayrı alanlardır. Albüm sanatçısı ile parça sanatçısı ayrı ilişkilerdir.
- Etiket katkıları ve açık MusicBrainz yayın eşlemesinin katkıları ayrı saklanır. Yeniden tarama eşleşmeyi ezmez; eşleşme kaldırıldığında dosya katkıları geri görünür.
- **Müzik → Sanatçılarım** erişilebilir dosyalardaki albüm ve parça sanatçılarını bir araya getirir. Konuk sanatçının albümü açıldığında albümdeki erişilebilir parçaların tümü görünür. Katkı adları MusicBrainz kişi/grup kimliklerine bağlanır.
- `AC/DC`, virgül, `&` veya `feat.` içeren tek bir adı metin sezgisiyle birkaç sanatçıya bölmeyiz. Çok-değerli alanlar ve sıralı ID listesi kullanılır; isim ve ID sayısı farklıysa tahmini ID ataması yapılmaz. Eski ID3 sürümünün `/` ayıracı belirsizse düz ad korunur.
- Müzik import doğrulaması birleştirme metni farklı olsa da aynı sıralı katkı kimliklerini tanır; farklı açık albüm sanatçı kimliklerini reddeder.

## Listeden toplu takip

**Müzik → Listeden toplu takip** iki kaynak kabul eder:
1. Kullanıcının belirttiği herkese açık MusicBrainz sanatçı, release-group veya release koleksiyonu.
2. Home'daki kendi çalma listesi; erişilebilir müzik dosyalarının albüm/parça katkıları veya release-group kimlikleri.

Yerel listeden çıkan kimlikler MusicBrainz'den okunur. Koleksiyon sayfaları aynı toplam/offset ve tekrarsız kaynak kimlikleriyle denetlenir. Aynı albümün farklı baskıları tek release-group takibine dönüşür. Kısmi/bozuk sayfa takip uygulamaz.

İş, her metadata adımından sonra kalıcı cursor/items/seen snapshot'ını yazar; worker tekildir, restart lease/revision değiştirir. İptalden veya yeni lease'ten gelen geç yanıt uygulanmaz. Geçici sağlayıcı sorunlarında üç yeniden deneme, 60/120/180 saniye bekleme vardır.

Hazır listede kullanıcı kayıtları seçerek uygular. Uygulama tek transaction'dır; seçim hash'i tekrar komutunu idempotent yapar. Kapasite önizleme sonrasında tekrar kontrol edilir. Mevcut takip/otomasyon profili değiştirilmez. **Yeni yayınları da takip et** seçeneği yeni metadata/bildirim takibi oluşturur; kaynak araması ve edinme ayrı mevcut opt-in tercihleridir.

Sınırlar: kaynakta 500 kayıt ve 500 farklı aday; snapshot 4 MiB; sunucuda 32, hesapta 4 etkin/hazır iş; plan ömrü 24 saat; son 200 terminal iş/7 gün; hesapta 1000 sanatçı veya albüm listesi kaydı ve 100 otomatik müzik takibi. Hesap sürümü, sahiplik, keşif/unrated izni ve yerel listenin erişilebilir kimlik fingerprint'i uygulanırken denetlenir.

## ELRC ve sözden gezinme

- Dosya yanındaki `.elrc` / `.lrc` zamanlı sözler, gömülü sözler ve son olarak aynı adlı `.txt` desteklenir. Okuma kütüphane sınırında, 256 KiB ile sınırlıdır.
- Satır `[mm:ss.xxx]`, kelime `<mm:ss.xxx>`, global millisecond offset ve aynı sözün birkaç satır zamanında tekrar edilmesi desteklenir. Kelime son işareti sessiz aralıkta vurguyu bitirir.
- Kelime sırası veya sonraki satırla çakışma belirsizse satır metni korunur; kelime zamanı uygulanmaz. 10.000 satır/40.000 kelime sınırı vardır.
- PCM ve özgün oynatıcı sözden satır/kelime konumuna gider. Ortak odada kumanda izni olmayan katılımcı seek komutu göndermez. Klavye odağındaki söz görünümü sabitlenir; azaltılmış hareket tercihi korunur.

## Müzik videosu ve ilişkili albümler

Albüm/recording ilişkileri gerçek MusicBrainz ilişki kayıtlarından okunur; tür ve yön saklanır. Albüm ilişkisi Home'un yayın ayrıntısına açılır. Video recording ses recording'den ayrı kimliktir.

Parça ayrıntısındaki **Video ve ilişkiler** erişilebilir yerel video dosyalarını bağlar ve Home oynatıcısında açar. Yönetici açık dosya bağlantısı kurar; isteğe bağlı video recording MBID sağlayıcıda `video=true` ile doğrulanır. Ses/video kütüphane izinleri ayrı uygulanır. Ses recording kimliği değişince önceki kimliğe ait bağlantı normal görünümden çıkar; yönetici eski bağlantıyı yeniden eşleyebilir veya kaldırabilir. Ses ve videonun konumları ayrı tutulur. Parça başına 20 bağlantı sınırı vardır.

## Ek etiket konteynerleri

Yönetilen kopyada **FLAC, MP3, M4A, Ogg/Vorbis (`ogg/oga`), Opus, WAV, AIFF (`aif/aiff`)**:

1. Kaynak, yazma akışı sunmayan TagLibSharp abstraction ile okunur.
2. Yazma öncesi ayrı geçici dosyaya kopyalanır; ID3v2.4 / Xiph / iTunes etiketleri, sıralı `ARTISTS`/`ALBUMARTISTS`, MusicBrainz ID, tam tarih, kapak ve ReplayGain yazılır.
3. Gerçek FFmpeg ses paket hash'i karşılaştırılır. Başlık, albüm, disc/track, ID/katkı, tarih ve kapak tekrar okunarak doğrulanır.
4. Önceki dosya byte-hash ile geri alma alanına kaydedilir; mevcut durable publish/rename/undo günlüğü atomik yayımlar. Kaynak kütüphaneye veya hardlink kaynağının içeriğine yazılmaz.

Önizleme, yalnız fiziksel dosya fingerprint'ine değil yayın/parça/katkı metadata hash'ine de bağlıdır. Sıra beklerken veya yayımlama öncesinde eşleme değişirse yeni önizleme gerekir; eski komut farklı metadata'yı sessizce yazamaz. Önceki sürümden hash taşımayan bekleyen tag işleri de yeniden önizleme ister; tamamlanmış undo günlükleri korunur.

Native test, TagLibSharp'ın AIFF için oluşturduğu eski ID3 varsayılanında `AC/DC` adının bölündüğünü yakaladı. Hazırlanan kopyada ID3v2.4 ve açık çok-değerli alanlar kullanıldı; özgün kaynak korundu. Sonraki format/kapak/tarih/undo kontrolleri geçti.

Opus native testi ayrıca tam `N×255` byte etiket paketinde TagLibSharp 2.3'ün eksik sıfır lacing sonlandırıcısını yakaladı; ses hash doğrulaması yayını durdurdu. Home yalnız hazırlanmış Ogg başlık kopyasında kanıtlanmış bu sınırı RFC 3533'e göre tamamlar, CRC'leri kontrol eder/yeniler; dolu sayfada sıralı continuation sonlandırma sayfası ekler. Ses paketleri ve granule konumları korunur. İşlem iptal/CRC hatasında kopyayı değiştirmez; bu bir codec veya kaynak ses dönüşümü değildir.

## Veri ve kod

Şema **29 → 30**, `030_music_catalog.sql`: `music_album_credits`, `music_track_credits`, `music_video_links`, `music_list_jobs`; albüm gösterim adı ve parça katkı index sürümü.

Eski albüm ID'leri, etiket/yayın eşleşmeleri, favoriler, kuyruk/liste üyeliği, konum ve tag/undo günlükleri korunur. Eski birleşik sanatçı alanından yapay çoklu kimlik üretilmez; mevcut müzik dosyaları bounded index worker'ında yeniden okunur. Kullanıcı hacmi erişilemiyorsa kayıt silinmez.

Kod: `MediaStore.MusicCredits/MusicLists/MusicVideos`, `MusicBrainzProvider.Catalog`, `MusicListWorker`, `MusicCatalogEndpoints`, `MusicTagFile`, `LyricsService`, müzik index/import ve React müzik/listeler/oynatıcı yüzeyleri.

## Kaynaklar ve lisans — 2026-10-15/16

- Context7 `/websites/musicbrainz_doc`: artist-credit, entity ilişkileri, video/audio recording ayrımı; public collection lookup/browse/pagination.
- MusicBrainz [Data License](https://musicbrainz.org/doc/About/Data_License) ve [Database](https://musicbrainz.org/doc/MusicBrainz_Database): çekirdek kimlik/artist-credit/ilişkiler CC0. Koleksiyon kullanıcı verisi bu etiketle genellenmez; sağlayıcı atfı ve yeniden dağıtım/ticari kullanım kapısı ayrıca kaydedildi.
- TagLibSharp **2.3.0**, kurulu XML tipleri ve [pinned kaynak](https://github.com/mono/taglib-sharp/tree/b5ae84f2e84087bf160bb0471420200dd2b5d809): `IFileAbstraction`, çoklu sanatçı/MBID alanları, Xiph, AIFF ID3, iTunes; LGPL-2.1-only. Yeni paket bağımlılığı eklenmedi.
- [RFC 3533 §5/6](https://www.rfc-editor.org/rfc/rfc3533): `N×255` paketlerinde sıfır segment, continuation/page sequence/granule ve Ogg CRC `0x04c11db7`; Home'un sınırlı başlık düzeltmesi ve bağımsız bit-düzeyi CRC test oracle'ı.
- [LRC / Enhanced LRC biçimi](https://en.wikipedia.org/wiki/LRC_(file_format)): kelime zamanları. Kaynak söz dataset'i kullanılmadı; native fixture'lar izole test alanındadır.
- Context7 `/tanstack/query`: job durumuna bağlı polling ve sayfalı sorgular; kurulu tipler/üretim build. FFmpeg 9.0.2 native hash/probe.

## Doğrulama ve kabul

**326/326 backend/native, atlanan 0**: `artifacts/test-results/home-0.26.0-full.trx`. **40/40 Node** testi, frontend/worker üretim build ve worker sözdizimi geçti.

- Katkı/erişim/yeniden tarama, tamamlanmamış listeler, restart/iptal/kota/idempotency, API/CSRF, eski metadata önizlemesi/yayınlama engeli doğrulandı.
- Dokuz dosya uzantısında gerçek ses, çoklu sanatçı/MBID, tam tarih, kapak ve ses-paket hash'i; hardlink kaynağının korunması ve exact-byte undo geçti. Ogg sıfır segment/CRC, 255-segment dolu sayfa ve iptal sınırları bağımsız protokol kontrolünden geçti.
- Şema-29 verisindeki albüm ID, takip, liste/kuyruk, konum ve tag/undo günlükleri korunarak **30**'a taşındı; katkı reindex'i aynı albüm ID'sini korudu.
- Windows x64, Linux x64 ve Linux ARM64 için imzalı **0.26.0** paketleri üretildi. **0.25.0/şema 29 → 0.26.0/şema 30 → önceki sürüm/veri** update/rollback geçti; hesap korundu, restore önceki oturumları iptal etti (`artifacts/update-roundtrip-0.25.0-0.26.0.json`).
- Kapanan önizleme kayıtlı 0.25.0 paketiyle yeniden açıldı; imzalı 0.26.0 güncellemesinde sahip oturumu korundu. Canlı **http://localhost:5080**, PID **8228**, release `.data/preview/updates/releases/32af901765ec42dc9d697fd6957910ec`; veri `.data/preview`, şema **30**.
- Canlı yeni müzik istemcisi, katalog kontrolleri ve offline worker sunuluyor; **0 indirme/oynatma/oda, 0 müzik liste işi/video bağlantısı, 0 etkin sentetik kütüphane ve 0 FK ihlali**, VAD CPU kullanılabilir (`artifacts/live-runtime-v026.json`).
- Gerçek MusicBrainz: **Radiohead, 107 albüm grubu; OK Computer, 12 parçalı yayın; Airbag recording `4a7fea2e-545b-4c63-bc9a-9943cc3a29d7` ve bir recording ilişkisi** okundu. OK Computer için ilişki listesi boş döndü ve boş olarak korundu. Geçici 429 yanıtları bounded bekleme/tekrarla geçildi; doğrulama takip, medya edinme veya oynatma başlatmadı (`artifacts/live-music-v026.json`).

Paketler `artifacts/packages/home-0.26.0-{win-x64,linux-x64,linux-arm64}.zip`. Canlı geri dönüş 0.25.0 ve `b3455fcd3ae141dc991cbc4db5d60365.zip` yedeğidir. Linux paketlerinin üretimi runtime/cihaz kabulünün yerine geçmez.

**ACCEPT-26** kullanıcı kabulü:
1. Gerçek ortak/konuk sanatçılı albümde dosya ve MusicBrainz katkılarını, Sanatçılarım gezintisini kontrol et.
2. Kendi çalma listesi veya açık koleksiyondan seçimli toplu takip ve isteğe bağlı yeni-yayın bildirimini dene.
3. Gerçek ELRC/LRC söz dosyasında kelime/satırdan ileri/geri gezinmeyi ve oda kumanda iznini kontrol et.
4. Yerel müzik videosu bağla/izle; ilişkili albümü aç.
5. Yönetilen gerçek Ogg/Opus/WAV/AIFF kopyasında etiket/kapak/adlandırma ve geri almayı kontrol et.

Gerçek kaynakla edinme, cihaz, Linux/Docker ve dağıtım kapıları tam ürün panosunda açıktır.
