# 0.6 — Kalıcı otomatik arama ve edinme

Başlangıç: **2026-10-13**, **0.7.0 içinde yayımlandı; fixture/backend doğrulandı**. ID: IDX-04/05, QUA-02/04/05/06, ACQ-02, OPS-01/07.

Her içerik için yönetici tarafından etkinleştirilen ayrı otomasyon kuralı; varsayılan **kapalı**. Etkin arama yalnız bildirim üretebilir; otomatik edinme ayrıca kullanıcının seçtiği bir tercihtir. Onaysız, gelecek tarihli veya takip dışı kapsam otomatik edinilmez.

SQLite iş kuyruğu; hedef başına tek etkin iş, kural/profil snapshot'ı, niyet revizyonu, süreli claim, restart toparlanması. Sürüm seçimi mevcut kaliteyi düşürmez; cutoff, gecikme/minimum yayın yaşı, günlük edinme sınırı ve başarısız sürüm engelleme uygulanır.

İndirme kaydı oluşturulurken kural ve hedefin hâlâ geçerli olduğu aynı yazma transaction'ında kontrol edilir. Ağ isteği sürerken kapatılan otomasyon eski sonuçla transfer başlatamaz. Kullanıcı iptali otomatik tekrarın önüne geçer.

Geliştirme kontrolleri HTTP/transfer fixture'larıyla yapılır. Canlı sunucuda kural veya internet medya transferi geliştirme aracı tarafından etkinleştirilmez. Gerçek indexer kabulü kullanıcı hesabı seçilince yapılır.

`AutomationTests` altı senaryo: kapalı/bildirim modu, gerçek kalıcı transfer komutu, arama sırasındaki iptalin DB write-fence'i, gecikme/yeniden arama, engel listesi, gelecek/onaysız/cutoff içeriğinin atlanması, günlük bölüm-job kotası ve kullanıcı iptali/restart. `SourceTests` standart RSS GET'ini de doğrular.

Sınırlar: yükseltme şu aşamada bilinen çözünürlüğü artırır; aynı çözünürlükte PROPER/custom-format yükseltmesi ve gelişmiş delay/protokol profilleri açık. RSS ilgili yeni sürümü görünce kalıcı hedef aramasını uyandırır. Aynı anda bir otomasyon araması, iki toplam kaynak araması; 2000 etkin hedef işi ve 30 günlük terminalsiz-payload geçmişi sınırı vardır. Canlı önizlemede otomasyon kuralı ve indirme sayısı 0 olarak doğrulandı.
