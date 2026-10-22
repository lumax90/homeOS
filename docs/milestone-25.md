# 0.25 — Bölüm kimliği onarımı, alternatif sıralar ve dosya sınırları

Tarih: **2026-10-15**, çalışan yayın **0.25.0**. Gereksinimler: **CATALOG-04 / CAT-04/07, PLY-07**, ilişkili **CAT-11, REQ-01/02, ACQ-01/02, IMP-03, PLY-09, USR-02, OPS-01/03**.

## Kullanıcı akışı

### Sağlayıcının bölüm sırası değiştiğinde

İçerik ayrıntısındaki **Katalog kimlikleri ve alternatif adlar → Sağlayıcı bölüm değişikliklerini incele** güncel sağlayıcı listesinden bir onarım planı oluşturur. Eski/yeni sezon-bölüm, yeni kimlikler, arşivlenecek kayıtlar ve çakışmalar görünür.

- Sağlayıcı bölüm ID'si aynıysa kalıcı Home bölüm ID'si korunur; yer değiştiren numaralar tek transaction içinde güncellenir.
- Aynı numarada farklı sağlayıcı ID'si varsa kullanıcı **eski kimliği koru** veya **yeni bölüm oluştur** seçer. Bu seçim doğrulanmadan uygulanmaz.
- Yeni bölüm seçimi eski dosyayı yeni yapım/bölüm gibi göstermez. Eski bölüm arşivde, dosya ve geçmiş eski kimliğinde kalır.
- Eski sağlayıcı ID'si açıkça değiştirildiyse sonraki gecikmiş yanıt onu geri yazamaz. Aktif transfer/import bulunan içerikte yapısal onarım bekletilir.
- Plan 30 dakika, oluşturan yönetici ve katalog revizyonuna bağlıdır. Aynı uygulanmış planın aynı seçimlerle tekrarı idempotenttir; farklı veya eskimiş seçim reddedilir.

### Bir sağlayıcı bölümü birkaç kanonik bölüme karşılık geldiğinde

Kimlik birleştirme ekranında her kaynak bölümüne bir veya birden fazla kanonik karşılık seçilir. Dosya üyeliği ve istek kapsamı bu ID listesine taşınır. Ayrıca bağlı sağlayıcının sezon/bölüm karşılıkları ayrı panelden düzenlenebilir.

- Kaynak bölümüne göre başlangıç/bitiş saniyeleri belirtilmişse kısmi konum ve dosya zamanları bu sınırlarla taşınır.
- Zaman bilinmiyorsa dosyanın kendi devam konumu korunur; eksik bilgiyle hedef bölümlere süre bölüştürülmez. Tam izlenmiş kaynak durumu korunur. Eski kaynak geçmişi kayıtlıdır.
- Kaynakta saklanan alternatif sıralar da ID karşılıklarıyla ana kataloğa taşınır. Her dosya ve sıra girdisi en fazla 20 kanonik bölüm içerir.
- Aynı kanonik bölümün birkaç ayrı kaynak bölümüne atanması, çelişen aynı-sağlayıcı yapım kimlikleri veya çözülemeyen arşivlenmiş kimlik bağımlılıkları açık çatışmadır. Mevcut kayıtlar korunarak işlem reddedilir.
- Sonradan düzenlenen bağlı sağlayıcı karşılığı yeni dosya eşleştirmelerinde kullanılır. Mevcut dosya üyeliği dosya ayrıntısından açıkça düzenlenir.

### DVD/dijital ve özel sıralar

**Bölüm numaralandırması → DVD, dijital ve alternatif sıralar** altında sıralar adlandırılarak saklanır. Elle `kaynakSezon:bölüm=kanonikSezon:bölüm[,karşılık]` girilebilir. TMDB DVD, Digital, Original air date, Story arc, Production ve TV grupları aktarılabilir; mevcut mutlak numara akışı ayrıca korunur.

TMDB grup sırası bir sezon numarası sayılmaz: kullanıcı her gruba dosyalarda kullandığı kaynak sezonunu atar. Özel bölümler için 0 kullanılabilir. Grup üyeliği, `show_id`, kaynak episode ID, sıralı ordinal, toplam sayılar ve snapshot fingerprint'i doğrulanır.

