# 0.15 — Müzik yayın kimliği ve albüm sürümleri

Başlangıç: **2026-10-13**, **0.15.0 yayımlandı; backend/native ve canlı metadata doğrulandı**. MUS-02/03/05/06, IDX-03, ACQ-01, IMP-02/03.

MusicBrainz release-group, release ve recording farklı kimlikler olarak saklanır. Yerel albüm bir release'e açık kullanıcı seçimiyle eşlenir; disc/track ve süre/başlık uyuşmazlıkları görünür kalır. Kaynak etiketleri salt okunur. Cover Art Archive proxy'si resim kaynağı/redirect/yanıt boyutu sınırıyla çalışır; görüntü hakları CC0 metadata lisansından bağımsızdır.

Müzik yayını → caps tabanlı `t=music` veya `search/q` → açıklanabilir ses kalite kararı → korumalı seçilmiş sonuç → ortak BitTorrent/NZB motoru → gerçek probe/etiket/recording doğrulaması → ayrı yönetilen `downloads/music-library` → tarama ve MusicBrainz kimlik bağlantısı. Yanlış albüm/recording, eksik gerekli parça ve gerçek codec uyuşmazlığı staging'de durur. Sonradan seçilen import profili, ilk release kurallarını değiştirmeyen ayrı snapshot'tır.

Kaynak: Context7 `/websites/musicbrainz_doc` release lookup (`recordings+artist-credits+release-groups`), release-group browse ve Cover Art Archive API belgeleri 2026-10-13'te kontrol edildi. Kamu API'sinin 1 istek/sn sınırı ortak provider kuyruğunda korunur. CAA proxy yalnız HTTPS CAA/Archive.org hostlarına gider; API sırları taşınmaz. Görsel cache 512 MiB/2000 dosya soft limitlidir, 404 sonuçları 15 dakika tutulur.

## Doğrulama ve yayın

- **151/151 backend/native test, 0 atlanan**; frontend üretim build ve imzalı Windows x64/Linux x64/Linux ARM64 paketleri üretildi. Linux paketlerinin cihaz üzerinde çalıştırma kabulü açık.
- Release-track ile recording ID ayrımı, çok disc/vinil parça numarası, yanlış başlık için açık eşleştirme, kaynak etiketlerini koruma, rescan'de kullanıcı eşleşmesini koruma, kütüphane ACL'si ve CAA redirect/cache sınırı fixture'larla doğrulandı.
- Native FFmpeg FLAC paketi; yanlış release-group reddi, ses/etiket/recording doğrulaması, ayrı müzik kütüphanesine atomik yayın, tarama ve kalıcı MBID bağlantısı testinden geçti. Kaynak dosya hash'leri değişmedi. Znab `t=music` capability ve sıfır transferli kaynak araması ayrıca doğrulandı.
- Canlı Home API, Radiohead **OK Computer** için **39 yayın**, seçilen `0b6b4ba0-d36f-47bd-b4ea-6a5b91842d29` yayınında **12 parça** döndürdü. Airbag'in release-track ve recording ID'leri ayrı döndü (`artifacts/live-runtime-v015.json`). Canlı medya transferi başlatılmadı; download sayısı 0.
- Gerçek süreç **0.14.0/şema 15 → 0.15.0/şema 17 → eski sürüm + eski veri** geri dönüşü geçti (`artifacts/update-roundtrip-0.14.0-0.15.0.json`). İlk test yükleme denemesi Windows PowerShell HTTP bağlantısında istemci tarafında kesildi; büyük paket doğrulama betiği yeni bağlantı kullanacak şekilde düzenlendi ve roundtrip tamamlandı. Üretim yükseltmesi aynı paketle başarılıydı.
- Çalışan önizleme `http://localhost:5080`, 0.15.0; önceki 0.14.0 ve `981876c756cd419dbc8b26dc225be694.zip` state yedeği uygulamanın geri dönüş kaydında.

Albüm listesi metadata favorisidir. Sanatçıdan otomatik yeni-albüm edinme, ses profiline göre otomatik sürüm yükseltme, gapless/sonic analiz ve yönetilen etiket yazma bu dilimde tamamlanmış sayılmaz. Bunlar kapsam panosunda açık kalır. Canlı müzik indexer/transfer kabulü kullanıcı kaynağına bağlıdır.
