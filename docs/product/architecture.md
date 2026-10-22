# Ürün mimarisi ve ortak kurallar

## Ana akış

```text
Gerçek sağlayıcı kataloğu / yerel dosyalar
  → içerik kimliği ve kullanıcı isteği
  → takip kapsamı / yayın ve eksik durumları
  → kaynakların yeteneklerine göre arama
  → release ayrıştırma / profil değerlendirmesi / açıklanabilir retler
  → kullanıcı edinme komutu (ve kullanıcı etkinleştirirse otomasyon)
  → protokol motoru / doğrulama / onarım
  → kimliğe bağlı kontrollü import
  → altyazı ve medya izleri
  → istemciye uygun oynatma
```

## Modüller ve veri sahipliği

| Modül | Sahip olduğu bilgi | Temel komut/olay |
|---|---|---|
| Identity | kullanıcı, rol, oturum, cihaz yetkisi | hesap oluştur/değiştir, oturum iptal |
| Catalog | içerik, provider alias, sezon/bölüm, sanatçı/albüm/parça, sürüm, koleksiyon | keşfet, eşleştir, düzelt, birleştirme öner |
| Library | fiziksel dosya, kök, erişim, izler, probe sonucu | tara, yeniden bağla, yönetilen yayın |
| Requests/Tracking | istek, izleme kapsamı, profil, eksik/yaklaşan birimler | iste, onayla, takip et, kapsam değiştir |
| Sources | capability, kategori, kimlik bilgisi, kota, sağlık | bağla, test et, sorgula, devre dışı bırak |
| Quality | release parser, profil, custom format, cutoff/delay | değerlendir, sırala, ret nedenlerini ver |
| Acquisition | release provenance, arama sonucu, kullanıcı seçimi, blocklist | edinme komutu oluştur, tekrar ara |
| Download budgets | aktarım sahibi, aylık kabul/byte borcu, yürütme rezervasyonu, kullanım makbuzu | bütçe ayarla, yer ayır, başlat, beklet, tüketimi göster |
| Transfers | BT/NNTP protokolü, byte/parça doğrulaması, seed/resume | indir, duraklat, devam, iptal |
| Import | manifest, dosya/istek eşleştirmesi, hardlink/copy, karantina | doğrula, yayımla, sonucu bağla |
| Subtitles | dil profili, sağlayıcı sonucu, altyazı izleri/puanı | bul, indir, senkronla, yükselt |
| Playback | session/device profile, stream kararı, FFmpeg işi, segment cache | oynat, seek, iz değiştir, durdur |
| Watching | hesap daveti, ortak zaman çizelgesi, oda üyeliği, komut receipt'i | odaya katıl, hazır ol, ortak oynat, kişisel uzak kumanda |
| Operations | kalıcı işler, geçmiş, yedek, güncelleme, bildirim | görevi yürüt, teşhis et, yedekle |

Modüler monolit korunur. Codec/protokol/format kütüphaneleri yardımcı araçlardır; iş akışı ve kalıcı sonuçlar Home'a aittir.

## Kimlik ve durum

- İçerik bir dosya değildir. Aynı yapım birden fazla sürüm/dosya içerebilir; bir dosya birden fazla bölüm içerebilir.
- Provider ID tür/providera göre namespaced olur. IMDb/TMDB/TVmaze/TVDB/MusicBrainz alias'ları doğrulanmış kaynakla ilişkilendirilir.
- Müzikte albüm/parça sanatçı katkıları ID, credited name, sıra ve joinphrase ile modellenir; kaynak etiket ve açık yayın eşlemesi ayrı provenance taşır. Toplu liste aktarımı sahip/hesap sürümü/cursor/lease ve seçim receipt'i taşır; veri bütünü doğrulandıktan sonra seçilen takipler atomik uygulanır (0.26).
- Bölüm ID'si numaradan bağımsızdır. Sağlayıcı numara/ID değişimi önizlemeyle onarılır; arşivlenen bölümün dosya/geçmişi korunur. Bir sağlayıcı bölümü birden çok kanonik bölüme karşılık gelebilir. DVD/dijital sıra kaynak koordinatlarını kanonik ID'lere bağlar; edinme bu eşlemenin snapshot'ını taşır (0.25).
- Fuzzy başlık benzerliği tek başına kalıcı kimlik birleştirme nedeni değildir.
- Kullanıcı isteği, mevcut fiziksel dosyadan bağımsız saklanır. İstenen, beklenen, bulunan, indirilen ve oynatılabilir durumları ayrı hesaplanır.
- Edinme komutu, seçilen release ve profil sürümünün snapshot'ını taşır. Sonradan profil değişmesi devam eden işe sessizce farklı anlam yüklemez.
- İptal/devam/silme gibi niyetler revizyon taşır; eski worker gözlemi yeni komutu ezemez.
- Edinme hesabı, işin kalıcı sahibidir. Manuel komut girişe; otomasyon kural/monitor sahibine bağlanır. Paket byte rezervasyonu veri başlamadan doğrulanır; resume aynı paket neslini tekrar borçlandırmaz, staging temizliği yeni nesildir (0.24).
- Oda zaman çizelgesi revizyonu, hazır bariyeri ve kaynak epoch'u ayrı tutulur. Oda üyesinin akışı normal playback admission/kotasını kullanır; kumanda rolü medya taşımaz. Oda/member/epoch, hazırlıktan byte okumaya kadar oynatma lease'ine bağlıdır (0.23).
- Metadata güncellemesi kullanıcının kilitlediği alanı veya manuel eşleştirmesini izinsiz değiştirmez.

