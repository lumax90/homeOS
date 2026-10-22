# Home ürün planı

Oluşturma: **2026-10-12**. Amaç: Plex/Jellyfin + Prowlarr + Radarr + Sonarr + Lidarr + Bazarr + Overseerr/Seerr işlevlerini, kendi veri ve iş motoruna sahip tek homeserver ürününde erişilebilir kılmak.

## Kararlaştırılanlar

- Sunucu: C# / ASP.NET Core (.NET 10 LTS), SQLite, kalıcı ve sınırlı iş yürütme.
- İstemci: React/TypeScript, HeroUI, Motion; ilk kararlı hedef **web + PWA**.
- Her kullanıcı işlemi aynı katalog, izin modeli ve iş geçmişini kullanır.
- Gerçek metadata akışı; kullanıcı TMDB token'ını Home'a ekleyecek. Anahtarsız TVmaze kullanılabilir.
- Geliştirme doğrulamasında internetten film/müzik indirme başlatılmaz. Gerçek edinme kullanıcı komutudur; gelecekteki otomasyon da kullanıcının açık tercihiyle çalışır.
- Browser/cihaz kabul testleri kullanıcıda. Backend doğrulama ve üretim derlemesi geliştirme sorumluluğunda.
- Dağıtım lisansı açık karar. Mevcut uygulamaları arka planda çalıştırmak ürün mimarisi değildir.

## Planın dosyaları

1. [Kapsam ve özellik izlenebilirliği](feature-matrix.md)
2. [Mimari ve veri kuralları](architecture.md)
3. [Uygulama sırası ve çalışma panosu](execution.md)
4. [Kabul ve yayın kapıları](acceptance.md)
5. [Kaynak / kanıt kaydı](sources.md)

## Başarı ölçütü

Kullanıcı gerçek bir içerik bulur; ister, takip eder veya koleksiyonuna ekler; sistem mevcut dosyaları ve eksikleri bilir; kaynakları sorgular; sürümleri açıklanabilir tercihlerle değerlendirir; seçilen edinmeyi doğrulanmış dosya/altyazı/metadata olarak kütüphaneye bağlar; cihazına uygun oynatır. Sorun oluşursa aynı içerik sayfasında sebep, geçmiş ve uygulanabilir bir sonraki işlem görünür.

Bir ekranda buton bulunması veya bir API adaptörünün derlenmesi tamamlanmış özellik sayılmaz. Her kapsam satırının çalışan komutu, kalıcı sonucu, hata davranışı ve doğrulaması olmalıdır.

## Durumların anlamı

- **Kısmi:** bir alt akış var; satırın bütün kabul ölçütleri henüz sağlanmıyor.
- **Planlandı:** tasarım/sıra tanımlı, uygulanmadı.
- **Uygulanıyor:** aktif kod değişikliği var.
- **Backend doğrulandı:** ilgili teknik kontroller geçti; kullanıcı kabulü ayrıca izlenir.
- **Kullanıcı kabulü:** browser/cihaz sonucu kullanıcı tarafından bildirildi.
- **Tamamlandı:** satırın bütün zorunlu koşulları ve bağımlılıkları kapandı.

Çalışan **0.27.0 / şema 39** geliştirme önizlemesi, tam ürün kabulü anlamına gelmez; son tam regresyonlu yayın 0.26.0'dır. Yüzde veya bitiş tarihi, ölçüm yapılmadan uydurulmaz. Desteklenmeyen sağlayıcı/cihaz, eksik kimlik bilgisi ve lisans bağımlılıkları açık kapı olarak tutulur.
