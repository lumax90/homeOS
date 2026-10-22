# 0.19 — Hesap/cihaz oynatma sınırları ve ortak akış oturumu

Tarih: **2026-10-14**, **0.19.0 yayımlandı**. Gereksinimler: **USR-03, USR-01/02, PLY-02/03/09, MUS-06, OPS-01/03**.

## Tek kota, bütün oynatma yolları

`PlaybackSessions`, doğrudan dosya, HLS/remux/ses-video dönüştürme ve PCM müzik kuyruğu için ortak bir oturum verir. FFmpeg hazırlanmadan önce SQLite üzerinde kota ayrılır. Bir ses kuyruğu tek oturum kullanır; sıradaki parçanın ön hazırlığı ikinci bir oynatma sayılmaz.

- Varsayılan sunucu sınırı **16** eşzamanlı oynatma; ayarlanabilir aralık 1–64.
- Varsayılan hesap sınırı **3** oynatma / **3** etkin cihaz; hesap sınırı 0–16, etkin cihaz 1–16. Oynatma sınırındaki 0, o hesapta oynatmayı kapatır.
- Varsayılan cihaz sınırı **2** oynatma; 1–16 ayarlanabilir. Yönetici cihaz bazında 0–16 override seçebilir; 0 o cihazda oynatmayı kapatır. Boş override genel cihaz varsayılanını kullanır.
- Hesap, etkin cihaz sayısı, cihaz ve toplam sunucu sınırları birlikte uygulanır. Yönetici hesapları da bu sayaca katılır. Mevcut FFmpeg dönüştürme adedi/disk rezervasyonu ve PCM hazırlama/cache sınırları ayrıca geçerlidir.
- Sınır güncellemesi revizyonludur. Genel sınır değişikliği tüm açık oynatmaları; hesap/cihaz değişikliği ilgili oynatmaları kapatır. Eski revizyonla kaydetme 409 döndürür.

Yönetici **Hane ve hesaplar** bölümünden hesap, sunucu ve cihaz sınırlarını düzenler. Kullanıcı **Açık oynatmalarım** bölümünde kendi oturumlarını ve geçmişini görüp diğer girişlerindeki oynatmayı kapatabilir. Yönetici bütün hanenin oynatmalarını kapatabilir. İzleme/oynatma erişimi başka bir girişin akış kimliğini kullanma yetkisi vermez.

## Tarayıcı cihazı ve komut kimliği

`PlaybackDeviceIdentity`, sunucunun ürettiği rastgele cihaz kimliğini Data Protection ile korunan **HttpOnly / SameSite=Strict** çerezine koyar. HTTPS ve path-base cookie sınırları uygulanır. Giriş kaydı cihaz kimliğine bağlanır; arayüzün gönderdiği `clientId`, cihaz kimliği olarak kabul edilmez.

Normal sekmeler aynı cihaz kaydını, ayrı oynatıcı komut kimliklerini kullanır. Başarılı yeniden giriş ve PIN/profil geçişi aynı tarayıcı cihazını korur; önceki giriş iptal edilir. 0.18'den taşınan girişler migration sırasında ayrı bir cihaz kaydı alır; sonraki girişte korumalı çerez oluşturulur. Bu, tarayıcı kaydıdır; fiziksel donanım attestation'ı değildir. Çerezlerin silinmesi yeni tarayıcı kaydı oluşturur; hesap ve toplam eşzamanlı sınırlar yine uygulanır.

Web belgesi, video/PCM/özgün oynatıcı geçişlerinde aynı `browserPlayback` komut kimliğini ve monoton revizyonu korur. Eski bileşenin gecikmiş close isteği daha yeni oturumu kapatamaz. Aynı komutun tekrar gönderilmesi aynı oturumu verir. Aynı revizyonla farklı istek, kapanmış komutu tekrar kullanma ve başka girişin oturumunu replace etme reddedilir. Kota ayrımı ve eski oturumun yer değiştirmesi aynı DB transaction'ındadır.

## Akış erişimi ve iptal

