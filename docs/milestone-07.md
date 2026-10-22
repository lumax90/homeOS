# 0.7 — İşletim, yedekleme ve doğrulanmış donanım profili

Başlangıç: 2026-10-13, **0.7.0 yayımlandı ve doğrulandı**. OPS-01/02/03, PLY-04/05.

Yedek: SQLite online `BackupDatabase`, Data Protection anahtarları ve yönetilen altyazılar; kullanıcı medya payload'ları yedeğe dahil değildir. Bütün girdiler için boyut/hash manifesti. Yetki yönetici policy'sindedir.

Geri yükleme: doğrulanmış DB/anahtar/altyazı seti yeni bir `states/{id}` dizinine hazırlanır. Başlangıçta tek aktif-state pointer dosyası atomik değiştirilir. Önceki state korunur; DB ve anahtarlar ayrı ayrı yerinde değiştirilmez. Geri yüklemede oturumlar geçersizleştirilir, transferler duraklatılır, otomasyon kapatılır.

Kaynak: Context7 Microsoft.Data.Sqlite dokümanı bulunamadığı için Microsoft Learn `dotnet/standard/data/sqlite/backup` belgesi 2026-10-13'te okundu. `BackupDatabase` sırasında yazmalar kısa süreli bloklanır.

Ortam GPU'su: NVIDIA GTX 1660 SUPER, sürücü 32.0.16.1742. Kurulu FFmpeg 9.0.2'de `h264_nvenc` gerçek 12-frame/null-output denemesi başarılı. `zscale`, `tonemap`, `scale_cuda` filtreleri kurulu; uygulama profili ve çıktı doğrulaması bu kartta ayrıca izlenir. Bu gözlem başka GPU/işletim sistemleri için destek iddiası değildir.

## Doğrulanan sonuçlar

- Birleşik backend paketi: **94/94, 0 atlanan**; native test ortamı `HOME_TEST_FFMPEG`, `HOME_TEST_FFPROBE`, `HOME_TEST_NVENC=1` ile çalıştı.
- Yedek/state testi DB, kişisel konum ve Data Protection sırrını yeni state'te geri açtı; önceki state korundu. Zip traversal ve hazırlanmış state'in sonradan değiştirilmesi reddedildi.
- GTX 1660 SUPER üzerinde uygulamanın NVENC HLS segmenti gerçek H.264 olarak okundu. PQ/HDR10 ve HLG fixture'larından H.264, 8-bit BT.709 çıktısı doğrulandı. Bu profilde çözme/filtreleme CPU, NVENC kodlama GPU'dadır; tam GPU pipeline iddiası yok.
- FFmpeg 9/libx265 fixture'larında renk bilgisi encoder VUI parametreleriyle doğrulandı; bu nedenle test girişinin gerçekten PQ/HLG olduğu ayrıca kontrol edilir.
- Dolby Vision dönüşümü ve aktarım bilgisi eksik geniş renk alanı otomatik olarak kabul edilmez. Görsel renk doğruluğu, uzun akışlar, fiziksel TV cihazları ve başka GPU/API profilleri kullanıcı/donanım kapısıdır.
- Canlı 0.7: sahip hesap `Home` yönetici olarak korundu, TMDB Başlangıç (27205) gerçek akışı, NVENC/tone-mapping self-test'i ve uygulama içi online yedek başarılı.
- Sunucunun yönetici API'sinden kendini yeniden başlatması doğrulandı: process 11592 → 19484, aynı `http://localhost:5080`, 0.7.0.
- Kanıt: `artifacts/live-runtime-v07.json`, `artifacts/live-backup-v07.json`, `artifacts/live-restart-v07.json`.
- Yükseltme yedeği: `.data/backups/preview-v041-20261013-105732` (DB/WAL + anahtarlar). Yeni uygulama yedeği `.data/preview/backups/9a505f9837214517a9be1f4a6ca91915.zip`.

Çalışan paket: `artifacts/server-v0.7`. Restore UI yalnız doğrulanan state'i hazırlar; admin yeniden başlatması veya süreç yöneticisiyle açılış pointer'ı atomik etkinleştirir. Container için süreç yöneticisi kullanılır. Uygulama yedeği 1 GiB / 50.000 dosya ile sınırlıdır, medya payload'ı içermez. Tam hedef platform paketleri, imzalı güncelleme ve geniş disaster-recovery matrisi açık.
