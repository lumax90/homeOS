# 0.11 — Kurulum, güvenilir paket ve ağ ayarları

Başlangıç: **2026-10-13**, **0.11.1 yayımlandı; Windows/native/HTTP doğrulandı**. OPS-02/04/05/06/09, CLI-01/02.

İmzalı sürüm manifesti, dosya hash'leri, platform/şema uyumu, önceki sürüm + veri yedeği, staging hazırlığı ve kontrollü yeniden başlatma. Güncelleme paketi içinden gelen anahtar kendisine güven vermez; kurulumun önceden güvendiği public key kullanılır. Dağıtım lisansı kararı açık; yerel geliştirme imzası kamu yayın kimliği sayılmaz.

Reverse proxy/path-base desteği; yalnız açıkça yapılandırılmış proxy IP'lerinden forwarded headers, Kestrel'in gerçek TLS sertifikası ve HTTP/PWA tanısı. Kaynak: Context7 `/dotnet/aspnetcore.docs`, proxy/load-balancer, `UsePathBase` ve Kestrel certificate belgeleri 2026-10-13'te kontrol edildi.

Hedef paketler Windows x64, Linux x64/ARM64 ve Docker tanımı. Bu ortamda Docker daemon/WSL çalışmıyor; Linux/ARM64 cihaz çalıştırma kabulü ayrı kapı olarak kalır.

## Sonuçlar

- Birleşik **126/126 test, 0 atlanan**. `PackageTests` gerçek RSA-PSS imza, hash, kendi anahtarını getiren paket, yol kaçışı ve hazırlık/iptal sınırını; `NetworkTests` alt-yol API/HTML/manifest/cookie ve güvenilen proxy davranışını doğrular.
- `PublishedHttpsTests` gerçek yayımlanmış Home sürecini PFX ile HTTPS'e ve `/home` alt yoluna geçirip yeniden başlatır. TLS el sıkışması, yeni oturum, secure cookie, HTTP→HTTPS yönlendirme ve yanlış path sınırı doğrulandı. Güvenlik/path sınırı değişince eski sunucu oturumları iptal edilir; cookie isimleri ve yolları alt yola özgüdür.
- İzole gerçek süreç testi: **0.11.0 → imzalı 0.11.1 → 0.11.0 + eski veri state'i**. Hesap kaldı, geri dönüş eski oturumları geçersizleştirdi. `artifacts/update-roundtrip-v011.json`.
- Windows kurucu `-NoStart -NoShortcut` ile ayrı geçici kurulum/veri dizinine çalıştırıldı; kurulan 410 dosyanın imzası/hash'i gerçek executable ile doğrulandı (`artifacts/install-verification-v011.json`). Kullanıcı masaüstü/browser kabulü açık.
- Windows x64, Linux x64, Linux ARM64 paketleri `artifacts/packages/home-0.11.1-*.zip` ve `.sha256` olarak üretildi; Linux paketleri burada çalıştırılmadı. `docker compose ... config --quiet` başarılı; imaj build/run kapısı açık.
- Paketler RSA-PSS/SHA-256 ile imzalı; yerel geliştirme public-key fingerprint `b6ebb7e0a2b2821afd3143dc3bd9c8b666189d9a41953aa6657cf60d789b566b`. Özel anahtar pakete eklenmez. İmzalı dosyalar değişirse güncelleme/geri dönüş reddedilir. İlk kurulum güven kökü ve kamu yayın kimliği dağıtım kararıdır.
- Standalone süreç kendini yeni release'e geçirir; container/systemd kurulumu süreç yöneticisiyle güncellenir/başlatılır. Başlangıç launcher'ı son etkin imzalı release'e yönlenir. Ağ/başlangıç dosyası `home-settings.json`, state yedeğinden ayrı korunur.
- Çalışan yayın: `artifacts/packages/publish-0.11.1-win-x64`, `http://localhost:5080`; canlıda HTTPS varsayılanı değiştirilmedi. `artifacts/live-runtime-v011.json`: 0 transfer, güvenilir paket anahtarı yüklü, bekleyen güncelleme yok.
- Önceki canlı state yedeği: `.data/preview/backups/363fef7dbf9343faabf7a9cfeb065a57.zip`.

Kurulum, proxy, volume/UID ve güncelleme adımları: [deploy/README.md](../deploy/README.md). Teknik dosyalar `Operations/PackageTrust.cs`, `UpdateService.cs`, `NetworkConfiguration.cs`; paketleyici `tools/Home.Packager`, script `tools/package.ps1`.
