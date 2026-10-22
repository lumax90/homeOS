# 0.23 — Birlikte izleme odaları ve kişisel uzak kumanda

Tarih: **2026-10-14**, çalışan yayın **0.23.1**. Gereksinimler: **PLY-08, USR-01/02/03, PLY-02/03/06/09, CLI-01, OPS-01/03**.

## Kullanıcı akışı

- Bir dosyayı aç; oynatıcı başlığındaki **Birlikte** simgesinden **Birlikte izle / dinle** ile oda kur. Hane hesabının tam adını girerek davet et. Davetli kendi **Birlikte** ekranında **Katıl ve oynat** seçer.
- Oda başlangıçta duraklatılır. Ev sahibi oynatır; çevrimiçi oynatıcılar aynı konuma hazır olduğunu bildirdiğinde iki saniye ileri zamanlı ortak başlangıç verilir. Tarayıcı sesli otomatik oynatmaya izin vermiyorsa katılımcı **Bu cihazda oynatmaya izin ver** düğmesini kullanır.
- Oynatma/duraklatma, seek ve hız ortak; ses/altyazı/kalite her katılımcıya özeldir. Ev sahibi diğer katılımcılara ortak kumanda izni verebilir ve daveti kaldırabilir.
- **Uzak kumandayı aç**, o açık web oynatıcısını yalnız aynı Home hesabının diğer girişlerine gösterir. Başka cihazda **Birlikte → Kumandayı aç** ile oynatma/seek/hız/ses/sessiz ve dosya seçimi yönetilir. Kumanda ekranı medya almaz, ek playback slot'u kullanmaz.
- **Paylaşılan dosyayı değiştir**, yeni dosyayı tüm oynatıcılara taşır. Yeni dosya tüm davetli hesaplara açık olmalıdır. Dosya başına ortak kaynak ve zaman çizelgesi kullanılır; kişisel kuyruk otomatik olarak tüm katılımcılara yayımlanmaz.
- Müzik de ortak HTML medya/direct-HLS oynatıcısında açılabilir; kesintisiz PCM oynatıcıdaki **Özgün oynatıcı · birlikte dinle / kumanda** ile geçilir. PCM albüm kuyruğunu sample-accurate çoklu cihaz yayınına dönüştürme iddiası yoktur.

## Yetki ve oturum

1. Oda kurmak için bu giriş/cihaz/istemciye ait hazırlanmış bir oynatma lease'i gereklidir. İnternet edinmesi, bir odaya davet edildiği için başlatılmaz.
2. Oda listesi, ayrıntı, olay geçmişi, davet, katılım ve komutlar kendi hesap/medya kapsamını kontrol eder. Yetkisiz hesap oda kimliğiyle başlık, katılımcı veya akış elde edemez. Yönetici olmak başka hesabın uzak kumandasına katılma izni vermez.
3. Her katılımcı normal playback admission/kotasını kullanır. Direct/HLS URL'leri kendi girişine bağlıdır; arkadaşın akışını kullanmak reddedilir. Controller rolü oynatma açamaz.
4. Yeni oynatma hazırlığı ayrıca oda/member/client-revision/source-epoch'a bağlıdır. Admission, yayın ve byte okuma sırasında üyelik geçerli olmalıdır. Kalite/seek yenilemesinde eski kapanan lease, yeni hazırlanmakta olan lease'i kapatmaz; oda kapatma ise henüz üyelik heartbeat'ine bağlanmamış hazırlığı da iptal eder.
5. Bütün mutasyonlar cookie/CSRF ve istemcinin beklediği user/session kimliğini taşır. Eski profilden yola çıkmış komut başka girişe uygulanmaz. Başka girişin member/lease kimliği ödünç alınamaz.
6. Çıkarılan katılımcının odaya bağlı akışları kapatılır ve geç komutları uygulanmaz. Ev sahibinin odanın kurulduğu girişinden çıkması veya bu oturumun iptali odayı kapatır. Kaynak fingerprint/eşleşme/ACL değişimi de eski odayı geçersiz kılar.

## Zaman çizelgesi ve toparlanma

