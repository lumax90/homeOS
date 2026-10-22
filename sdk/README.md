# Home Source Adapter SDK v1

Home kullanıcı tarafından yönetilen JSON/RSS kaynak adaptörlerini çalıştırır. Kurulum: **Kaynaklar → Gelişmiş → Adaptör JSON tanımını yükle**. Yüklenen adaptör **Site seçerek kaynak ekle** listesine girer; kaynak adresi ve gerekiyorsa anahtarı girilip test edilir. Mevcut bağlantıya elle atama **Gelişmiş → Ağ/kimlik ayarları** alanındadır.

Sözleşme: `source-adapter-v1.schema.json`. `schemaVersion=1`; adaptör kimliği tekildir, `version` semantik üç sayıdır. `license` ve HTTPS `homepage` zorunludur. JSON tanımı yürütülebilir script içermez.

- `searchPath`: kaynak API adresiyle aynı origin'de göreli yol.
- `queryParameter`, `offsetParameter`, `limitParameter`: gerçek sorgu/paging parametre adları.
- `itemsPath`: noktayla ayrılmış JSON dizi yolu; boşsa yanıt kökü. Sayısal bileşenler dizi indeksidir.
- `fields`: `id`, `title`, `url`, `size`, `seeders`, `published`, `infohash` alanlarının JSON yolları. `size` byte sayısı veya KiB/MiB/GiB metni olabilir.
- `downloadTemplate`: `{id}` URL-encode edilerek doldurulur. Sonuç URL'leri yine kaynak ve ek izinli host kontrolünden geçer.
- `authentication`: none/query/bearer/header. Anahtar bağlantı formunda korumalı saklanır; manifestte sır bulunmamalıdır.
- `protocol`: torrent/usenet. Kaynağın Torznab/Newznab protokol ailesi bununla eşleşmelidir.
- RSS adaptörü standart item/guid/title/link/enclosure/pubDate ve seeder/size/infoHash alanlarını okur.

Arama sonucu `RawRelease` modeline, ardından Home kalite/kimlik/bütçe ve torrent/NNTP import hattına girer. Adaptör kaynak kütüphaneye yazamaz. Bir kaynak hatası diğerlerinin sonuçlarını geçersiz kılmaz. Kota, timeout, proxy ve redaksiyon ortak transport'tadır.

Tanım güncellemesi aynı `id` için yeni revizyon üretir, caps ve başlamamış kaynak sonuçlarını geçersiz kılar. Kaynak revizyonu sorgu ve sonuç yayınında yeniden denetlenir; eski bir sorgu sonradan sonuç yayımlayamaz. `searchPath` `/` olabilir; `//`, ters eğik çizgi ve origin değiştiren yollar reddedilir. Kullanımda adaptör kaldırılmadan önce kaynak bağlantısı değiştirilir. Kurulum/kaynak revizyonu/origin sınırları odaklı kontrollerden geçti; geniş JSON/RSS sözleşme matrisi ve gerçek kullanıcı bağlantısı sonraki doğrulama turundadır.