- `POST /api/playback`: artık doğrudan akış için de oturum `id`'si verir. Dönen `url` kullanılmalıdır.
- `/api/media/{id}/stream?playbackId=...`: yalnız o medya ve `direct` oturumuna erişir. Oturumsuz raw medya GET/HEAD **428** döndürür.
- `/api/playback/{id}/index.m3u8` ve segmentler: ortak oturum kimliğiyle, aynı yetkili girişe bağlıdır. Low-level FFmpeg süreç kimliği istemciye verilmez.
- `POST /api/music/playback`: tek PCM kuyruk oturumu açar. `/music/media/{id}/prepare` ve `/pcm` bu `playbackId`'yi gerektirir.
- Byte Range, suffix/invalid range ve HEAD davranışı ASP.NET Core dosya sonuçlarıyla korunur. Akış yanıtları `no-store` kullanır.
- Her veri isteği hesap/giriş, kaynak erişimi, medya fingerprint/eşleşme revizyonu, kota revizyonları ve oturum yaşam süresini denetler. PCM'de her izinli parça ayrıca doğrulanır. PCM kimliği video dosyası açamaz.
- Aktif yanıt, oturumun cancellation token'ına bağlanır. Kullanıcı/yönetici kapatması, çıkış veya yetki iptali `HttpContext.Abort` ile açık yanıtı ve hazırlık token'ını iptal eder. FFmpeg ve hazırlanan geçici dosya temizliği mevcut sahiplik sınırlarını kullanır.
- Yeni istekler yetki değişikliğini doğrudan görür. Arka plan uzlaştırıcısı açık akışları iki saniyelik aralıkla kontrol eder; hesap/oturum yönetimi uçları ayrıca hemen uzlaştırır. İstemci SSE/heartbeat iptalini alınca oynatıcıyı durdurur. Daha önce istemciye iletilmiş byte'ların geri alınması iddia edilmez.

## Yaşam süresi, restart ve sınırlar

- Heartbeat veya parça isteği olmadan **2 dakika**; duraklatılmış durumda heartbeat olsa da **15 dakika**; tek lease için **24 saat** üst sınırı.
- Aynı lease altında en fazla **6** eşzamanlı HTTP veri/hazırlık isteği. PCM'de son üç hazırlanan izin ve hâlen çalan parça tutulur: en fazla dört medya izni. Decode örnekleri mevcut 24 saniyelik istemci planlama ufkuna ve 4 GiB sunucu PCM cache'ine tabidir.
- Başlangıç hazırlığı bir dakika bütçeli; mevcut HLS ilk playlist süresi 35 saniye, PCM decode işi üç dakika sınırındadır. Kuyruk başlangıcı, PCM decode'dan önce döndüğü için istemci hazırlık sürerken heartbeat gönderebilir.
- Kapanmış oturum geçmişi en fazla **90 gün / 10.000** kayıt; API son 100 kaydı döndürür. Oynatıcı intent'leri giriş başına 1.024, sunucu genelinde 20.000 kayıtla sınırlıdır; geçersiz girişlere ait intent'ler temizlenir. Cihaz kayıt üst sınırı 10.000'dir.
- Normal kapanmada açık lease'ler `server-stopped` olur. Çökmeden sonra kalan lease'ler `server-restarted` ile kapatılır; eski akış adresleri yeniden kullanılmaz. Kişisel izleme konumu ve sınır tercihleri kalır. Kullanıcı kaldığı yerden yeni oynatma başlatır.
- Kaynak video/müzik klasörleri salt okunurdur. Oturum kapatma kaynak dosyayı veya başka oturumun PCM cache'ini silmez.

## API ve şema

Şema **22 → 23**, `023_playback_quotas.sql`. Yeni tablolar: `playback_devices`, `user_playback_limits`, `playback_intents`, `playback_leases`, `playback_media`; girişlere `playback_device_id` eklendi. Önceki yayımlanmış migration'lar değiştirilmedi.

- `/api/account/playback`, `/api/account/playback/{id}`: kişisel görünüm ve kapatma. `?all=true` yalnız yöneticiye açıktır.
- `/api/playback/limits`, `/api/users/{id}/playback-limits`: yönetici sınırları ve revizyonlu kayıt.
- `/api/playback/devices`, `/api/playback/devices/{id}`: kapsamlı cihaz listesi ve yönetici düzenlemesi.
- `PlaybackSessions`, `MediaStore.PlaybackLeases`, `MediaStore.PlaybackLimits`, `PlaybackDeviceIdentity`; frontend `PlaybackUsage.tsx`, `playbackIdentity.ts`, PCM transport ve iki oynatıcı.

Eski bağımsız istemciler doğrudan raw/PCM adresi yerine önce oturum açıp dönen kimliği kullanmalıdır. Home web/PWA istemcisi aynı yayında güncellenmiştir.

## Doğrulama

