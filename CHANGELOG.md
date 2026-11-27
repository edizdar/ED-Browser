# 📋 ED Browser — Sürüm Notları

---

## v5.0.1 — Yenilenen Çoklu Çözünürlüklü Uygulama Simgesi, Arayüz Amblemi & Tam Web/GitHub Dokümantasyonu
- 🎨 **Yeni Uygulama İkonu (`Assets/edbrowser.ico` & `Assets/edbrowser.png`):**
  - `v3.1.8` sürümündeki 3B metalik mavi-gümüş **"ED"** küre amblemi modern Windows 11 tasarım diliyle yeniden işlendi; 16 px'ten 256 px'e kadar 7 katmanlı hibrit DIB+PNG `.ico` paketi oluşturuldu.
  - Yeni logo Yeni Sekme sayfasına, Kurulum Sihirbazına ve uygulama pencere başlık çubuğuna doğrudan gömüldü.
- 📄 **Dokümantasyon & Web Sayfası Güncellemesi:** GitHub repo açıklaması/etiketleri, [README.md](README.md), [PRIVACY.md](PRIVACY.md) ve `eroldizdar.tr/p/ed-browser.html` için Blogger uyumlu HTML paketi (`docs/eroldizdar-ed-browser.html`) güncellendi.

---

## v5.0.0 — Tüm 14 Yeni Nesil Özellik Sürümü
- 🧩 **Üretkenlik & Sekme Yönetimi:**
  - **Oturum Kurtarma (`RestoreSessionOnStartup`):** Kapanıp açıldığında son sekmeleri, sabitli sekmeleri ve grupları pasif `0 MB` bellek modunda geri getirir.
  - **Dikey Sekmeler (`Ctrl+Shift+V`) & Renkli Sekme Grupları:** Sol kenarda katlanabilir dikey sekme paneli ve sağ tık menüsünden renkli sekme grubu atama.
  - **Bölünmüş Ekran (`Ctrl+Shift+X`):** Tek pencerede iki web sayfasını yan yana gezme ve sol/sağ değiştirme.
  - **Canlı Omnibox Otomatik Tamamlama:** Yazarken yer imleri, geçmiş ve arama motoru önerilerini gösteren odak çalmayan açılır liste.
- 🛡️ **Gelişmiş VPN, Gizlilik & Reklam Filtresi:**
  - **Bölge Filtreli Sunucu Seçici (`Tümü / EU / US / AS`)**, **Canlı Hız Göstergesi (`↓ / ↑ KB/s`)** ve **VPN Kill Switch** (tünel düşerse gerçek IP sızmasını `503` ile keser).
  - **Dinamik Filtre Listesi (Peter Lowe / EasyList)** güncelleme desteği + **Kozmetik Reklam Gizleme (CSS)** + **Site Bazlı Reklam & HTTPS-Only İstisnaları** (kilit ikonu menüsü).
- 📖 **Okuma, Medya, Profiller & Sistem:**
  - **Okuyucu Modu (`Ctrl+Alt+R`)**, **Resim İçinde Resim (PiP) & Medya Merkezi**, **Çoklu Kullanıcı Profilleri (`--profile`)**, **Eklenti Araç Çubuğu Popup İkonları**, **Site Favicon Önbelleği**, **Görev Yöneticisi (`Shift+Esc`)** ve **Uygulama İçi Otomatik Güncelleyici**.

---

## v4.4.1 — `ERR_TUNNEL_CONNECTION_FAILED` Düzeltmesi, Çoklu Site VPN Doğrulama & Otomatik Yedekli Failover
- 🐛 **Ücretsiz VPN'de `ERR_TUNNEL_CONNECTION_FAILED` Hatası Giderildi:**
  - Yalnızca `cloudflare.com` testine yanıt verip diğer sitelerde (`ip-adresim.net`, `google.com`) bağlantıyı kesen kısıtlı/bozuk proxy düğümleri artık çoklu hedef HTTPS doğrulaması (`google.com/generate_204` + `cloudflare.com/cdn-cgi/trace`) ile baştan eleniyor.
