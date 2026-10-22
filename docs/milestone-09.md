# 0.9 — Altyazı sağlayıcısı ve dil profili

Başlangıç: 2026-10-13, **0.9.0 yayımlandı; backend/native fixture doğrulandı**. SUB-02/03/04/05.

OpenSubtitles.com REST v1: uygulama API-key, kullanıcı JWT'si, API/VIP host seçimi, parent IMDb/TMDB + bölüm parametreleri, little-endian moviehash, dil/forced/HI filtreleri ve açıklanabilir eşleştirme puanı.

Şema kaynağı: OpenAPI 1.0.1 (MIT işaretli) https://github.com/soenneker/soenneker.opensubtitles.openapiclient/blob/main/openapi.json ; Context7 ilgili istemci belgeleri. Resmi Stoplight kök adresi 404, consumer sayfası 403 döndü. Kamu `/infos/formats` isteği JSON/SRT/WebVTT biçimlerini doğruladı. Şema search → `files.file_id` → `/download` akışını ve API-key + Bearer zorunluluğunu açıklar. Login'in döndürdüğü host yalnız iki bilinen API hostundan kabul edilir.

Geçici download URL'si istemciye verilmez. İndirme ayrı, credentials taşımayan HTTPS isteğidir. Sonuç ve yerel payload metadata/fingerprint ile bağlıdır. Yeniden aynı seçimin uygulanması hazır yönetilen altyazıyı döndürür; kaynak medya/yan dosyaları yazılmaz.

Varsayılan otomasyon kapalıdır. Geliştirme gerçek internet altyazısı indirmesi başlatmaz; hesap ve quota akışları HTTP fixture'ları ile doğrulanır. Yetkili canlı sağlayıcı testi kullanıcı API-key/hesabını bağladığında yapılır.

Kanıt: birleşik 103/103 test, atlanan 0. Moviehash, VIP parent/bölüm araması, şifreli sırlar, credentials taşımayan dosya isteği, yinelenen seçim/kota ve uçuş sırasındaki profil değişiminin yayını engellemesi doğrulandı. Üretim frontend ve Windows x64 self-contained build geçti. `artifacts/live-runtime-v09.json`: 0 indirme, sağlayıcı bağlantısı yok, otomasyon kapalı. Çalışan paket `artifacts/server-v0.9`; geçiş yedeği `.data/preview/backups/07a45188639545edae788f76761a0eee.zip`.

Kullanıcı OpenSubtitles hesabını açtığını bildirdi; API Consumers → New Consumer → HomeMediaServer adımları resmi yardım merkezi kaynağıyla iletildi. Sırlar uygulama ekranından girilir. Medya override, otomatik zaman eşleme, daha iyi altyazıya yükseltme ve diğer sağlayıcılar henüz bu dilimde değildir.