**Bu sırayı kullan** seçimi:
1. Dosya eşleştirme ve otomatik eşleştirmeye,
2. Znab kaynak aramasının sezon/bölüm parametrelerine,
3. Release kimlik değerlendirmesine,
4. Edinme anında dondurulan import numaralandırma snapshot'ına uygulanır.

Kanonik katalog ve kişisel bölüm geçmişi kalıcı bölüm ID'sinde kalır. Kaynak sırası düzenlenirse eski aramalar geçersiz olur; başlamış edinmenin snapshot'ı korunur. Arşivlenmiş hedef içeren sıra açıkça geçersiz gösterilir ve yeni arama/edinmede kullanılmaz. Eski ID'ler editörde `#id` ile korunur; aynı numaradaki yeni bölüme sessizce dönüştürülmez.

### Çok bölümlü dosyada otomatik sınır önerisi

**Dosyanın bölüm kapsamı ve zamanları → Dosyadan bölüm sınırlarını öner** kalıcı bir yerel analiz işi oluşturur.

- Önce `S01E01` veya `Episode 1` / `Bölüm 1` gibi tekil bölüm sahne işaretleri aranır.
- Yeterli işaret yoksa FFmpeg ile düşük çözünürlüklü siyah görüntü ve sessizlik analizi yapılır. Ses yoksa siyah sınırın sahne işaretiyle örtüşmesi gerekir.
- Sağlayıcı süreleri veya eşit aralıklar yalnız arama penceresidir. Öneri gerçek sinyal gerektirir; benzer güçlü adaylar varsa sonuç belirsizdir.
- Öneri kullanıcıca uygulanır. Dosya fingerprint'i, bölüm kimlikleri ve eşleşme revizyonu tekrar kontrol edilir. Kaynak dosya değiştirilmez.
- Yeni zamanlar aynı transaction içinde kişisel ham dosya konumundan bölüm konumlarına yansıtılır; başka dosyadan daha yeni izleme durumu ezilmez. Dosyanın %95 izlenmiş olması son bölümün otomatik tamamlanmış sayılması için kullanılmaz.

## Veri ve işlem güvenliği

- Bölüm ID'si ile numarası ayrıdır. `episodes.retired` eski ID/numara geçmişini saklar; aktif numaralar tekildir.
- İstek kapsamı bir bölüm bölündüğünde veya sezonlara dağıldığında açık bölüm-ID listesine dönüşebilir. Aynı kullanıcının eşdeğer isteği birleştirilir; numara takası sırasında iki farklı istek yanlışlıkla birbirine birleştirilmez.
- Kanonik değişiklikte katalog/eşleşme revizyonları ilerler. Eski kaynak sonucu, otomasyon, oynatma, altyazı ve offline eşleşme komutlarının önceki kimliğe yazması engellenir. Onarım sonrası otomasyon kullanıcıca yeniden etkinleştirilir.
- Snapshot veya sağlayıcı sorgusu başarısız olduğunda önceki katalog korunur. Boş sağlayıcı listesi otomatik toplu arşivleme nedeni değildir; kısmi/tekrarlı sezon yanıtları reddedilir.
- Planlar en fazla 64 etkin; eski plan geçmişi en fazla 256 kayıt/7 gün. Dizi başına 20 sıra, sırada 20.000 kanonik bölüm; API düzenleme gövdesi 4 MiB.
- Bölüm sınırı analizinde tek worker, 32 bekleyen/çalışan iş, 2–20 bölüm, en fazla 8 saat kaynak ve 3 dakika işlem bütçesi vardır. İşler revision/lease taşır; iptal ve restart eski sonucu uygulamaz. Yalnız Home işaretli analiz cache'i temizlenir; PID yeniden kullanımı start-time/executable ile ayırt edilir.

## Şema ve kod

Şema **28 → 29**, `029_episode_catalog.sql`:

- `episodes` tablosu kalıcı ID/composite foreign key korunarak aktif-numara indeksi, runtime ve retired alanlarıyla yeniden kurulur.
- `episode_alias_targets`, `request_episode_scope`, `episode_refresh_plans`, `catalog_episode_orders`, `episode_boundary_jobs`.
- Mevcut alias'lar bire-bir hedeflere taşınır; mevcut dosya, kişisel konum ve mutlak sıra kayıtları korunur. Foreign key kontrolü migration sonunda zorunludur.

