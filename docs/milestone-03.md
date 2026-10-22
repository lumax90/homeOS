# 0.3 — Gerçek keşif, takip, kaynaklar ve doğrulanmış edinme

Doğrulama: **2026-10-12**. Yayın: `artifacts/server-v0.3`, çalışan adres: `http://localhost:5080`.

## Çalışan akış

- TMDB film/dizi trend, popüler, yaklaşan/yayında, yüksek puanlı listeleri ve sayfalı arama; TVmaze anahtarsız dizi kataloğu/arama.
- Dosyasız içerik isteği; sahip hesabının takip listesi, kalite profili, tüm/gelecek/eksik/duraklat kapsamları.
- SQLite'ta kalıcı, revizyonla korunan metadata yenilemesi. Numaralı bölümler, eksik/mevcut/yaklaşan durumları, sezon filtresi, sayfalama ve 30 günlük takvim.
- Torznab/Newznab API bağlantıları; caps, desteklenen dış kimliklerle sorgu ve metin fallback, kategoriler, öncelik, sayfalama, zaman aşımı, Retry-After ve artan backoff.
- Aynı anda en fazla iki arama, arama başına dört kaynak isteği, toplam 60 saniyelik bütçe. Bir kaynağın hatası diğer kaynakların sonuçlarını silmez.
- Açıklanabilir kalite değerlendirmesi: yapım kimliği/yılı, sezon/bölüm, çözünürlük/kaynak/codec, PROPER/REPACK, boyut, seeders, dil ve gerekli/yasaklı/tercihli sözcükler.
- 30 dakika ömürlü, sırları korumalı arama sonuçları ve profil snapshot'ı. API anahtarı veya torrent URL'si yanıt/loglara çıkmaz; release GUID'i hash olarak saklanır.
- Kullanıcının seçtiği kaydedilmiş sonuç → torrent/magnet motoru. Tekrarlanan aynı komut yeni transfer oluşturmaz veya sonradan verilen duraklat komutunu bozmaz. İzinli kaynak dışında yönlendirme ve uyuşmayan infohash reddedilir.
- Kaynak aramasından edinilen payload için gerçek ffprobe kontrolü: dosya adı/yıl/bölüm, gerçek çözünürlük, toplam boyut ve seçili ses dili. Belirsiz/uyuşmayan payload staging'de kalır.
- Doğrulanan dosya/bölüm eşleştirmesi yayından önce kalıcıdır; başarılı tarama sonrasında tek transaction ile kanonik içeriğe bağlanır. Kullanıcının arada yaptığı manuel eşleştirme ezilmez. Başarısız katalog bağlantısı görünür ve yeniden denenebilir.
- PWA manifesti ve PNG simgeleri; build hash'ine bağlı çevrimdışı statik kabuk. API, oturum ve medya cache'e yazılmaz.

## Veri geçişi

`004_product_catalog.sql`: demo kütüphaneleri `enabled=0` ile arşivlenir; kayıtlar, konumlar ve fiziksel kaynaklar korunur. Normal katalog, oynatma ID'si, istatistikler ve iş listelerinde görünmezler. Ayrıca `tracking`, `content_requests`, `quality_profiles`, `indexers`, `release_results`, `acquisition_links`, `acquisition_files` tabloları eklenir.

Yükseltmeden önce çalışan 0.2 durduruldu; DB/WAL ve Data Protection anahtarları birlikte `.data/backups/preview-v02-20261012-223359` altına kopyalandı. Eski yayın ve eski yedekler tutuluyor.

## API ekleri

Tümü kimlik doğrulamalı; mutasyonlar mevcut CSRF sözleşmesini kullanır.

| Yöntem | Yol | İşlev |
|---|---|---|
| GET | `/discovery?provider=tmdb&kind=movie&collection=trending&q=&page=1` | Gerçek katalog sayfası |
| GET | `/discovery/{provider}/{kind}/{id}` | Sağlayıcı ayrıntısı ve yerel kayıt |
| GET | `/content` | Aktif sahip istekleri/takip listesi |
| POST | `/content/requests` | `{provider,kind,providerId,monitorMode,qualityProfileId}` |
| GET | `/content/{id}`, `/content/{id}/files` | İçerik/dosya ilişkisi |
| GET | `/content/{id}/episodes?season=&state=&offset=0&limit=100` | `{items,total,offset,limit,seasons}`; sayfa en fazla 200 |
| PUT | `/content/{id}/tracking` | Kapsam ve kalite profili |
| DELETE | `/content/{id}/request` | İsteği iptal et; dosya/katalog kaydı korunur |
| GET | `/calendar?from=YYYY-MM-DD&to=YYYY-MM-DD&offset=0` | En fazla 93 günlük aralık; sayfa 100 |
| GET/POST/PUT | `/quality-profiles[/{id}]` | Kalite kuralları |
| GET/POST/PUT | `/indexers[/{id}]` | Korumalı kaynak ayarları |
| POST | `/indexers/{id}/test` | caps ve bağlantı doğrulaması |
| POST | `/releases/search` | `{contentId,season?,episode?,profileId?,page:0}` |
| POST | `/releases/{id}/download` | `{overrideRejections:false}`; kullanıcı edinme komutu |
| POST | `/downloads/{id}/link` | Yayımlanmış dosyayı yeniden tara/bağla; transfer başlatmaz |

## Kanıt

- Tam backend paketi: **60 test geçti**. Ardından eklenen üç grab sınırı/idempotency testiyle kaynak grubunun **10 testi geçti**; mevcut ağaçta 63 ayrı test vakası doğrulanmış durumda.
- TypeScript + Vite + PWA üretim derlemesi başarılı. Windows x64 self-contained yayın başarılı.
- Gerçek kullanıcı TMDB token'ı ile canlı okuma: trend listesi 20 film; `27205` **Başlangıç**, IMDb `tt1375666`. TVmaze Sherlock araması 10 sonuç; `335` Sherlock ve `77427` Young Sherlock.
- Kanıt: `artifacts/live-catalog-v03.json`. Canlı normal katalogda 0 sentetik dosya/kütüphane ve 0 indirme.
- Browser/device kabulü kullanıcıda; bu dilimde browser otomasyonu çalıştırılmadı.

## Açık sınırlar

- Kullanıcı henüz indexer seçmediğini bildirdi. Znab/kimlik/grab akışı HTTP fixture'larıyla; BitTorrent motoru izole loopback eşleriyle doğrulandı. Gerçek indexer hesabı ve kullanıcının seçeceği internet transferi açık kabul kapısıdır.
- Newznab araması vardır; NNTP/NZB aktarımı yoktur. İndirme düğmesi bu sonuçlarda etkin değildir.
- Otomatik RSS/edinme/yükseltme, cutoff/delay/custom-format motoru, çok kullanıcı/istek onayı henüz uygulanmadı.
- S01E01 ve 1x01 tekil dosyalar desteklenir. Çok bölümlü tek dosya, mutlak anime ve günlük numaralandırma otomatik import'ta tahmin edilmez. Filmde birden fazla ana video manuel inceleme gerektirir.
- Dosya adı/metadata/probe eşleştirmesi, dosya içeriğinin anlamsal olarak doğru film olduğunu kanıtlayan bir fingerprint sistemi değildir.
- API anahtarı dışında cookie/oturum gerektiren özel kaynak adaptörleri; sağlayıcılar arası içerik alias birleştirme; sezon/bölüm bazında ayrı kullanıcı isteği sonraki kartlardır.
- PWA'nın bu dilimi çevrimdışı kabuktur; çevrimdışı film depolama ve cihaz kabulü açık.
