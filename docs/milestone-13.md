# 0.13 — Aktarım politikaları ve sürüm tercihleri

Başlangıç: **2026-10-13**, **0.13.0 yayımlandı ve doğrulandı**. DL-02/03/04, QUA-03/04/05, IMP-06, OPS-08.

Hedef: kalıcı iş önceliği, global/per-torrent hız, dosya seçimi, seed süre/oran bütçesi ve kullanıcı komutuyla doğrulanmış staging temizliği. Cleanup yalnız ilgili indirme staging alanını kaldırır; yayımlanmış kütüphane dosyaları ve kullanıcı kaynakları korunur.

Gelişmiş release tercihleri: süre sınırlı custom-format regex/codec/HDR/protokol koşulları; minimum skor; PROPER/REPACK ve aynı çözünürlükte gerçek daha iyi sürüm; protokol önceliği ve retention/minimum-age. Karar/payload/intent snapshot'ı ayrımı korunur.

Kaynak kontrolü: Context7 `/alanmcgovern/monotorrent` ve **release-v3.0.2** kaynak/types; `SetFilePriorityAsync`, `PartialProgress`, dosya `BitField`, `UpdateSettingsAsync`, `MaximumDownloadRate/MaximumUploadRate`, `DataBytesSent/DataBytesReceived`. `master` ile ad farkları nedeniyle yayımlanan 3.0.2 türleri esas alındı. Torznab 1.3 spec `seedtype` (`ratio/seedtime/both/either`), `minimumratio`, **saniye** birimli `minimumseedtime` ve volume factor alanları 2026-10-13'te okundu. Saniye eşiği aşağı yuvarlanmadan dakika cinsine çevrilir.

## Kanıt

- Birleşik **139/139 test, atlanan 0**, frontend/PWA üretim build ve imzalı Windows paketi başarılı.
- Gerçek loopback BitTorrent ile yalnız seçilmiş dosya tamamlandı; diğer dosya tamamlanmadan hash doğrulamalı seçili payload yayımlandı. Per-file `High`/`DoNotDownload`, per-torrent/global hız ayarları ve iki protokolün bütçe paylaşımı kurulu 3.0.2 motorunda denetlendi.
- Staging temizliğinden sonra yayımlanmış hardlink/kopya ve kaynak dosya byte'ları değişmedi. Eski komut revizyonu temizlik ve telemetriyi değiştiremiyor; sayaç tekrarları idempotent.
- Kaynak seedtype/oran/süre ve kullanıcı durdurma koşulları; freeleech volume factor normalizasyonu; arşiv/geri çıkarma; regex bütçesi, gerçek codec uyuşmazlığı, retention/protokol ve aynı kalite puanı testleri geçti.
- NNTP hız beklemesi iptal edilebildi; sınırsız yeni ayar sonraki okumada uygulandı. İndirme bütçesi torrent ve NNTP aynı anda çalışırken iki havuza bölünür; NNTP 64 KiB okuma adımlarında sınırlanır. Küçük transport burst'leri ve son gözlem aralığı sayaç hassasiyetinin sınırıdır; tracker hesap bakiyesi iddiası değildir.
- Aktarım kuyruğu öncelikli işe boş slotta başlar. Seçilmiş dosyalar yayımlandıktan sonra dosya seçimi değiştirilemez. Temizlik açık bir kullanıcı komutudur; arşivleme kayıtları/hash/provenance ilişkilerini silmez.
- Canlı yayın uygulama içi güncellemeyle 0.13.0'a alındı: `artifacts/live-runtime-v013.json`; 0 transfer, varsayılan upload 512 KiB/sn, önceki 0.12.0 ve yedek `d1908bb4e0d442798ca666d8969e3f52.zip` korunuyor.

Canlı indexer/NNTP hesabı, özel tracker seed muhasebesi ve uzun transfer/karma protokol performans kabulü açık. Aynı kalite yükseltmesi ayrıca açılır; mevcut sürümün kaynak/kalitesi bilinmiyorsa otomatik olarak daha iyi olduğu varsayılmaz.