## Dosya işlemleri

- Kökler `read-only` veya açıkça `managed` olarak tanımlanır. Kaynak verisi ve uygulama cache/staging alanı ayrıdır.
- Kütüphane devre dışı bırakma/arşivleme fiziksel dosya silme işlemi değildir.
- Offline kök veya kısmi tarama silme/eksik toplu sonucuna dönüşmez.
- Transfer hash kontrolünü, import medya/profil kontrolünü tamamlamadan yeni sürüm iyi eski sürümün yerine geçmez.
- Cross-volume copy, hardlink ve atomic move farklı sonuçlar olarak ölçülür. Çakışan dosyada kullanıcı verisi üzerine yazılmaz.
- Geri dönüşüm/retention; kütüphane linkleri, seeding ve açık oynatma oturumlarıyla birlikte değerlendirilir.
- Dış yol, symlink/reparse, arşiv traversal, dosya aygıt adları ve aşırı metadata derinliği sınırları bütün giriş yollarında ortaktır.

## Sağlayıcı sözleşmesi

Her adaptör: protokol/sürüm, desteklenen içerik ve sorgular, kimlik doğrulama, kota, paging, timeout, görüntüleme atfı, hata dönüşümü ve doğrulama tarihini bildirir. Yok bilgi `unknown` olur; sıfır/başarılı olarak uydurulmaz.

Sırlar sunucuda korumalı tutulur. API cevapları ve loglar anahtarı, cookie'yi veya özel tracker URL'sini taşımaz. İndirme için istemcinin serbest URL göndermesi yerine kaydedilmiş release kimliği kullanılır. Bir kaynağın başarısızlığı, başarılı diğer kaynak sonuçlarını yok etmez.

## İş yürütme

- Kalıcılık SQLite'tadır; RAM kuyruğu yalnızca uyandırma/yerel yürütme aracıdır.
- Dış ağ çağrısı sırasında uzun DB transaction tutulmaz.
- Her iş için durum, giriş, deneme sayısı, son hata, tekrar zamanı, niyet revizyonu ve idempotency anahtarı bulunur.
- İnteraktif oynatma, ağır tarama/thumbnail/fingerprint işlemlerinden önceliklidir. CPU, disk, ağ ve GPU için ayrı sınırlı iş havuzları gerekir.
- Kapanış, iptal ve güç kesintisi sonrası toparlanma farklı yollardır. Sonuçlar ancak gerçek tamamlanma koşuluyla başarılı işaretlenir.
- Tek veri köküne tek yazıcı Home instance'ı; şema yükseltmesi öncesi tutarlı yedek ve anahtar koruması.

## İstemci

Web/PWA, backend komutlarına bağlı tek tasarım sistemi kullanır. Cam materyal gezinme ve kontrollerde, içerik açık hiyerarşide kalır. Büyük listeler server paging/virtualization kullanır. Tasarım örnek veriye bağlı kurulmaz. Oynatma modu codec ve cihaz profiline dayanır; frontend kütüphanesi seçimi codec desteği iddiası değildir.

2026-10-14 istemci açıklaması: CasaOS/Linux/Windows üzerinde çalışan Home sunucusu ortak katalog, hesap, iş ve konum verisini tutar. Bilgisayar web arayüzünü; telefon/tablet mobil uyumlu web/PWA'yı kullanır. TV için kumanda, büyük kartlar ve uzaktan okunabilirliğe göre ayrı istemci hedeflenir; yerel TV/mobil istemciler henüz uygulanmadı. CasaOS'a özel mağaza paketi ve Linux/Docker çalıştırma kabulü açıktır. Web/PWA push 0.21'de aynı hesap/outbox modeline bağlandı; cihaz/gateway kabulü ayrıca izlenir.

## Dış bağımlılık kapıları

- Gerçek metadata: kullanıcı TMDB token'ı ve sağlayıcı erişimi.
- Gerçek kaynak araması: kullanıcının indexer endpoint/kimlik bilgisi veya doğrulanmış açık adaptör.
- Altyazı/lyrics/fingerprint: ilgili sağlayıcı anahtarı, kota ve lisans.
- GPU/DVR/cast: gerçek cihaz ve istemci doğrulaması.
- Dağıtım: uygulama/bağımlılık lisansı kararı, imzalama ve hedef platform paketi.

Bu kapılar hata/sahte veriyle gizlenmez; uygulama bağlantı/uyumluluk durumunu açık gösterir.
