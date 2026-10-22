# Seçili UI dili ve sabit ayar çerçevesi — LOCALIZE-02 / SETTINGS-02

Tarih **2026-10-18**, hedef **0.31.1 / şema 39**. Gereksinimler **CLI-05, CLI-01**. Kullanıcı İngilizce seçiliyken Ayarlar/Oynatma/Bildirimler gibi Türkçe UI başlıkları ve sekmeye göre değişen ayar pencere boyutu bildirdi.

## Dil hatası ve kalıcı denetim

`translate()` sözlükte olmayan key'i olduğu gibi döndürüyordu. İlk audit yalnız `çğıöşü` gibi harfler üzerinden kontrol yaptığı için **Ayarlar, Oynatma, Bildirimler, Film ve dizi bilgileri** gibi ASCII Türkçe metinleri hiç yakalamıyordu. Bu başlıkların English karşılığı gerçekten eksikti; kullanıcı tercihi veya metadata dili sorunu değildi.

- Eksik UI karşılıkları dolduruldu; Settings/Playback/Notifications/Movie and TV metadata dahil başlık ve dinamik metinler English olur.
- `ui-message-audit.mjs` **semantik UI yerlerini** kontrol eder: JSX metni/visible attribute, explicit translate/notify/error sink, label/title/description ve option/tab label çiftleri. Harf/dil tahmini kullanılmaz.
- UI key'i sözlükte veya açık technical-neutral listesinde yoksa **dev/production build hata verir**. Transport action/ID, kullanıcı/sağlayıcı başlığı ve form değeri UI key'i diye işaretlenmez. Protokol/brand/kod/ünite istisnaları `neutral-messages.json` içinde açık kayıtlıdır.
- Source catalog üstüne extension/correction önceliği vardır. English render edilmiş kendi UI mesajları için English alias → canonical Turkish map eklenir; en/tr geçişinden sonra eski own mesaj da çevrilebilir. Provider/user title'a runtime çeviri uygulanmaz.
- **96 frontend dosyası**, 0 uncovered semantic UI mesajı. İlk inventory `artifacts/i18n-uncovered-v0311.json`, final `artifacts/i18n-audit-v0311.json`. İngilizce/Türkçe settings kategori+açıklama, ASCII missing-key build fail, parametre/transport/user text korunumu ve reverse own-message geçişi unit'te doğrulanır.

Bu UI denetimi backend/provider'dan gelen bilinmeyen serbest metnin evrensel makine çevirisi değildir. Metadata/user content tercihi ve gerçek provider metinleri kendi sınırında kalır; uygulamanın kendi static UI başlıkları seçilen UI dilinden gelir.

## Boyut hatası

Kurulu HeroUI container `flex-col items-center` idi. Container genişliği tanımlı olsa da Home'un aradaki DOM wrapper'ı **auto/intrinsic width** kullanıyordu; aktif panel içeriği değişince gerçek dialog genişliği değişebiliyordu.

- Ara wrapper `home-dialog-frame` olarak işaretlenir, settings bağlamında width/height **100%**, min-width/min-height 0 ve fixed outer frame kullanır.
- Settings container desktop'ta `min(1040px, viewport-48px)` × `min(800px, viewport-64px)`; mobile'da viewport-24px × viewport-32px. Boyut sadece viewport/breakpoint ile değişir, sekme ile değil.
- Settings backdrop mobile padding 12px; fixed-width wrapper flex shrink/grow'a bırakılmaz. Body/grid tracks bounded; uzun içerik `settings-pane` içinde scroll olur. Scrollbar gutter reserve edilir.
- Genel modalın diğer boyut/close davranışına müdahale edilmez; settings'ye scoped kurallar kullanılır.

## Kanıt ve durum

**95/95 Node, 0 atlanan**; strict dictionary audit/frontend-worker build, .NET publish ve imzalı Windows paket başarılı. npm audit 0 açık. Kanıt `artifacts/language-frame-v0311.tap`. Geniş backend regresyonu bu UI-only dilimde yapılmadı; son tam regresyon 0.26.0, VERIFY-27 açık.

Paket `artifacts/packages/preview-v0311/home-0.31.1-dev.1-win-x64.zip`, **837 dosya / 75.082.051 byte**, SHA-256 `41f27dba5dfaf4448ee342840d3e8991fff6e069985a5bb9408812024cf98fd7`. İstemci `/assets/index-CEKE5dDP.js`, CSS `/assets/index-Cz4-JMgD.css`, PWA **66c35ea78240f0ce**.

**Transfer sınırı:** kullanıcının yeni `eee9c5de62814d43b4907c297bba86a1` indirmesi running/downloading olduğu için ilk restart ertelendi. Kullanıcı **“Ben duraklatayım”** dedi ve işin paused olduğu salt-okunur DB/API kontrolünde görüldü. Agent pause/resume veya yeni internet edinme komutu vermedi; kullanıcı pause işleminden sonra imzalı yayın yapıldı.

## Çalışan önizleme ve hedefli DOM kanıtı

**2026-10-18 23:31 yerel saat**: imzalı **0.31.0 → 0.31.1**, PID **18680**, release `f0de3ebfbdb2471e8dd5fb8da2079427`, **http://localhost:5080**. Mevcut sahip oturumu, şema 39/FK 0, **7 medya / 2 istek / 3 indirme / 5 kişisel kayıt**, metadata en-US korundu. İki normal transfer paused kalır; arşivli transfer tutulur. Rollback 0.31.0, backup `12b035e2babc4a1795643a60d6d228a8`.

Kullanıcının mevcut tab/pref'ine dokunmayan geçici background tab'da yeni `index-CEKE5dDP.js` yüklendi. DOM `lang=en`, başlık **Settings** ve **13 kategori** English olarak ölçüldü (Playback/Notifications/Movie and TV metadata dahil). Account/interface/operations geçişinde **outer=937×636, frame=937×636, dialog client=935×634** tüm örneklerde aynı kaldı; ölçü farkı border'dır.

Background tab'daki ilk RAF beklemesi hidden-document nedeniyle timeout oldu; bilinmeyen işlem körlemesine tekrarlanmadı, tab/state incelendi ve sadece synchronous UI/DOM ölçümleri yapıldı. Geçici tab kapatıldı. Dil tercihi değiştirilmedi; locale/acquisition/playback komutu verilmedi. Bu hedefli DOM teşhisidir, screenshot veya otomatik browser suite/device acceptance değildir.

Kanıt `artifacts/update-live-0.31.1-preview.json`, `artifacts/live-controls-v0311.json`, `artifacts/live-language-frame-v0311.json`, `artifacts/preview-before-v0311.json`. Yeni istemci Ctrl+F5 ile alınır; kullanıcı browser/device kabulü ve VERIFY-27 açık kalır.

2026-10-18'de Context7 [i18next resources/fallback/changeLanguage](https://www.i18next.com/overview/api) ve kurulu HeroUI `modal.css` container/dialog tip ve stil sınırları okundu. Yeni dependency veya dataset eklenmedi.