- Sunucu `position + anchorUnixMs + rate + state` zaman çizelgesinin otoritesidir. Durum revizyonu, hazır olma bariyeri (`syncRevision`) ve kaynak/yeniden başlangıç nesli (`epoch`) ayrı izlenir.
- Komutlar beklenen durum revizyonunu taşır. Eşzamanlı zıt komutlardan yalnız biri uygulanır; diğeri güncel durumu gösterip yeniden kullanıcı seçimini bekler. Komut ID/hash/sonuç revizyonu aynı SQLite transaction'ında kaydedilir. Kaybolmuş yanıtta aynı ID ile retry tekrar seek/play yapmaz.
- Heartbeat sıra numarası, geç gelmiş eski konum/hazır olma raporunu engeller. Eski sync/epoch, yeni dosyanın hazır olduğunu ilan edemez. Katılım ve ayrılma ayrıca monoton istemci niyetiyle sıralanır; geç join yanıtı kapanmış/yeni katılımı canlandıramaz.
- İstemci sunucu saatini RTT orta noktasıyla yaklaşık eşler; istemci duvar saati değişimi yerine `performance.now()` kullanır. Küçük farkta ±%4 hız düzeltmesi, büyük farkta seek; tekrar seek için üç saniye bekleme vardır. Native medya/event gecikmesi gerçek cihaz kabulünde ölçülür; frame/sample-perfect senkron iddiası yoktur.
- Bağlı oynatıcı buffer'a girerse grup aynı konumda bekler. On beş saniyedir rapor vermeyen katılımcı hazır olma bariyerine dahil edilmez; yeniden bağlanınca tekrar hazırlanır. Ev sahibinin tüm oda bağlantıları kesilirse oda duraklatılır.
- İstemci sekiz saniyedir doğrulanmış durum alamadığında yerel oynatmayı duraklatır. Sunucu yeniden başlarsa oda duraklatılmış olarak saklanır; kapalı süre izlenmiş süreye eklenmez. Eski stream lease'leri kapanır; yeni epoch ile akış hazırlanır ve ev sahibi yeniden oynatır.
- **0.23.1:** aynı epoch içinde kota, pause-timeout veya elle kapatılmış lease otomatik yeniden açılmaz. Oynatıcı açık hata ve kullanıcıya ait yeniden dene eylemini gösterir. Otomatik akış yenileme yalnız gerçek sunucu restart epoch'una bağlıdır.
- Oda durumu bir saniyelik sınırlı bakım döngüsüyle, istemci ise iki saniyelik heartbeat ve mevcut SSE'nin yalnız `rooms.changed` invalidation olayıyla toparlanır. SSE kişisel başlık/davet/token taşımaz.

## Kalıcılık ve sınırlar

Şema **26 → 27**, `027_watch_rooms.sql`: `watch_rooms`, `watch_room_clients`, `watch_room_commands`; playback lease'lerine oda/member/epoch bağı. Oda verisi, en fazla 16 etkin katılımcısı olan bir JSON aggregate olarak saklanır. İstemci niyeti, oda değişikliği, komut receipt'i ve oda kaynaklı lease iptali aynı transaction'dadır.

- Hesap başına **4**, sunucuda **32** etkin oda; oda başına **12 oynatıcı / toplam 16 katılım / 32 hesap daveti**.
- En fazla **24 saat** oda ömrü; hiçbir heartbeat/komut yoksa **30 dakika** sonra kapanır. Normal playback'in iki dakika idle ve on beş dakika pause sınırları geçerlidir; bu kotalar oda ile aşılmaz.
- Son **256 komut receipt'i / oda**, UI'da son 32 olay. Kapalı oda geçmişi en fazla **7 gün / 500 oda**. Eski istemci niyeti, giriş oturumu kapanana kadar tekrarları engeller; giriş başına 1024 / global 20000 sınırı vardır.
- Kimliği doğrulanmış giriş başına **1200 durum isteği/dakika**, hesap başına **120 kumanda/katılım komutu/dakika**; sunucu HTTP gövde sınırı 64 KiB. Bekleyen sınırsız mesaj kuyruğu veya oda başına yeni FFmpeg servisi yoktur.

## Kod ve API

- `Watching/WatchRoomService`, `WatchRoomEndpoints`, `WatchRoomDocument`; `MediaStore.WatchRooms`; mevcut `PlaybackSessions` admission/yayın/iptal hattı.
- `/api/watch/rooms`, `/rooms/{id}`, `/rooms/{id}/events`, `/rooms/{id}/join`, `/rooms/{id}/members/{member}/heartbeat`, `/rooms/{id}/commands`, `/clients/{client}/leave` (`/api/watch` önekiyle).
- `web/src/watch/WatchController.ts`: renderer/controller ortak durum makinesi, monoton saat ve sıra koruması. `WatchRooms.tsx`, `Player` ve `usePlayback`: gerçek kullanıcı yüzeyi ve medya köprüsü.
- Kalite, dil ve altyazı tercihleri her cihazın mevcut FFmpeg/hls.js kararını kullanır; kaynak kütüphane salt okunur kalır.

## Doğrulama

