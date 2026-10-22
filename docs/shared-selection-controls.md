# Uygulama geneli açılır seçimler — UI-CONTROLS-01

Tarih **2026-10-18**, hedef **0.29.1**, şema **39**. Gereksinimler **CLI-01/05**, oynatıcıda **PLY-06/08**. Kullanıcı geri bildirimi yalnız Keşfet'e değil bütün uygulamaya aittir.

## Teşhis ve karar

38 TSX dosyasında **92 tekli seçim** native HTML `<select>` kullanıyordu. Kapalı kutuların CSS'i modern görünse de açık menüler işletim sisteminin/tarayıcının native popup'ıydı; popup genel CSS ile güvenilir biçimde kaplanamaz. Bu nedenle yalnız renk/border override'ı eklemek yerine gerçek açık panel renderer'ı değiştirilir.

**HomeSelect** kurulu **HeroUI 3.2.6 / React Aria Select + ListBox** kullanır. Trigger, portal popover, klavye/typeahead/focus/escape/dışarı tıklama, selected indicator ve disabled state aynı accessible library primitive'indedir. Yeni bağımlılık yok.

Tek bir **home-ui-controls** derleme aşaması, mevcut Home yerelleştirme aşamasından önce, dev ve production'da bütün tekli `<select>` JSX'ini HomeSelect'e bağlar. Kaynak JSX'i native form authoring/tip denetimi için kalır; gerçek renderer ortak bileşendir. Bu, runtime DOM patch'i veya global click yakalama değildir. Mevcut `value`, `disabled`, `required` ve `target.value` kullanan komut handler'ları korunur. Kaynak `<option>` elemanları yalnız seçim verisidir; görünür native dropdown oluşturmaz. Eksik accessible label build hatasıdır; mevcut label span'ından türetilen aria-label da yerelleştirme aşamasına girer.

`selectModel.ts` değerleri string olarak korur; boş placeholder ve sayısal 0 ayrı kimliktir, duplicate value'da ilk seçenek korunur. Anahtar prefix'i UI içinde kalır; Home komutuna özgün değer gider. Payload yalnız typed `{target:{value}}` olup DOM event/HTMLSelectElement olarak cast edilmez. Bilinmeyen veya disabled key komut üretmez. Gelecekte `ref/onBlur/currentTarget/selectedOptions` gibi tekli native DOM API'leri eklenirse ortak renderer'a açıkça destek eklenmelidir; bütün uygulama audit testi bu değişimi sessizce kabul etmez.

Episode canonical mapping'deki **1 kalıcı `multiple size=3` listesi** dropdown değildir; mevcut selectedOptions/range davranışı korunur ve liste renkleri ortak yüzeye uyarlanır. Native `<select>`in React Aria'nın accessibility/form iç uygulamasında bulunması, eski OS popup'ının kullanıcıya açılması anlamına gelmez. Required boş seçim null state'tir; named form alanına UI key değil özgün değer gönderilir.

## Ortak görsel sistem

- `home-controls.css`: tek yüzey/background/border/blur/shadow/radius/highlight token seti. Tekli seçim panelleri ve **Daha fazla** menüsü aynı tokenları kullanır; navigation stylesheet'teki yinelenen panel kaplaması kaldırılır.
- **Video.js 10.1.0** menüleri aynı materyali kurulu skin CSS token'larıyla kullanır. Store/iz/kalite/room komutları veya menü layout/animasyon sahipliği değiştirilmez.
- Uzun değer trigger'da ellipsis, panelde satır sarma; viewport sınırı ve kaydırılabilir uzun listeler; dar ekranda en az 44px seçim satırı. Focus görünürdür ve reduced-motion tercihinde ek Home transitions kaldırılır.
- Panel portal ile açılır: detay hero'su/scroll paneli menüyü kesmez. Fullscreen içindeki portal hedefi gerçek `document.fullscreenElement`tir; fullscreen değişirse açık panel kapanır. HeroUI'nin kurulu `UNSTABLE_portalContainer` tipi kullanılır, deprecated olduğu için yükseltmede yeniden kontrol edilmelidir. Native video-only fullscreen ve PiP işletim sisteminin kendi UI'sidir.
- Eski `select` CSS kuralları yeni button/listbox renderer'ını hedeflemez. Bütün select-related styles ve z-index bağlamları incelendi; ortak CSS'de sadece container genişliği/flex/player/hero yerleşimi açıkça aktarılır. Popover z-index 320, mevcut modal/player 100/201 üzerinde, toast 400 altında.

