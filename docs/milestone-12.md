# 0.12 — Ebeveyn profilleri ve kişisel katalog

Başlangıç: **2026-10-13**, **0.12.0 yayımlandı; backend ve canlı metadata doğrulandı**. USR-02, REQ-03/04, CAT-05/08, PLY-09.

Kütüphane iznine ek olarak içerik/dosya bazlı yaş ve engelleme; bilinmeyen sınıflandırma politikası; sunucu tarafı sayım/dosya/iz koruması. Kısıtlı profiller transfer yönetim yetkisi alamaz. Sağlayıcı sınıflandırması ile yöneticinin yerel yaş tercihi ayrı tutulur.

PIN geçişi yalnız yönetici tarafından güvenilir kılınan cihazdaki seçili hesaplara açıktır. Cihaz ve hedef hesap revizyonları, süre sınırı, başarısız PIN beklemesi ve revokasyon sunucuda saklanır. Tam hesap girişinin yerini genel bir PIN endpoint'i almaz.

Kişisel favori/puan ve koleksiyonlar; yerel başlık/açıklama/yaş/etiket override'ları sağlayıcı yenilemesinde korunur. Hiçbiri kaynak medya dosyalarını değiştirmez.

Kaynak: TMDB release_dates/content_ratings `append_to_response` sözleşmesi Context7 `/websites/developer_themoviedb` ile 2026-10-13 tarihinde okundu. Sayısal ve açık yaş tanımları normalize edilir; bilinmeyen sertifika otomatik güvenilir sayılmaz.

## Doğrulama

- Birleşik **132/132 test, atlanan 0**, frontend üretim build ve imzalı Windows paketi başarılı.
- Altı yeni senaryo: katalog/sürüm/sayı/afiş/stream/metadata yaş sınırı, profil değişimindeki eski progress, korunan yerel metadata, kişi bazlı koleksiyon/favori/puan, güvenilen cihaz/PIN ve oturum iptali, kalıcı PIN deneme sınırı, hesap revizyonu ve sınıflandırma sözlüğü.
- İmzalı süreç roundtrip'i **0.11.1 (şema 12) → 0.12.0 (şema 13) → 0.11.1 + şema 12 yedeği** olarak geçti (`artifacts/update-roundtrip-0.11.1-0.12.0.json`).
- Canlı sunucu uygulama içi imzalı güncellemeyle yükseltildi. `artifacts/live-runtime-v012.json`: 0 transfer, sahip sınırsız yönetici, PIN atanmadı, koleksiyonlar boş. TMDB `27205` gerçek yanıtında **TR: 13+** sınıflandırması doğrulandı.
- Önceki sürüm/backup uygulamada geri dönüş hedefi olarak saklandı: 0.11.1, `.data/preview/backups/01d5e79833c24e218cf012c0adf51069.zip`.

PIN hash'leri dışarı verilmez. Cihazın kaldırılması bağlı oturumları da kapatır. Parola/hesap/PIN revizyonu grant'i geçersizleştirir. Beş başarısız PIN denemesi 15 dakikalık kalıcı bekleme üretir. Genel giriş hâlâ hesap parolasıyla yapılır. Profil geçişinde eski oynatıcı/queue güncellemeleri beklenen kullanıcı kimliğiyle fenced edilir; HTTP gövdesindeki kullanıcı kimliği hedef kullanıcı olarak kullanılmaz.

Yaş modeli sınıflandırması bilinmeyen içerik için ayrı izin kullanır. TMDB TR/US/GB'deki açık yaş eşikleri desteklenir; PG gibi tekil yaş belirtmeyen etiketler `unknown` kalır. TVmaze/MusicBrainz verisinin yaş sınıflandırması yoksa bu politika geçerlidir. Kişi başına 100 koleksiyon, koleksiyon başına 1000 içerik; favori görünümü 500 kayıtla sınırlı. Kişi/oyuncu gezinmesi, gelişmiş koleksiyon kaynağı listeleri ve tüm protokollerde kullanıcı oynatma/edinme kotası ayrı kartlarda sürer.
