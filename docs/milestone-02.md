# 0.2 — İçerik kimliği ve uygulama içi indirme

Tarih: 2026-10-12. Bu belge uygulanan modeli ve kaynak doğrulamasını açıklar; son çalıştırma sonuçları `implementation.md` içindedir.

## İçerik modeli

- `media`: fiziksel dosya, yolu, gerçek codec/süre bilgileri ve izleme konumu.
- `content`: sağlayıcı/tür/dış kimlik için tek kanonik içerik kaydı; film veya dizi.
- `episodes`: diziye bağlı sezon/bölüm kimliği.
- `media_matches`: dosyanın içerik/bölüm bağlantısı, sürüm etiketi ve eşleştirme zamanı.
- Aynı sağlayıcı kimliğine bağlanan dosyalar içerik kimliğini paylaşır. Farklı sağlayıcılar arasındaki kimlik birleştirmesi henüz otomatik değildir.
- Sonraki taramalar fiziksel dosyayı günceller; kullanıcı eşleştirmesini değiştirmez.
- Bağlantı kaldırıldığında dosya adı yeniden gösterilir; dosya ve izleme geçmişi korunur.
- Arayüz yüklenen sonuçlarda aynı içerik/bölümün sürümlerini bir kartta toplar. API sayfalaması fiziksel dosyalar üzerindedir; dosya ve içerik sayıları farklı olabilir.

## Metadata sağlayıcıları

### TVmaze

Anahtarsız genel API: `https://api.tvmaze.com/`.

- `/search/shows?q=...`
- `/shows/{id}`
- `/shows/{id}/episodebynumber?season=...&number=...`

Arama sonucu kullanıcı tarafından seçilir. Eşleştirme sırasında sunucu, seçilen kimliği ve bölümü sağlayıcıdan yeniden alır. Başlık/özet/görsel URL'si tarayıcının gönderdiği veriden kaydedilmez.

Resmî kaynak: https://www.tvmaze.com/api — 2026-10-12 tarihinde incelendi. Kaynak atfı ve CC BY-SA bilgisi arayüzde bulunur. Genel API verileri İngilizce olabilir.

### TMDB

Sunucu tarafında `Authorization: Bearer` ile **API Read Access Token** kullanılır. Token ayarlardan girilir veya başlangıçta `Home__Tmdb__Token` ile sağlanabilir. Ayarlardaki kayıt ortam değerinin önüne geçer; temizleme işlemi bağlantıyı kaldırır.

- `/3/search/movie`, `/3/search/tv`
- `/3/movie/{id}`, `/3/tv/{id}`
- `/3/tv/{id}/season/{season}/episode/{episode}`
- `/3/authentication` bağlantı testi

Token ASP.NET Data Protection ile korunarak SQLite'ta tutulur ve API yanıtlarında geri verilmez. Bu korumanın anahtar dosyaları uygulamanın veri klasöründedir; yedekleme bu anahtarları da kapsamalıdır.

Kaynaklar (2026-10-12):

- https://developer.themoviedb.org/docs/authentication-application
- https://developer.themoviedb.org/reference/search-movie
- https://developer.themoviedb.org/reference/tv-episode-details
- https://developer.themoviedb.org/docs/image-basics

Görseller için belgelenmiş `image.tmdb.org/t/p/` boyutları kullanılır. Yetkili canlı TMDB testi için kullanıcı token'ı gerekir; HTTP fixture testleri bunun yerine canlı doğrulama sayılmaz.

### İstek ve görsel sınırları

Aramalar 10 dakika önbelleklenir. Sağlayıcı çağrıları tek sırada ve en az 600 ms aralıkla yapılır; 429 yanıtının bekleme süresi dikkate alınır. Yanıt gövdeleri ve okuma süreleri sınırlıdır. Görsel önbelleği yalnızca `image.tmdb.org` ve `static.tvmaze.com` HTTPS adreslerini kabul eder; yönlendirme izlemez.

## MonoTorrent motoru

**MonoTorrent 3.0.2**, uygulamanın kendi sürecine kütüphane olarak gömülüdür. Ayrı bir indirme uygulaması veya uzak indirme servisi kullanılmaz.

