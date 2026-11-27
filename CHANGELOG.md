# 📋 ED Browser — Sürüm Notları

---

## v4.1.0 — Ultra Düşük RAM Mimarisi, Kurulum Sihirbazı & Açık Kaynak Sürümü
- 🚀 **Yeni Mimari (WinForms GDI+ + Edge WebView2 Chromium Motoru):** Electron kaldırıldı; arayüz tamamen yerel GDI+ vektör çizime taşındı.
- 📦 **Kurulum Sürümü (`ED.Browser.Setup.4.1.0.exe`):** Masaüstü & Başlat Menüsü kısayolları, Windows Uygulamalar listesi kaydı ve `Uninstall.exe --uninstall` kaldırma sihirbazı eklendi.
- 🔔 **Otomatik Güncelleme Bildirimi:** Açılışta ve `Ayarlar → Sistem & Hakkında` içinden GitHub Releases kontrolü (rate-limit korumalı fallback dahil) + uygulama içi güncelleme çubuğu.
- ⚡ **Gelişmiş RAM Yönetimi:**
  - Pasif sekmelerde `MemoryUsageTargetLevel.Low`
  - Otomatik `TrySuspendAsync()` sekme uyutma (`💤`) ve derin bellek boşaltma (`❄️` — `DeepUnload`)
  - Araç çubuğunda canlı `⚡ XX MB` bellek göstergesi ve tek tıkla RAM temizleme
  - `--renderer-process-limit` ayarı
- 🎨 **Modern Arayüz & DPI Uyumluluğu:** Vektör ikonlu hap omnibox, Chrome tarzı sekmeler, koyu/aydınlık tema uyumlu açılır menüler ve 2 sütunlu ayarlar penceresi.
- 🛠️ **Chrome Tarzı Geliştirici Araçları:** `F12`, `Ctrl+Shift+I` ve sağ tık → `İncele` ile tarayıcının sağına sabitlenen, sürüklenebilir ayırıcılı DevTools paneli.
- 🕐 **Gelişmiş Geçmiş:** Anlık arama kutusu, satır üzeri `×` ile tekil kayıt silme ve sağ tık menüsü (`Bu Sitenin Tüm Geçmişini Sil` dahil).
- 🏠 **Kullanıcı Kontrollü Yeni Sekme:** Varsayılan sabit siteler kaldırıldı; `+ Site Ekle` ile kendi site kısayollarını ekleme, düzenleme ve kaldırma.
- 🔖 **Yer İmleri İyileştirmeleri:** Yer imi çubuğunda basılı tutup sürükleyerek sıralama ve klasör içine taşıma, klasör menüsünün fare çıkınca otomatik kapanması, yıldız penceresinden `+ Yeni Klasör` oluşturma.
- 🔐 **Güçlendirilmiş Şifre Kasası:** Sabit şifre anahtarı yerine **AES-256-GCM + Windows DPAPI** ile kullanıcıya özel yerel şifreleme.
- 🛡️ **Genişletilmiş Reklam Engelleyici:** 150+ reklam, takipçi ve telemetri ağı desteği.
- 🌐 **Proxy & Cloudflare DoH:** Doğrudan / Sistem / Özel HTTP-SOCKS5 / Tor (`127.0.0.1:9050`) + Cloudflare DoH (`1.1.1.1`) ve IP test aracı.

---

## v3.1.8 / v3.1.7 *(Eski Electron Nesli)*
- Versiyon yazım hatası düzeltildi
- Ara versiyonlar bu sorunu çözmek içindir

### v3.1.3
- 🐛 İndirilenler paneli açıldığında liste güncel veriyi göstermiyor sorunu düzeltildi
- 🐛 Adres çubuğuna sağ tıkla → Yapıştır ve Git düzeltildi
- ✨ Adres çubuğu sağ tık menüsüne Kes, Kopyala, Tümünü Seç, Temizle eklendi
- 🐛 Tam ekranda ESC tuşuyla çıkış exe versiyonunda çalışmıyordu, düzeltildi
- 🐛 Görev yöneticisinde uygulama adı görünmüyordu, düzeltildi
- ✨ Yeni sekme butonu sekme çubuğuna taşındı
- 🐛 Resimleri Yükle ayarı kapalıyken düzgün çalışmıyordu, düzeltildi
- ✨ Portable sürüm eklendi

### v3.1.1
- ✨ Ekran görüntüsü özelliği eklendi (Tüm sayfa veya alan seçerek kaydetme)
- ✨ Windows Varsayılan Tarayıcı kaydı iyileştirildi

### v3.1.0
- ✨ Şifre Yöneticisi eklendi (AES-256 şifreleme, otomatik doldurma)
- ✨ Diğer tarayıcıdan içe aktarım (Chrome/Edge/Firefox yer imleri ve şifreler)

### v3.0.0 — İlk Sürüm
- 🚀 ED Browser ilk sürüm yayınlandı

---

*ED Browser — Geliştirici: [Erol Dizdar](https://eroldizdar.tr)*
