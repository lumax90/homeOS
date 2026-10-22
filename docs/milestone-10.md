# 0.10 — Dahili Usenet aktarımı ve paket bütünlüğü

Başlangıç / kaynak doğrulaması: **2026-10-13**, **0.10.0 yayımlandı; backend/native doğrulandı**. DL-05/06, IDX-03/05, IMP-01/02/03.

- Home'un kendi NNTP istemcisi: TLS veya STARTTLS, sunucu sertifikası/hostname doğrulaması, AUTHINFO, message-id BODY, dot-unstuffing, bağlantı/yanıt sınırları ve iptal.
- NZB XML'i dış varlık çözmeden ayrıştırılır. Parçalar yEnc boyut/offset/CRC ile doğrulanır; kalıcı SHA-256 parça makbuzları restart sırasında yeniden doğrulanır. Kaynak mesaj kimlikleri ve parolalar korumalı saklanır.
- Sunucu önceliği, sınırlı bağlantılar ve eksik article için sonraki kaynağa geçiş. Başarısız ve eksik parçalar anlaşılır sonuç verir.
- PAR2 yalnız yönetilen staging alanında; paket içi dosya adları onarımdan önce doğrulanır. Onarım sonucu tekrar boyut/hash kontrolüne girer.
- RAR/ZIP/7z açımı SharpCompress ile sınırlandırılmış ve iptal edilebilir akışlardan; mutlak/üst dizin/bağlantı/çakışma girdileri reddedilir. Aynı doğrulama ve atomik import motoru kullanılır.

Kaynaklar: RFC 3977 (NNTP BODY/multiline), RFC 4642 (STARTTLS), RFC 4643 (AUTHINFO); TLS cipher seçimi eski RFC örnekleri yerine .NET/işletim sistemi güncel varsayılanlarıdır. SharpCompress Context7 `/adamhathcock/sharpcompress` API/ReaderOptions belgeleri ve **0.50.5** paketi (MIT, 2026-10-09, commit `e4193af83b6ec8bdd5fb4f6ebffc2315dd3fc626`). NuGet'teki 1.0.0 kaydının **unlisted** olduğu registration metadata'sından görüldü; güncel aktif/tag'li 0.50.5 seçildi ve API'ler bu sürüme göre kullanıldı. par2cmdline v1.4.0 resmi Parchive yayını. Kaynak/veri lisansları `docs/dependencies.md` altında.

Geliştirme internet medya indirmesi yapmaz. Protokol/yEnc/NNTP kontrolleri loopback sunucuyla, onarım ve arşiv kontrolleri küçük izole fixture'larla yapılır. Gerçek Usenet hesabı/retention/uzun paket kabulü kullanıcı kaynağına bağlıdır.

## Kanıt ve sınırlar

- Birleşik **115/115 test, 0 atlanan**; frontend/PWA üretim build ve Windows x64 self-contained publish başarılı.
- 12 Usenet senaryosu: XML/komut/yol reddi, yEnc 256-byte/escape/CRC, TLS ve STARTTLS'te sertifika reddi + güvenilen test zinciri, eksik parçadan kalıcı devam/idempotent import, kaynak fallback, bloke BODY iptali, ZIP traversal, parola korumalı solid RAR, PAR2 yol doğrulaması, gerçek bozuk bloğun native onarımı ve ilişkileri koruyan migration.
- `012_usenet` migration'ı download tablosunu foreign keys kapalı transaction'da yeniden kurar; commit öncesi `foreign_key_check` çalışır. Var olan indirilenler/hash/provenance ilişkileri korunur.
- Yeni NNTP sunucuları Kaynaklar → Usenet / NNTP ekranında; NZB elle veya Newznab sonucundan seçilir. Sunucu parolası ve NZB'nin içindeki özel mesaj/parola alanları Data Protection ile korunur.
- Varsayılan sınırlar: 8 MiB NZB, 20.000 dosya/200.000 segment, sunucu başına 20 ve iş başına 16 bağlantı, 256 GiB payload (yapılandırılabilir), 16 MiB decoded segment; arşiv açımı ve PAR2 20.000 dosya/boyut sınırına tabidir. PAR2 aracı 256 MiB/2-thread ve süre limitleriyle çalışır.
- Aynı yEnc dosyasındaki örtüşen/parça adı çelişkileri ve doğrulanmamış CRC reddedilir. Eksik PAR2 cildinin içindeki kısmen sağlam paketlerden kurtarma, obfuscated/missing ilk dosya adlarının her çeşidi, RAR/7z'nin tüm varyantları ve NNTP uzun süreli performansı henüz geniş kabul matrisiyle doğrulanmadı.
- Aktif yayın `artifacts/server-v0.10`, port 5080. Canlı kanıt `artifacts/live-runtime-v010.json`: 0 download/0 NNTP hesabı, harici PAR2 kullanılabilir, SharpCompress 0.50.5.
- Yükseltme yedeği `.data/preview/backups/192047b99aa1429bbef104898cc84909.zip`; çalışan DB şema 12. 2026-10-13 ortam yeniden başlatmasının ardından eski tamamlanmış işler tekrar uygulanmadan yayın yeniden açıldı.
