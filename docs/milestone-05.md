# 0.5 — Hane hesapları, yetkiler ve istek akışı

Başlangıç / kaynak kontrolü: **2026-10-13**. Durum: **0.7.0 içinde yayımlandı; backend doğrulandı, browser kabulü açık**.

- USR-01/02: sahip/yönetici, üye ve izleyici; hesap açma/davet, devre dışı bırakma, parola ve oturum iptali; tüm veya seçili kütüphanelere erişim.
- PLY-09: kullanıcıya ait konum/izlenme; aynı içerik/bölümün dosya sürümlerinde ortak içerik konumu.
- REQ-01/02/03: kişi ve kapsam bazında istek, onay/ret/iptal, aylık kota, otomatik onay tercihi ve kalıcı olay geçmişi.
- OPS-07: özel bildirim kutusu; istek/edinme durumlarından idempotent bildirimler.

Yetki yalnız arayüzde uygulanmaz: katalog sayfalama/sayımlar, dosya/afiş/altyazı/range ve HLS segmenti dahil bütün okuma yolları kütüphane sınırını kullanır. Yönetim komutları ayrı policy ile korunur. Kullanıcı kimliği HTTP gövdesinden alınmaz.

Cookie bir sunucu oturum kimliği ve hesap revizyonu taşır. Oturum/hesap değişikliği her yeni HTTP isteğinde kontrol edilir. Eski 0.4 cookie'leri yükseltmeden sonra yeniden giriş gerektirir; hesap hash'i, metadata sırları ve geçmiş taşınır.

Kaynaklar: Context7 `/dotnet/aspnetcore.docs`, cookie `ValidatePrincipal/RejectPrincipal` ve Minimal API role-policy belgeleri 2026-10-13 tarihinde okundu. Yüksek entropili davet token'ının yalnız hash'i saklanır; token tek kullanımlı ve süre sınırlıdır.

Kanıt: `HouseholdTests` altı senaryo; izinli sayımlar/versiyonlar, gizli dosya/afiş/altyazı/stream engeli, kişisel ve sürümler arası konum, kota/onay/özel bildirimler, revokasyon, atomik davet tüketimi ve sezon kapsamları. Toplam birleşik paket 94/94 geçti. Yaş sertifikası/PIN, tüm protokollerde kullanıcı eşzamanlı oynatma kotası ve harici kimlik sağlayıcıları kapsamda açık.