Odaklı **9 oda backend/native testi** ve **11 üretim controller testi**: davet/ACL/CSRF, aynı hesap farklı giriş sınırı, kota, bağımsız direct/HLS, CAS ve tekrar, hazır bariyeri, buffering, eski heartbeat, üyelik kapanışı, kalite hazırlığı ve geç yayın engeli; istemci saat kayması, seek, ağ kesintisi, autoplay, stale response, kumanda ekranında 0 yerel akış ve aynı epoch'ta kasıtlı kapatmayı geri açmama.

- **259/259 backend/native, atlanan 0:** 0.23 ana yayını; `artifacts/test-results/home-0.23.0-full.trx`. Gerçek Kestrel restart/çökme, eski epoch reddi ve şema-26 offline grant/receipt/politika/oynatma geçmişi korunumu dahil.
- **38 Node kontrolü:** 11 oda controller + 12 offline + 8 push + 7 ses. 0.23.1 istemci düzeltmesi sonrası 11/11 oda testi ve frontend/worker üretim build geçti. **0.23.1 yayımlanmış süreç restart/çökme testi** ayrıca geçti (`home-0.23.1-published-room.trx`); genel backend/native kanıtı yukarıdaki 0.23 ana koşusudur.
- Windows x64, Linux x64 ve Linux ARM64 için **0.23.0 ve 0.23.1** imzalı paketleri üretildi. **0.22.0/şema 26 → 0.23.0/şema 27 → önceki veri/sürüm** ve **0.23.0 → 0.23.1 → önceki veri/sürüm** update/rollback geçti. Her iki geri dönüşte hesap korundu, restore eski oturumları iptal etti. Kanıtlar `artifacts/update-roundtrip-0.22.0-0.23.0.json`, `artifacts/update-roundtrip-0.23.0-0.23.1.json`.
- Canlı önizleme **0.23.1 / şema 27**. Sahip oturumu korundu; yeni oda istemcisi ve önceki offline worker sunuluyor. **0 oda/komut/oda istemcisi, 0 oynatma, 0 indirme, 0 etkin sentetik kütüphane ve 0 FK ihlali**; VAD CPU kullanılabilir (`artifacts/live-runtime-v0231.json`). Doğrulama canlı oda/oynatma veya internet medya indirmesi başlatmadı.
- Linux/Docker runtime ve gerçek çoklu tarayıcı/cihaz kabulü açıktır. Browser suite çalıştırılmadı; kullanıcı adımları aşağıdadır.

## Kaynaklar — 2026-10-14

- [Plex Watch Together](https://support.plex.tv/articles/watch-together/): davet, paylaşılan medya erişimi, lobi ve ortak transport davranışı. Sayfanın **2025-02-25** notu yeni Plex deneyiminde bazı platformların bu özelliği kaldırdığını, web'in devam ettiğini söylüyor. Home gereksinimi bağımsızdır; eski platform listesinden bugünkü eşdeğerlik çıkarılmadı.
- Context7 `/dotnet/aspnetcore.docs`: doğrulanmış kullanıcıya göre `AddPolicy` / `RateLimitPartition.GetFixedWindowLimiter`, middleware sırası, sıfır kuyruk.
- Context7 `/reactjs/react.dev`: immutable/cached `useSyncExternalStore`, kararlı subscribe ve cleanup; kurulu React tipleri.
- Context7 `/tanstack/query`: `enabled`, `refetchInterval`, query `AbortSignal`; kurulu v5 tipleri. Yeni runtime/test bağımlılığı eklenmedi; React/Query/hls.js mevcut lisans envanterindedir.

## Kullanıcı kabulü — ACCEPT-23

1. İki hesapta aynı gerçek dosyaya erişim ver. İlk cihazda oynatıcıdan oda kur, diğer hesabı davet et; ikinci cihaz **Birlikte** ekranından katılsın.
2. Gerekirse her cihazda oynatma izni ver. Oynat/pause/seek/hız ve cihazlara özel farklı altyazı/kaliteyi dene. Buffer/bağlantı kesintisi sonrası ortak konumu kontrol et.
3. Ev sahibi konuk kumandasını açıp kapatsın; sonra katılımcıyı çıkarsın. Çıkarılan cihaz odayı veya eski akışı kullanamamalı.
4. Kendi hesabının diğer cihazından **Uzak kumandayı aç**ılmış oynatıcıyı yönet. Ses, dosya değiştirme ve hedef cihazda kapatma/profil geçişini kontrol et.

Cast/AirPlay/DLNA/Quick Connect protokolleri, native TV istemcileri ve kalan ürün kapıları [panoda](product/execution.md) açık kalır.