- Odaklı kota/HTTP/hesap/erişim kontrolleri **27/27**, atlanan 0; TypeScript + üretim build ve **7/7** saf ses zamanlayıcı testi başarılı.
- Eşzamanlı farklı protokol rezervasyonu, hesap/cihaz/toplam sınırı, raw GET kaçışının engellenmesi, aynı hesapta farklı giriş izolasyonu, tek slotta replace, eski close/retry, yetki değişimi, süre aşımı ve DB restart toparlama testleri.
- Gerçek FFmpeg ile direct → HLS → PCM → direct yolu ve bitişik müzik ön hazırlığının tek kota kullanması; oturumsuz/yanlış protokolle raw/PCM erişim reddi.
- Açık 64 MiB doğrudan HTTP yanıtının sunucu kapatmasıyla kesilmesi; bloklanan probe/PCM hazırlığının iptali. Fixture'lar test alanında yerel olarak üretilir.
- Ses testleri yeni oturum kabulünden önce PCM'nin hazırlanmamasını, iptal edilen lease'in planlı ses düğümlerini durdurmasını ve eski heartbeat'in yeni seek'i bozamamasını doğrular. Bu değişiklik sırasında bulunan kuyruk sonu/replay yarışması giderildi.
- **207/207 backend/native test, atlanan 0**, `artifacts/test-results/home-0.19.0-full.trx`. Yayımlanmış Kestrel sürecinde normal restart ve zorla süreç sonlandırma sonrasında eski stream/komut kimlikleri reddedildi; hesap sınırı, giriş ve kaynak byte'ları korundu. HTTPS/path-base üzerinde cihaz çerezinin Secure/HttpOnly/path sınırı da doğrulandı.
- Windows x64, Linux x64 ve Linux ARM64 paketleri aynı güvenilir anahtarla imzalandı. Gerçek süreçte **0.18.0/şema 22 → 0.19.0/şema 23 → eski sürüm/veri** update/rollback geçti; restore oturumları iptal etti (`artifacts/update-roundtrip-0.18.0-0.19.0.json`). Linux çalıştırma kabulü açıktır.
- Canlı önizleme imzalı uygulama içi güncellemeyle 0.19.0/şema 23'e geçti; sahip oturumu korundu. Sunucu 16, hesap 3 oynatma/3 cihaz, varsayılan cihaz 2 sınırı API'den okundu. **0 açık oynatma, 0 indirme, 0 etkin sentetik kütüphane**; VAD CPU başlangıcı başarılı. Doğrulama oynatma/medya indirmesi veya medya analiz işi başlatmadı (`artifacts/live-runtime-v019.json`).
- Browser/cihaz kabulü kullanıcıya aittir; otomatik browser suite çalıştırılmadı.

## Kaynaklar — 2026-10-14

1. Context7 `/dotnet/aspnetcore.docs`: [file results ve range/conditional yanıtları](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/responses?view=aspnetcore-10.0), [HttpContext / RequestAborted](https://learn.microsoft.com/aspnet/core/fundamentals/use-http-context?view=aspnetcore-10.0). Kurulu .NET **10.0.12** reference XML'de `Results.Stream`, `FileStreamHttpResult.ExecuteAsync` ve `FileStream` sözleşmeleri okundu.
2. Context7 `/reactjs/react.dev`: [useEffect](https://react.dev/reference/react/useEffect), [StrictMode](https://react.dev/reference/react/StrictMode); asenkron cleanup ve eski cevabın engellenmesi. Kurulu React **19.3.0**, HeroUI **3.2.6** button tipleri ve TypeScript build doğrulandı.
3. Mevcut Home Data Protection/cookie/path-base sınırları ve [0.11 dağıtım sözleşmesi](milestone-11.md). Yeni üçüncü taraf bağımlılığı eklenmedi.

## Kullanıcı kabulü — ACCEPT-19

1. Hane hesabında eşzamanlı oynatmayı 1 yap; bir video oynarken ikinci sekmede müzik/video açmayı dene. Açıklamalı sınır mesajından **Açık oynatmalarım** bölümüne geçip eski oynatmayı kapat.
2. Aynı oynatıcıda ses/altyazı/kalite veya kesintisiz → özgün müzik geçişini dene; bu geçiş kendi başına ikinci slot tüketmemeli.
3. Başka tarayıcıdan/cihazdan hesabınla giriş yapıp oynatmayı veya giriş oturumunu kapat; ilk cihazın akışı durmalı.
4. Sunucuyu yeniden başlat; sınırlar/kişisel konum korunmalı, eski oturum geçmişte görünmeli; kaldığın yerden yeniden oynat.

Bu dilim USR-03'ün oynatma eşzamanlılık/cihaz ve oturum geçmişi bölümünü uygular. Kişiye özel internet edinme/indirme bütçelerinin genişletilmesi, offline medya, oda/cast ve bütün ürünün kalan kabul kapıları panoda açıktır.
