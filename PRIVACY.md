# 🔒 ED Browser — Şeffaflık & Gizlilik Raporu (`v4.3.0`)

Bu belge **ED Browser** uygulamasının tam olarak hangi verileri topladığını, neleri **kesinlikle toplamadığını**, hangi gizlilik/VPN korumalarını sunduğunu ve kullandığı **Chromium (Edge WebView2)** motorunun sınırlarını şeffaf biçimde açıklar.

---

## 1. Sıfır Telemetri Taahhüdü
- ED Browser geliştiriciye veya üçüncü taraflara **hiçbir kullanım istatistiği, çökme raporu, ziyaret geçmişi veya analiz verisi göndermez**.
- Tarayıcı profiliniz tamamen yerel olarak saklanır:
  - **Taşınabilir (Portable) Sürüm:** Çalıştırılabilir dosyanın yanındaki `EDBrowserData\` klasörü.
  - **Kurulum Sürümü:** `%LOCALAPPDATA%\EDBrowser4\`.

---

## 2. Şifreli Yerel Şifre Kasası
- Kaydedilen site şifreleri **AES-256-GCM (kimlik doğrulamalı şifreleme)** ile `passwords.json` dosyasına yazılır.
- Şifreleme anahtarı (`vault.key`) sabit bir şifre değildir; her Windows kullanıcı hesabı için **Windows DPAPI (`ProtectedData` / `crypt32.dll`)** ile yerel olarak şifrelenir.
- Bulut senkronizasyonu yoktur; şifreler bilgisayarınızdan dışarı çıkmaz.

---

## 3. Yerleşik Gizlilik Kalkanı Özellikleri (`v4.3.0`)
| Koruma Katmanı | Ne Yapar? | Varsayılan Durum |
| :--- | :--- | :--- |
| **Reklam & İzleyici Engelleyici** | 150+ reklam, analitik ve telemetri alan adını sayfa yüklenmeden `204 Blocked` ile keser (`AdBlockerService`). | ✅ Açık |
| **HTTPS-Only Modu** | Harici `http://` bağlantıları otomatik `https://` yapar (SSL/bağlantı desteklemeyen yerel/özel IP'lerde ve hata veren alanlarda güvenli geri dönüş içerir). | ✅ Açık |
| **URL İzleme Parametre Temizleyici** | Ziyaret edilen adreslerden `utm_*`, `fbclid`, `gclid`, `dclid`, `yclid`, `msclkid`, `mc_cid`, `mc_eid`, `igshid`, `si`, `mkt_tok` etiketlerini otomatik temizler. | ✅ Açık |
| **WebRTC IP Sızıntı Koruması** | `--force-webrtc-ip-handling-policy=disable_non_proxied_udp` ve `--enforce-webrtc-ip-permission-check` ile VPN/proxy altında yerel ağ IP sızıntısını önler. | ✅ Açık |
| **Parmak İzi & GPC/DNT Koruması** | Tüm HTTP isteklerine `DNT: 1` ve `Sec-GPC: 1` başlıkları ekler; `navigator.globalPrivacyControl = true` tanımlar, `getBattery()` API'sini kapatır ve donanım parmak izi yüzeyini azaltır. | ✅ Açık |
| **DNS-over-HTTPS (DoH)** | DNS sorgularını İSS ve yerel ağdan gizler (**Cloudflare `1.1.1.1`**, **Quad9 `9.9.9.9`**, **AdGuard DNS** veya Kapalı). | ✅ Cloudflare (`1.1.1.1`) |
| **Gizli Mod (`Ctrl+Shift+P`)** | Geçici profil dizini + `IsInPrivateModeEnabled = true` ile açılır; pencere kapatılınca geçici klasör diskten silinir, geçmiş/indirme/şifre kaydedilmez. | İsteğe bağlı |
| **Çıkışta Otomatik Temizleme** | Tarayıcı kapatılırken gezinme geçmişini temizler (Ayarlar → Gizlilik & Ağ). | İsteğe bağlı |

---

## 4. VPN / Tünel Seçenekleri (Dürüst Teknik Açıklama)
ED Browser bağımsız bir ücretli VPN sunucu ağı kiralamaz; **tamamen şeffaf ve kullanıcı kontrollü 4 tünel yöntemi** sunar:
1. **Cloudflare WARP Yerel Tüneli (`127.0.0.1:40000`):** Sisteminizde Cloudflare WARP yerel SOCKS5 proxy modunda çalışıyorsa tek tıkla WebView2 trafiğini WARP üzerinden geçirir (`VPN` rozetinden port durumu canlı algılanır).
2. **Tor Ağı (`127.0.0.1:9050` / `9150`):** Sisteminizde Tor servisi veya Tor Browser çalışıyorsa WebView2 trafiğini Tor SOCKS5 tünelinden geçirir.
3. **Özel VPN / SOCKS5 / HTTPS Proxy Profilleri:** Kendi WireGuard/OpenVPN SOCKS5 köprünüzü, SSH tünelinizi veya HTTPS proxy sunucunuzu kullanıcı adı/şifre desteğiyle profil olarak kaydedebilirsiniz (`IP Test Et` ile çıkış IP ve ülkesi doğrudan doğrulanır).
4. **Chrome Web Store VPN Eklentileri:** `VPN Eklentisi Kur` kısayoluyla Chrome Web Store'daki VPN uzantılarını WebView2'ye kurabilirsiniz.

---

## 5. Arka Planda Kurulan İsteğe Bağlı Ağ Bağlantıları
ED Browser yalnızca sizin açtığınız web sayfaları dışında şu opsiyonel bağlantıları kurar:
- **Güncelleme Kontrolü:** Başlangıçta (Ayarlar'dan kapatılabilir) veya `Güncellemeleri Denetle` butonuna bastığınızda yalnızca `api.github.com/repos/edizdar/ED-Browser/releases/latest` (ve limit durumunda `github.com/edizdar/ED-Browser/releases/latest`) adresini sorgular.
- **IP Test Aracı:** Yalnızca `IP Test Et` butonuna bastığınızda `https://cloudflare.com/cdn-cgi/trace` üzerinden çıkış IP adresinizi gösterir.
- **Eklenti Kurulumu:** Yalnızca siz bir mağaza eklentisini kurmayı seçtiğinizde `clients2.google.com` veya `edge.microsoft.com` üzerinden `.crx` paketini indirir.

---

## 6. Microsoft Edge WebView2 Motoru Hakkında Not
- ED Browser, render motoru olarak Windows'ta kurulu **Microsoft Edge WebView2 Runtime**'ı paylaşır; motor başlangıç parametrelerinde `--disable-background-networking`, `--disable-sync`, `--disable-component-update`, `--disable-domain-reliability`, `--disable-breakpad` ve `Translate,OptimizationHints,MediaRouter,msEdgeShoppingAssist,msEdgeTelemetry` devre dışı bırakılmıştır.
- Buna karşın WebView2'nin ikili çekirdeği Microsoft tarafından sağlandığından, motor seviyesindeki dahili ağ davranışları Microsoft Edge WebView2 politikalarına tabidir. Kaynak kodun tamamı incelemeye açık ve MIT lisanslıdır.