## Kapsam ve doğrulama

Kütüphane filtreleri; keşif; içerik/metadata/sezon/puan/koleksiyon; kaynak/kalite/protokol/indirme; hesap/dil/bildirim/operasyon; müzik oynatıcı/baskı/otomasyon; Video.js ayar yan paneli; altyazı/offline/receiver/oda; ek raf/okuyucu seçenekleri ortak renderer'a dahildir. Test/bildirim/dosya/edinme komutları için yeni yetki veya otomatik işlem eklenmez.

Odaklı Node kontrolleri bütün **92 seçim / 38 dosyayı** gerçekten transform edip label/value/handler sınırlarını tarar; key/empty/zero/disabled/type/locale, navigation ortak yüzeyi ve kurulu Video.js CSS token'ları doğrulanır. Frontend/worker üretim derlemesi, .NET publish/imzalı paket ve canlı shell/veri korunumu kayıtları finalde eklenir. **Browser/device görsel ve klavye kabulünü kullanıcı yapar**; otomatik browser suite veya internet medya edinmesi yoktur. VERIFY-27 geniş regresyonu açık kalır.

Kaynaklar 2026-10-18'de Context7 ve kurulu types ile okundu: [HeroUI v3 Select](https://heroui.com/en/docs/react/components/select), [v3 migration](https://heroui.com/en/docs/react/migration/select), [Video.js v10 skin customization](https://videojs.org/docs/guides/customize-skins/). Mevcut [bağımlılık/lisans envanteri](dependencies.md) geçerlidir; dataset/kod paketi alınmadı.

## Final build — 2026-10-18

**46/46 Node, 0 atlanan**: ortak renderer/uygulama-geneli audit/Video.js token kontrolleri, önceki yerelleştirme/keşif/navigation/hero, gerçek Video.js store/room/offline kontrolleri geçti. Frontend ve worker tip kontrolü, Vite üretim build ve .NET publish başarılı; npm audit 0 açık. Bu UI-only dilimde geniş backend regresyonu çalıştırılmadı; son tam regresyonlu sürüm 0.26.0 olarak kalır.

İstemci `/assets/index-D8dwz_YN.js`, lazy player `/assets/Player-DFydDtUU.js`, ortak UI `/assets/ui-Bk5WZT3J.js`, stylesheet `/assets/index-gNVYbIt-.css`, PWA **d72fa5707df397d4**. İmzalı Windows paket `artifacts/packages/preview-v0291/home-0.29.1-dev.1-win-x64.zip`: **837 doğrulanmış dosya / 74.849.669 byte**. Canlı yayın/veri korunumu aşağıya eklenir; browser kabulü kullanıcıdadır.

Uygulama-geneli transform envanteri `artifacts/controls-scope-v0291.json` içinde 38 dosyanın seçim sayılarıyla kaydedildi. Paketin SHA-256 manifesti `91b278fdbd1f8a1d9343f8eec76b705230e4c98808fdbf317de26d92f66293e9`; güvenilen imza parmak izi değişmedi (`b6ebb7e0a2b2821afd3143dc3bd9c8b666189d9a41953aa6657cf60d789b566b`).

## Çalışan önizleme

**2026-10-18 20:47 yerel saat**: **0.29.0 → 0.29.1** imzalı güncelleme tamamlandı; mevcut sahip oturumu korundu. **http://localhost:5080**, PID **11212**, release `34192947a40e4571ae38a09e4f0fdd38`. Ortak renderer ve CSS materyali canlı HTTP istemcisinde doğrulandı. Şema **39**, **0 FK ihlali**; güncelleme öncesi **7 medya / 2 istek / 2 indirme / 4 kişisel kayıt** ve metadata `tr-TR` tercihi korundu. Kullanıcının mevcut YTS kaydı paused olarak aynı kaldı; edinme/oynatma komutu verilmedi.

Kanıt: `artifacts/preview-before-v0291.json`, `artifacts/update-live-0.29.1-preview.json`, `artifacts/live-controls-v0291.json`. Rollback **0.29.0**, yedek `40a879178e5f447b9fa98d8886cc431c`; önceki release/yedekler tutuldu. **Ctrl+F5** yeni istemciyi alır. Bu HTTP/build doğrulamasıdır; popup görseli, keyboard/typeahead/focus ve gerçek browser/device kabulü kullanıcıda açık kalır.