`MediaStore.EpisodeCatalog`, `EpisodeMerge`, `EpisodeRefresh`, `EpisodeOrders`, `EpisodeBoundaries`; `EpisodeCatalogEndpoints`, `EpisodeBoundaryService/Detector`; metadata/parser, kaynak arama, import ve React katalog/numaralandırma/dosya yüzeyleri.

## Doğrulama

Odaklı kontrollerde birden-çoğa eşleme, ham/kanonik konum, istek kapsamı, provider ID değişimi/numara takası, eski kimlik reddi, source-order snapshot, arşivlenmiş hedef, native siyah/sessizlik sınırı, son bölüm watched eşiği, API yetki/CSRF ve composite-FK migration geçti.

- **296/296 backend/native, atlanan 0**, `artifacts/test-results/home-0.25.0-full.trx`; **38/38 Node** kontrolü, frontend/worker üretim build ve worker sözdizimi başarılı.
- Gerçek yerel video/seste siyah görüntü/sessizlik sınırı bulundu; uygulama, ham dosya konumunu doğru bölüm konumuna çevirdi ve kaynak hash'ini korudu. Belirsiz sinyal veya eşit bölme varsayımıyla sınır uygulanmadı.
- Şema-28 veritabanından composite foreign key, alias, medya üyeliği, kişisel konum ve mutlak sıra kayıtlarının korunumu doğrulandı.
- Windows x64, Linux x64 ve Linux ARM64 imzalı **0.25.0** paketleri üretildi. **0.24.0/şema 28 → 0.25.0/şema 29 → önceki veri/sürüm** update/rollback geçti; hesap korundu, restore eski oturumları iptal etti (`artifacts/update-roundtrip-0.24.0-0.25.0.json`).
- Canlı önizleme **0.25.0 / şema 29**. Sahip oturumu korundu; yeni katalog istemcisi ve önceki offline worker sunuluyor. **0 bölüm yenileme planı/sıra/sınır işi, 0 indirme/oynatma/oda, 0 etkin sentetik kütüphane ve 0 FK ihlali**; VAD CPU kullanılabilir (`artifacts/live-runtime-v025.json`). Doğrulama canlı katalog değiştirme, medya indirme veya oynatma başlatmadı.
- Gerçek kullanıcı kataloğu/sağlayıcı sırası ve cihaz sonuçları **ACCEPT-25**; Linux/Docker runtime ve genel dağıtım kapıları mevcut kartlarda açıktır.

## Kaynaklar — 2026-10-15

1. Context7 `/websites/developer_themoviedb`: TV episode groups ve details; tipler 1–7, `groups.order`, `episodes.order`, `show_id`, runtime ve provider episode ID. Resmî örnekteki tekrarlı/çelişkili kayıtlar veri doğrulamasından muaf tutulmadı.
2. Context7 `/websites/ffmpeg_documentation`: `blackdetect` / `silencedetect`, info seviyesinde başlangıç/bitiş logları, null muxer. Kurulu FFmpeg 9.0.2 ile gerçek yerel video/ses fixture'ı.
3. Mevcut React/Query/HeroUI kurulu tipleri ve üretim build; yeni runtime kütüphanesi veya dataset benimsenmedi. Sağlayıcı ve FFmpeg lisansları mevcut envanterdedir.

## Kullanıcı kabulü — ACCEPT-25

1. Gerçek dizide sağlayıcı değişikliklerini incele; eski/yeni kimlik ve istek kapsamını kontrol et. Onarım sonrası dosya ve konum korunmalı.
2. Farklı sağlayıcının birleşik bölümünü birkaç kanonik bölüme eşleştir; eski dosyada devam et ve yeni bir eşleştirmeyi dene.
3. DVD/dijital sıra aktar veya elle kaydet; etkin seçimin dosya eşleştirme ve kaynak aramasında doğru numaraları kullandığını kontrol et.
4. Çok bölümlü gerçek dosyada sınır önerisini izle/ileri sararak kontrol et; belirsiz adaylarda manuel aralıkları kullan.

Gerçek kaynak edinmesi, kullanıcı cihazı, Linux/Docker ve diğer tam ürün kapıları çalışma panosunda sürer.
