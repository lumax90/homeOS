# 0.8 — Müzik kataloğu, yerel albümler ve listeler

Başlangıç: **2026-10-13**, **0.8.0 yayımlandı; backend/native ve canlı metadata doğrulandı**. MUS-01/02/03/06/07 ve CAT-08 temel dilimi.

Hedef: gerçek dosya etiketlerinden sanatçı/albüm/disc/parça/kalite/söz bilgisi; fiziksel dosyadan ayrı MusicBrainz release-group/release/recording kimlikleri; gerçek MusicBrainz sanatçı/album keşfi; kişisel çalma listesi ve kuyruk.

Kaynaklar: MusicBrainz API ve data license resmi belgeleri ve Context7 `/websites/musicbrainz_doc` 2026-10-13'te okundu. Kamu JSON API'si `https://musicbrainz.org/ws/2/`; 1 istek/sn, açık uygulama User-Agent. Core CC0 alanları; supplementary annotation/tag/rating kullanılmaz. Ticari API kullanımı lisans kararı açık kapısıdır.

Yerel isimle oluşturulan albüm kimliği kütüphane/klasör kapsamında kalır. Sağlayıcı MBID'si olmadan aynı adlı farklı albümler global birleştirilmez. Kaynak etiketleri salt okunur; kullanıcı listeleri/metadata eşleşmeleri uygulama verisinde tutulur.

Kanıt: birleşik **98/98 test**, 0 atlanan. Native FLAC etiketleri, Türkçe UTF-8, iki disc, numaralar, sözler, ReplayGain ve salt-okunur kaynak hash'i; özel liste/kuyruk ACL'si ve sıralı güncelleme; LRC offset/çoklu timestamp; MusicBrainz response/paging fixture'ları. Canlı Home API Radiohead araması ve 107 release-group'tan 50 kayıtlık sayfa verdi (`artifacts/live-music-v08.json`). Geliştirme doğrulaması müzik medyası indirmedi.

Kuyruk/listeler 500 medya ve kullanıcı başına 100 liste ile sınırlı; aynı video/audio elementi korunarak sonraki/önceki, karışık ve tekrar kontrolleri eklendi. Gapless/sonic analysis, provider release-track eşleştirme editörü, ses edinme/kalite yükseltme otomasyonu ve etiket yazma henüz tamamlanmadı. Dosya sürümleri albümde fiziksel kayıt olarak gösterilir. Sanatçı listesi metadata favorisidir; yeni albüm edinme komutu değildir.

Windows native stdout/stderr UTF-8 açıklandı; test Türkçe alanlarda önceki kod sayfası hatasını yakaladı. Uygulama içi yedek: `.data/preview/backups/aa2a1b3b4b39459dae8164932be1fd96.zip`. Çalışan yayın `artifacts/server-v0.8`.