- 🔁 **Otomatik Yedek Sunucuya Geçiş (`LocalVpnRouterService` Failover):**
  - El sıkışma zaman aşımı koruması (`5,5 sn`) ve çalışmayan bir ücretsiz röleden otomatik olarak sıradaki doğrulanmış yedek sunucuya geçiş (`FailoverServerPromoted`) eklendi; rozet yeni sağlıklı ülkeyi otomatik güncelliyor.
  - Açılışta eski (`v4.4.0`) önbellekten kalan bozuk sunucular sıfırlanıp sağlıklı sunucu otomatik doğrulanır.

---

## v4.4.0 — Tek Tıkla Anlık VPN, Sıfır Yeniden Başlatmalı Router & Yerleşik Sunucu Tarayıcısı
- ⚡ **Yeniden Başlatmasız Anlık VPN Anahtarı (`Services/LocalVpnRouterService.cs`):**
  - Tarayıcı başlangıcında `127.0.0.1` üzerinde hafif asenkron HTTP/CONNECT & SOCKS5 (RFC 1928/1929) yönlendirici başlar; WebView2 bu yerel yönlendiriciye bağlı kalır.
  - Üst menüdeki `VPN` rozetinden **VPN Koruması** açılıp kapatıldığında veya ülke/tünel değiştirildiğinde **tarayıcı kapanmadan** bir sonraki bağlantı anında yeni tünel üzerinden geçer; adres çubuğu rozeti canlı olarak `VPN • DE`, `WARP ✓`, `TOR ✓` şeklinde güncellenir.
- 🌍 **"VPN Profili Nereden Bulacağım?" Sorununa Yerleşik Çözüm (`Sunucu Bul`):**
  - VPN paneline **Canlı Ücretsiz VPN Sunucu Tarayıcısı** eklendi (`ProxyService.DiscoverVerifiedFreeRelaysAsync`): GeoNode ve ProxyScrape listelerinden çekilen SOCKS5/HTTP adayları `cloudflare.com/cdn-cgi/trace` üzerinden paralel HTTPS gecikme/ülke doğrulamasından geçirilerek çalışan sunucular anında listelenir ve tek tıkla bağlanır.
  - Ayrıca tek tıkla **WARP İndir**, **Tor İndir**, **VPN Eklentisi (Chrome Web Store)** ve `+ Ekle` ile özel WireGuard/SOCKS5/HTTPS profili ekleme kısayolları panel içine eklendi.
- 🐛 **VPN Paneli Açılır Liste (ComboBox) Kapanma Hatası Düzeltildi:**
  - `VpnPrivacyPopup` içinde açılır listeye tıklandığında dışarı tıklama algılayıcısının popup'ı erken kapatmasına yol açan hata, `_comboDropDownOpen` korumasıyla tamamen giderildi.

---

## v4.3.0 — VPN Tünel Merkezi, Kapsamlı Gizlilik Kalkanı & Şeffaflık Raporu
- 🔐 **Opera/Brave Tarzı VPN & Gizlilik Kalkanı (`VPN` Omnibox Rozeti):**
  - **4 Tünel Modu:** Doğrudan, Sistem Proxy, **Özel VPN / SOCKS5 / HTTPS Profilleri** (kullanıcı adı/şifre desteği dahil), **Tor Ağı (`127.0.0.1:9050` / `9150`)** ve **Cloudflare WARP (`127.0.0.1:40000`)**.
  - Yerel WARP/Tor portlarının açık olup olmadığını otomatik algılama, çıkış IP & ülke doğrulama (`FetchTraceAsync`), ve `Chrome Web Store` VPN eklentilerine hızlı erişim.
- 🛡️ **Gelişmiş Gizlilik Sertleştirme:**
  - **HTTPS-Only Modu:** Harici `http://` bağlantıları otomatik `https://` yapar (SSL desteklemeyen adreslerde güvenli geri dönüş korumalı).
  - **URL İzleme Parametre Temizleyici:** `utm_*`, `fbclid`, `gclid`, `dclid`, `yclid`, `msclkid`, `mc_cid`, `mc_eid`, `igshid`, `si`, `mkt_tok` parametrelerini otomatik kırpar.
  - **WebRTC Yerel IP Sızıntı Koruması:** `--force-webrtc-ip-handling-policy=disable_non_proxied_udp` + `--enforce-webrtc-ip-permission-check`.
  - **Parmak İzi & GPC/DNT Koruması:** Tüm isteklerde `DNT: 1` / `Sec-GPC: 1` başlıkları, `getBattery()` engelleme.
  - **Çoklu Şifreli DNS (DoH):** Cloudflare (`1.1.1.1`), Quad9 (`9.9.9.9`), AdGuard DNS veya Sistem DNS.
  - **Çıkışta Gezinme Geçmişini Otomatik Temizleme** seçeneği + tam şeffaflık için [PRIVACY.md](PRIVACY.md).

