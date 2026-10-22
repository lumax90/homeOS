# Raflı keşif ve arayüz tür dili — 0.29 geliştirme önizlemesi

Tarih: **2026-10-18**. Kart **DISC-04 / CAT-02/05/08, CLI-01/05**. Şema **39**, migration yok. Kaynaklar gerçek TMDB/TVmaze ve mevcut hesabın yerel katalog kayıtlarıdır; Netflix kodu, verisi veya görselleri kopyalanmaz.

## Dil sınırı

- Tür seçimi ve tür raflarının başlığı **arayüz dili** ile istenir: English → `en-US`, Türkçe → `tr-TR`. `/discovery/genres?language=` açık ve iki destekli locale ile doğrulanır. Tür kimlikleri değişmez; cache locale/kind'a göre ayrılır.
- `/discovery?genreLanguage=` kart metadata'sındaki genre etiketlerini de aynı UI locale'i ile eşler. Metadata başlığı/özeti için kullanıcının ayrı TMDB dil tercihi korunur; salt-okunur UI istekleri bu ayarı veya kayıtlı yerel başlıkları değiştirmez. Bu ayrım discovery footer'ında ayar bağlantısıyla açıklanır.
- Eski dinamik arama başlığı/sabit kart penceresi kaldırıldı. Ortak süre biçimi English için `s/min/h`, Türkçe için `sn/dk/sa` kullanır. Kullanıcı/sağlayıcı başlıkları otomatik çeviri hizmetine gönderilmez.

## Sayfa akışı

- Türler üstte yan yana bütün liste olarak gösterilmez; kompakt genre seçicidir.
- Browse görünümü gerçek **haftalık trend**, **izlemeye devam**, **daha sonra izle**, **izleme geçmişi**, **popüler**, **yüksek puanlı**, **yakında** ve gerçek genre raflarından oluşur. Raflarda yatay gezinme vardır; sayfayı aşağı kaydırırken sonraki raflar yaklaşınca yüklenir.
- Genre, arama veya Tümünü gör görünümü responsive grid'dir. Sütun sayısı container genişliğine göre **2–10** hesaplanır. Render sabit 20/40 ile kesilmez; TMDB'nin 20 kayıtlık API batch'i frontend sayfa sınırı değildir.
- Grid alt sentinel'i ve rafların yatay son sentinel'i yeni provider cursor'unu lazy ister. Posterler `loading=lazy` ve async decoding kullanır. En alttaki genel katalog grid'i de viewport'a yaklaşmadan başlamaz. Görünmeyen/başka route altındaki discovery otomatik ek sayfa istemez.
- Trend hero ve ilk raf aynı TanStack infinite cache'i paylaşır. Query'ler kind/genre/arama/UI genre locale/metadata locale ile ayrılır; AbortSignal taşınır, yeni sayfa mevcut fetch'le yarıştırılmaz (`cancelRefetch:false`). Üç ardışık boş batch otomatik akışı durdurur; manuel devam düğmesi bulunur. Provider 500 sayfa cursor sınırı korunur.
- Bu bir kişiselleştirilmiş öneri algoritması iddiası değildir: kişisel raflar kendi kayıtlarını; alternatif genre rafları sağlayıcının popüler/puan koleksiyonlarını gösterir. TVmaze'de olmayan trend/genre özellikleri varmış gibi sunulmaz; gerçek dizi katalog/arama grid'i kullanılır.

## Kişisel raflar

Yeni `/discovery/personal` endpoint'i kind/shelf/offset/limit doğrular; en fazla 60 kayıt/sayfa ve 5000 offset. History/continue için yalnız giriş yapan kullanıcının `content_watch_state` verisi; My list için kendi aktif isteği, favorisi veya sahibi olduğu koleksiyonu kullanılır. Yönetici rolü bu ownership koşulunu bütün haneye genişletmez. Yaş/blocked/kütüphane ve canonical content sınırları mevcut ContentBrowseAccess ile korunur. GET yalnız okur; kullanıcı seçmeden edinme başlamaz.

## Doğrulama

**28/28 backend, 0 atlanan**: İngilizce/Türkçe genre cache/sorgu ayrımı ve metadata tercihinin korunumu; API auth/invalid locale/paging; kişisel ownership, yönetici/hane ayrımı ve yaş/blocked sınırları. Kanıt `artifacts/test-results/discovery-shelves-v029.trx`.

**39/39 Node**: responsive kolon/cursor/boş batch/tekilleştirme/lazy bağlaması; önceki yerelleştirme, navigation/hero, gerçek Video.js store, room ve offline controller kontrolleri. Frontend/worker tip kontrolü, üretim build ve .NET publish geçti; npm audit 0 açık. İstemci `/assets/index-Bylfe_va.js`, PWA `7cfd1058a2a17c0a`. Windows imzalı paket `artifacts/packages/preview-v029/home-0.29.0-dev.1-win-x64.zip`, **837 dosya / 74.818.956 byte**.

Canlı salt-okunur provider metadata kaydı aşağıya eklenir. Browser/device kabulünü kullanıcı yapar; otomatik browser suite veya internet medya edinmesi yapılmaz. **VERIFY-27** geniş regresyonu kapanmış sayılmaz.

## Çalışan önizleme

**2026-10-18 19:45 yerel saat**: imzalı **0.28.3 → 0.29.0** tamamlandı; sahip oturumu korundu. **http://localhost:5080**, PID **9776**, release `3ee1b965e1924d8daf9a455dd015b347`. Şema 39, FK ihlali 0 ve güncelleme öncesi medya/istek/indirme/kişisel kayıtlar korundu.

Gerçek salt-okunur provider kontrolünde **19 English movie genre**, ID 28 için **Action / Aksiyon** locale ayrımı ve English genre etiketli **20 gerçek aksiyon sonucu / nextPage 2** doğrulandı. Kullanıcının metadata dil tercihi **tr-TR** olarak aynı kaldı. Kişisel endpoint bu hesap için **5 My list / 1 history / 1 continue** döndürdü; history ID'leri yalnız bu hesabın `content_watch_state` ile karşılaştırıldı. Yeni shelves-v1 istemcisi HTTP üzerinden sunuluyor. Oynatma/edinme komutu verilmedi.

Kanıtlar `artifacts/preview-before-v029.json`, `artifacts/update-live-0.29.0-preview.json`, `artifacts/live-discovery-v029.json`. Paket SHA-256 `1bfa9362bce3872b92ed917a96374fe1e1cc4e2cc162175a1d4ffe4c70d97ed2`. Geri dönüş **0.28.3**, yedek `fa5a54a3ac824ed5ba0ef2e0065cf67e`; önceki release/yedekler korundu. **Ctrl+F5** ile raflı yeni istemci alınır; scroll/kolon/görsel/device kabulü kullanıcıdadır.

2026-10-18 Context7 [TanStack Query v5 infinite queries](https://tanstack.com/query/latest/docs/framework/react/guides/infinite-queries), query cancellation ve kurulu types okundu. IntersectionObserver/ResizeObserver tarayıcı API'sidir; yeni UI bağımlılığı veya sağlayıcı veri seti eklenmedi. Mevcut [veri/lisans sınırları](dependencies.md) geçerlidir.
