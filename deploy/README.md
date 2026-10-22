# Home kurulumları

Güncelleme: **2026-10-22**. **0.43: Intel/AMD x64 CasaOS üzerinde ekransız Home sunucusu ve IP üzerinden LAN istemcileri için yerel paket hazır.** Linux container/native/protocol ve veri korunumu doğrulandı; sonuçlar [headless/LAN kaydında](../docs/headless-lan-043.md). Paket `artifacts/home-casaos-0.43.0/` altında. Fiziksel cihaz kabulü, ARM64 native oyun paketi ve resmi mağaza dağıtımı açık.

## Windows ve Linux paketleri

Geliştirme ağacında önce `npm run build`, sonra `tools/package.ps1 -Runtime win-x64` (veya `linux-x64`, `linux-arm64`). Paketleyici özel anahtarı `.data/package-signing/private.pem` altında oluşturur; dosya paketlere eklenmez. Genel anahtar/fingerprint ve SHA-256 manifesti pakette bulunur. İlk kurulum kaynağı/anahtarı dağıtım kanalından doğrulanır. Güncelleme daima **mevcut kurulumun** güvendiği anahtarla doğrulanır.

ZIP'i çıkarıp Windows'ta `Install-Home.ps1`, Linux'ta `bash install-home.sh` çalıştır. İstersen kurulum/veri yolu ve portu belirt. Sonrasında Home tarayıcıdaki ilk hesap ekranını açar. Dış FFmpeg/ffprobe ve PAR2 uygulama tarafından PATH'ten bulunabilir veya `Home__FfmpegPath`, `Home__FfprobePath`, `Home__Par2Path` ile tanımlanır.

Linux'ta `home.service.example` kullanıcı servisi örneğidir; yolu kendi kurulumuna göre düzenleyip `~/.config/systemd/user/home.service` altına al. `systemctl --user daemon-reload` ve `systemctl --user enable --now home` uygulanabilir. İşletim sistemi paket gereksinimleri .NET 10 `runtime-deps`/Ubuntu Noble eşdeğeridir. Bu platformlarda native codec/GPU ve dosya izinleri ayrıca doğrulanır.

## Docker

`docker compose -f deploy/compose.yaml up -d --build --wait`. Aynı ağdaki cihazlarda **`http://SUNUCU-IP:5080`** açılır; sunucu üzerinde tarayıcı/masaüstü oturumu gerekmez. Kalıcı uygulama verisi `home-data` volume'ündedir. UID/GID **1654**; bind data dizini seçilirse bu kullanıcıya yazma izni sağlanır. Medya klasörü eklemek için `HOME_MEDIA_PATH` ayarla ve `compose.media.example.yaml` override'ını kullan. Kütüphane yolu uygulamada `/media` olur; bind mount salt okunurdur. Compose tanımı kullanıcı medyasını oluşturmaz veya silmez.

Oyunlar için **62405 UDP ve TCP** aynı numarayla yayımlanır; emülatörün komut ve kaynak portları container içinde loopback'te kalır. WebRTC'nin duyuracağı IP normalde tarayıcıda açtığın adresten alınır; proxy/NAT için `HOME_SERVER_IP` belirtilebilir. Üç dakikalık kapanış süresi, init, sınırlı loglar, read-only rootfs ve dropped capabilities tanımda hazırdır.

**CasaOS:** Yerel imaj arşivi ve Custom Install tanımı [casaos/README.md](casaos/README.md) altında. İmaj önce `load-home.sh` ile yüklenir; sonra CasaOS'ta `docker-compose.yml` içe aktarılır. `home:0.43.0` kamu registry'sinde yayımlanmış bir imaj adı değildir.

Docker güncellemeleri imaj/süreç yöneticisiyle, **aynı `/data` volume'ü korunarak** yapılır. Uygulama içi executable geçişi container'da kullanılmaz. Tarifte FFmpeg/PAR2/fontlar ve doğrulanmış Linux native çalışma paketi vardır; sürüm/copyright envanteri ve native kaynakları imaj içinde tutulur. Windows'taki veri klasörünü bu sunucuya otomatik taşımıyoruz; tam veri taşımasında ROM/kayıt dosyaları da dahil volume/dosya aktarımı gerekir, küçük uygulama yedeği medya payload'ı değildir.

## Ağ ve HTTPS

Hesabın → Yedekleme ve sunucu → Ağ ve HTTPS. Dinlenecek IP/port, PFX/P12 sertifikası, genel URL, alt yol ve güvenilen proxy IP'leri yapılandırılır. TLS özel anahtarı Data Protection ile korumalı uygulama state'ine kaydedilir. Ağ ayarları veri kökündeki `home-settings.json` ile sonraki başlangıçta uygulanır; açık komut satırı/ortam seçenekleri önceliklidir. Container içindeki 8080 portu sabittir; dış port Compose eşlemesinden seçilir.

IP/HTTP ile online arayüz, medya ve desteklenen tarayıcıda receive-only WebRTC/klavye/dokunmatik kullanılabilir. Gamepad API, PWA kurulum/çevrimdışı şifreleme ve kamera/mikrofon birçok tarayıcıda **güvenilir HTTPS** ister. Home bu sınırlamaları atlatmaz. Domain zorunlu değildir; cihazların güvendiği IP-SAN sertifikası da kullanılabilir. Otomatik `home.local` duyurusu sonraki ayrı kapsamdır.

Reverse proxy için Home'u loopback'te tutup HTTPS'i örnek Caddy yapılandırmasında sonlandırabilirsin. Home'a yalnız gerçek proxy IP'sini ve `https://...` genel adresini kaydet. Alt yol seçildiyse proxy bu prefix'i kaldırmadan Home'a iletir: örneğin `/home/api/...`. Web, cookie path, afiş, altyazı, HLS ve PWA scope aynı prefix'i kullanır. `X-Forwarded-*` yalnız yapılandırılmış proxy IP'lerinden işlenir.

## Güncelleme ve geri dönüş

Uygulama → sunucu → sürüm/güncelleme bölümüne imzalı ZIP yükle. İmza, runtime, şema, boyut, yollar ve dosya hash'leri doğrulanınca hazırlık görünür. **Yedekle ve güncelle** önce online state yedeği alır, sonra yeni release dizinine geçer. Önceki release dosyaları yerinde kalır. İmzalı paket kurulumlarında önceki sürüm ve güncelleme öncesi DB/anahtar/altyazı yedeğine geri dönüş komutu bulunur. Restore sonrasında oturumlar iptal edilir, otomatik işler kapatılır ve transferler duraklatılır.

Kurtarma için eski paket ve `.data/.../backups/*.zip` yedeklerini tut. Bunlar medya payload'ının yedeği değildir. `home-settings.json` ağ/başlangıç ayarı ayrıca korunmalıdır. Geliştirme imzası henüz kamu ürün imza kimliği değildir.