---

## v4.2.0 — Gerçek CDP DevTools, Chrome/Edge Eklenti Mağazası, 6 Tarayıcı UX Standartları & Sürükle-Bırak Yenilemesi
- 🛠️ **Gerçek Chrome Tarzı Geliştirici Araçları (`F12` / `Ctrl+Shift+I` / Sağ Tık → İncele):**
  - Eski OS pencere bindirme (HWND overlay) yaklaşımı tamamen kaldırıldı; `CoreWebView2` Chrome DevTools Protocol (CDP) kullanan yerleşik `DevToolsPanel` oluşturuldu.
  - **Öğeler** (DOM ağacı, hesaplanan CSS stilleri, canlı satır içi stil editörü), **Konsol + JS REPL**, **Ağ** (istek/yanıt gövdesi ve önbellek devreden çıkarma), **Depo** (Çerezler, LocalStorage, SessionStorage), **Kaynak** (`Ctrl+U`), **Öğe Seçici (`Ctrl+Shift+C`)** ve **Mobil/Tablet Cihaz Emülasyonu**.
  - Sağa (`Dock Right`), alta (`Dock Bottom`) veya harici Tam Chromium DevTools penceresine tek tıkla geçiş.
- 🧩 **Chromium Eklenti Motoru & Mağaza Entegrasyonu (`Ctrl+Shift+E`):**
  - `CoreWebView2EnvironmentOptions.AreBrowserExtensionsEnabled = true` ve `AddBrowserExtensionAsync` ile gerçek Chromium eklenti desteği.
  - **Chrome Web Store**, **Microsoft Edge Add-ons**, `.crx` (CRX2/CRX3) / `.zip` paketleri ve açık eklenti klasörü yükleme.
  - Mağaza sayfalarında üstte tek tıkla **`ED Browser'a Ekle`** algılama çubuğu.
- 🎛️ **6 Tarayıcı (Opera, Brave, Yandex, Chrome, Edge, Firefox) Referanslı Üst Çubuk & Menü:**
  - Adres çubuğu yanına **`− %100 +` Yakınlaştırma**, araç çubuğuna doğrudan **Ekran Görüntüsü**, **Tam Ekran** ve **Eklentiler** butonları eklendi.
  - İsteğe bağlı Opera/Yandex tarzı **Sol Kenar Çubuğu (`Ctrl+Shift+L`)** eklendi.
  - Plain `ContextMenuStrip` yerine modern yuvarlatılmış, ikonlu, kısayol etiketli ve web sayfasına tıklanınca anında kapanan **`AppMenuPopup`** getirildi.
  - Yeni özellikler: **Kapalı Sekmeyi Yeniden Aç (`Ctrl+Shift+T`)**, **Sekme Sabitleme & Ses Kapatma (`🔊/🔇`)**, **Yeni Pencere (`Ctrl+N`)**, **Tarama Verilerini Temizle (`Ctrl+Shift+Del`)**, **Sayfayı Kaydet (`Ctrl+S`) / PDF Olarak Kaydet**, **Sayfa Kaynağı (`Ctrl+U`)**.
- 🔖 **Sorunsuz Yer İmleri Sürükle-Bırak:**
  - WinForms OLE `DoDragDrop` kaldırılıp özel tek yüzeyli **`BookmarkBarControl`** ile doğrudan fare yakalamalı sürükleme getirildi: hayalet önizleme, ekleme çizgisi, klasör içi vurgulama ve taşan yer imleri için `»` menüsü. Yan panel `TreeView` sürükle-bırak ve `Kök Dizine Taşı` düzeltildi.

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
