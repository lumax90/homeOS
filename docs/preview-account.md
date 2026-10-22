# Yerel önizleme hesabı değişikliği

**2026-10-19**, kullanıcı isteğiyle mevcut yerel önizleme sahibinin görünen/giriş adı **home** yapıldı ve istenen parola uygulandı. Parola bu belgede veya doğrulama kanıtında saklanmaz.

- Yalnız `.data/preview` içindeki mevcut **user ID 1** değiştirildi; yeni hesap oluşturulmadı.
- Kullanıcıca istenen kısa parola yalnız bu hesap için bir defalık bakım istisnasıdır. Genel **10–256 karakter** parola kuralı, ürün kodu ve runtime **0.32.0** değişmedi.
- ASP.NET Identity V3 PBKDF2/HMAC-SHA512 formatı, mevcut iterasyon parametresi ve yeni rastgele salt korundu. Parola hash olarak saklanır.
- Aktif medya/oyun yokken yapıldı. Önceki sahibin oturumları iptal edildi; yeni bilgilerle gerçek `/api/auth/login` ve `/api/session` doğrulandı.
- Rol/izinler, kütüphane yetkileri, medya, istekler, indirme niyetleri, kişisel kayıtlar ve oyun save/state verileri transaction öncesi/sonrası karşılaştırılarak korundu.
- Kanıt: `artifacts/preview-credential-change.json`. Geçici bakım betiği yalnız onaylı Temp/opencode alanındadır; uygulamaya paketlenmez.

Bu parola internet erişimine uygun değildir. Uzak erişim açılmadan önce güçlü parola seçilmelidir. Eski yayın belgelerindeki hesap adları/parolalar tarihsel kayıttır; mevcut giriş bilgisi olarak kullanılmamalıdır.