Paketin yayımlanmış XML API belgeleri ve `release-v3.0.2` etiketi incelendi. `master` dalındaki immutable settings API'si yayımlanmış 3.0.2 ile aynı değildir; uygulama 3.0.2'nin `EngineSettingsBuilder` / `TorrentSettingsBuilder` API'sini kullanır.

- https://github.com/alanmcgovern/monotorrent/tree/release-v3.0.2
- https://www.nuget.org/packages/MonoTorrent/3.0.2

Magnet önce metadata-only akışında çözülür. Dosya yolları doğrulandıktan sonra gerçek aktarım başlar. Çözülen metadata korumalı biçimde saklanır; v1/v2 hash takma adları yinelenen kayıtları önler.

### Kalıcılık ve kontrol

- `downloads`: kullanıcı niyeti (`running`, `paused`, `cancelled`), gözlenen durum, hızlar, hash, import ve tarama sonucu.
- Her komut `revision` artırır. Eski bir işleyicinin gözlemi yeni duraklatma/iptal/devam komutunu ezemez.
- Yeniden açılışta devam etmesi istenen işler yeniden doğrulanır. Duraklatılan ve iptal edilenler kendiliğinden başlamaz.
- MonoTorrent fast-resume modu `Accurate` olarak ayarlıdır.
- Başlangıç sınırı 3 eşzamanlı indirme, 3 paylaşım ve toplam 120 bağlantıdır. Paylaşım, yeni indirmelerin slotlarını tutmaz.
- Varsayılan yükleme sınırı 512 KiB/sn, indirme sınırı 0 (sınırsız). Bunlar `Home:Downloads` yapılandırmasındadır.
- Motor, ilk aktif indirmeye kadar oluşturulmaz. TCP/UDP portu 55123; otomatik router port yönlendirmesi kapalıdır.

### Dosya sahipliği ve yayın

Uygulama şu alanları yönetir:

```text
<data>/downloads/
  staging/<download-id>/        doğrulanan torrent payload'u
  imports/<id>-<attempt>/       yalnızca o import denemesinin geçici alanı
  library/<download-id>/        yayımlanmış medya
  engine/                      metadata ve fast-resume önbelleği
```

Medya, hash doğrulaması tamamlandıktan ve yazmalar durdurulduktan sonra içeri alınır. Uygun dosya sisteminde hardlink denenir; mümkün değilse dosya kopyalanır. Hazırlık alanı atomik klasör taşımasıyla yayımlanır. Manifest, yeniden başlatmada tamamlanmış yayının tekrar kullanılmasını sağlar. Çelişen hedefin üzerine yazılmaz.

Yayın ve tarama kuyruğu, tarama istekleriyle aynı kilit altında koordine edilir. Aktif taramanın görmediği yeni dosyaların kaybolmaması için yayın, o taramanın tamamlanmasını bekler.

İptal kaynak kütüphanelerini veya yayımlanmış dosyaları silmez. Kısmi payload ve önbellek için kullanıcı kontrollü temizleme/retention arayüzü sonraki iştir. Bu sürüm yazılım/ISO indirme yöneticisi değildir; desteklenen ses/video dosyası içeren torrentleri kabul eder.

## Yeni API

| Yöntem | Adres |
| --- | --- |
| GET / PUT | `/api/metadata/settings` |
| POST | `/api/metadata/test` |
| GET | `/api/metadata/search?provider=tvmaze&kind=show&q=...` |
| GET | `/api/media/{id}/match-hint` |
| PUT / DELETE | `/api/media/{id}/match` |
| GET | `/api/media/{id}/versions` |
| GET | `/api/media/{id}/artwork/{poster\|backdrop}` |
| GET | `/api/downloads` |
| POST | `/api/downloads/magnet` — `{ magnet }` |
| POST | `/api/downloads/torrent` — ham `.torrent` gövdesi, en fazla 4 MiB |
| POST | `/api/downloads/{id}/control` — `{ action: pause\|resume\|cancel }` |

Mevcut cookie/CSRF gereksinimleri bütün yeni yazma uçlarında geçerlidir. `home.lock`, aynı yazılabilir veri klasörünü iki Home sürecinin eşzamanlı açmasını engeller.
