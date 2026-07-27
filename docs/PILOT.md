# Print360 — Pilot Kurulum ve İzleme Kontrol Listesi

Bu belge, Print360'ı gerçek bir ortamda ilk kez devreye alırken adım adım
izlenecek kontrol listesidir. Amaç: kontrollü bir pilotla ürünü "laboratuvarda
test edildi"den "sahada çalışıyor"a taşımak.

**Pilot kapsamı önerisi:** 1 RDP sunucusu + 2-3 kullanıcı bilgisayarı, 2-3 hafta.
Küçük başlayın; sorun çıkarsa etkisi sınırlı olur.

---

## FAZ 0 — Ön Hazırlık (kurulumdan önce)

- [ ] **Pilot kullanıcıları seçildi** (teknolojiye açık, farklı yazıcı tipleri olan 2-3 kişi)
- [ ] **Yazıcı çeşitliliği** not edildi (eski/yeni, USB/ağ, lazer/etiket/fiş) — kenar durumları görmek için
- [ ] **Yedekleme:** Mevcut yazdırma yönteminden (Easy Print vb.) pilot sırasında vazgeçilmeyecek; Print360 paralel çalışacak (geri dönüş kolay olsun)
- [ ] **SQL Server erişilebilir** mi? (SQL Express de olur) — sunucu adı, `sa` veya yetkili kullanıcı/şifre hazır
- [ ] **Sunucuda .NET Framework 4.x** var (Windows Server ile gelir)
- [ ] **Kullanıcı PC'lerinde internet** var (SumatraPDF ilk indirme için) veya SumatraPDF.exe elde hazır
- [ ] **Yönetici hakları** hem sunucuda hem PC'lerde mevcut
- [ ] **Pilot sorumlusu** belirlendi (sorun olursa kime ulaşılacak)
- [ ] **Başarı kriterleri yazıldı** (aşağıdaki "Başarı Kriterleri" bölümü)

---

## FAZ 1 — Sunucu Kurulumu

- [ ] `Print360-Server-Setup.exe` sunucuya kopyalandı, **yönetici olarak** çalıştırıldı
- [ ] Sihirbaz soruları yanıtlandı:
  - [ ] Pilot kullanıcı adları (virgülle) girildi
  - [ ] Panel şifresi belirlendi ve **kaydedildi**
  - [ ] SQL sunucu/kullanıcı/şifre girildi
  - [ ] Panel yönetici kullanıcı adı + şifresi belirlendi ve **kaydedildi**
  - [ ] (İsteğe bağlı) E-posta raporu ayarlandı
- [ ] Kurulum "TAMAMLANDI" mesajıyla bitti (hata yok)
- [ ] **Yazıcılar oluştu:** `Get-Printer | Where Name -like 'Print360*'` → her kullanıcı için 3 yazıcı
- [ ] **Veritabanı oluştu:** SQL'de `Print360` veritabanı + tablolar var
- [ ] **Panel açılıyor:** Sunucuda tarayıcıda `https://localhost:8443` → giriş ekranı geliyor
- [ ] **Panele giriş:** Belirlenen kullanıcı adı/şifre ile Genel Bakış açılıyor
- [ ] **Ajanlar çalışıyor:** Görev Yöneticisi'nde `Print360.ServerAgent.exe` ve `Print360.Dashboard.exe` var
- [ ] **Lisans:** Deneme modundaysa lisans anahtarı Yetkiler → Lisans'tan girildi, "Lisanslı" göründü

---

## FAZ 2 — İstemci Kurulumu (her PC için tekrar)

- [ ] `Print360-Client-Setup.exe` PC'ye kopyalandı, **yönetici olarak** çalıştırıldı
- [ ] Sihirbaz soruları:
  - [ ] Sunucu adı/IP girildi (sunucuyla aynı ağda, ping çalışıyor)
  - [ ] İstemci şifresi girildi ve **not edildi** (her PC için aynı olabilir)
  - [ ] Hedef yazıcı boş bırakıldı (kullanıcı her işte seçsin) veya belirli yazıcı girildi
- [ ] SumatraPDF indirildi (veya elle kopyalandı) — `C:\Print360\SumatraPDF.exe` var
- [ ] Kurulum tamamlandı, ajan çalışıyor (`Print360.ClientAgent.exe` Görev Yöneticisi'nde)
- [ ] **Bağlantı doğrulandı:** Panelde Makineler sayfasında bu PC **● Çevrimiçi** görünüyor

---

## FAZ 3 — İlk Uçtan Uca Test (kritik — burada durun ve doğrulayın)

Her pilot kullanıcı, kendi RDP oturumunda:

- [ ] **Basit test:** Not Defteri'ne bir şey yazıp Ctrl+P → Yazdır
  - [ ] Lokal PC'de **yazıcı seçim penceresi açıldı** mı? (RDP tam ekranın üstünde)
  - [ ] Yazıcı seçilince çıktı **gerçekten kağıttan çıktı** mı?
  - [ ] Panelde İşler sayfasında iş **"Basıldı ✓"** göründü mü, doğru yazıcı adıyla?
- [ ] **Gerçek belge testi:** Kullanıcının normalde bastığı belge (fatura/irsaliye/Excel)
  - [ ] Sayfa sayısı, kağıt boyutu panelde doğru mu?
- [ ] **PDF modu:** "Print360 PDF" yazıcısına yazdır → masaüstünde "Print360 Belgeler" klasöründe PDF açıldı mı?
- [ ] **İptal:** Yazıcı seçim penceresinde İptal → iş "İptal edildi" olarak kaydedildi mi?
- [ ] **Çoklu yazıcı:** Farklı yazıcısı olan kullanıcıda da çalıştı mı?

> ⛔ **Bu fazda sorun varsa devam etmeyin.** `C:\Print360\logs\` (sunucu ve
> istemci) loglarına bakın; aşağıdaki "İzlenecek Loglar"a başvurun.

---

## FAZ 4 — İlk Gün İzleme

- [ ] Kullanıcılar **günlük işlerini Print360 ile** yapıyor (paralelde eski yöntem yedekte)
- [ ] Gün sonunda Panel → Genel Bakış: çıktı sayısı makul mü, engellenen/başarısız var mı?
- [ ] Panel → **Uyarılar** sayfası kontrol edildi (yazıcı sorunu, güvenlik, çevrimdışı, basılamadı)
- [ ] `C:\Print360\failed` klasörü boş mu? (dolu = basılamayan iş var, nedenini araştır)
- [ ] Kullanıcılardan **hızlı geri bildirim** alındı (yavaşlık, pencere sorunu, eksik yazıcı)
- [ ] Sunucu logunda tekrarlayan HATA satırı var mı?

---

## FAZ 5 — Birinci Hafta İzleme

- [ ] Her sabah Panel → Uyarılar ve failed klasörü kontrol (veya günlük e-posta raporu geldi mi?)
- [ ] **Makineler sayfası:** Tüm pilot PC'ler düzenli çevrimiçi mi? Kopan var mı?
- [ ] **Yazıcılar sayfası:** Yazıcı sağlık uyarıları gerçekle örtüşüyor mu?
- [ ] **Periyotlar:** Günlük çıktı grafiği beklenen mesai düzeninde mi?
- [ ] Ağ kesintisi / yazıcı kapalı gibi bir olay yaşandıysa: iş kaybolmadı, açılınca basıldı mı?
- [ ] SQL veritabanı boyutu ve sunucu CPU/RAM makul seyrediyor mu?
- [ ] Kullanıcı memnuniyeti: eski yönteme göre daha mı iyi/kötü/aynı?

---

## FAZ 6 — Pilot Değerlendirme (2-3 hafta sonunda)

- [ ] **Başarı kriterleri karşılandı mı?** (aşağıya bakın)
- [ ] Toplam iş sayısı / başarısız oran hesaplandı (Panel → İşler veya Excel dışa aktarım)
- [ ] Çözülemeyen sorun kaldı mı? Liste çıkarıldı
- [ ] Karar: **Yaygınlaştır** / **Düzeltip tekrar pilot** / **Vazgeç**
- [ ] Yaygınlaştırma kararıysa: kalan PC'ler ve kullanıcılar için plan yapıldı

---

## Başarı Kriterleri (pilot öncesi doldurun, sonunda ölçün)

| Kriter | Hedef | Sonuç |
|---|---|---|
| Yazdırma başarı oranı | ≥ %98 (basılan / gönderilen) | |
| Çıktının çıkma süresi | ≤ birkaç saniye | |
| Yazıcı seçim penceresi güvenilir açılıyor | Her seferinde | |
| Veri/rapor doğruluğu | Panel = gerçek | |
| Kullanıcı memnuniyeti | Eski yöntem ≥ | |
| Çözülemeyen kritik hata | 0 | |

---

## İzlenecek Loglar ve Yerler

| Ne | Nerede |
|---|---|
| Sunucu ajanı | `C:\Print360\logs\server-<kullanıcı>.log` |
| Web panel | `C:\Print360\logs\dashboard.log` |
| Bağlantı olayları | `C:\Print360\logs\connections.log` |
| İstemci ajanı | `C:\Print360\logs\client.log` (kullanıcı PC'sinde) |
| Basılamayan işler | `C:\Print360\failed\` (istemci PC'sinde) |
| PDF arşivi | `C:\Print360\archive\` (sunucuda) |
| Canlı durum | Panel → Uyarılar + Makineler + İşler |

---

## Hızlı Sorun Giderme

| Belirti | İlk bakılacak |
|---|---|
| Yazıcı seçim penceresi açılmıyor | İstemci ajanı çalışıyor mu? `Server=` doğru mu? Makine panelde çevrimiçi mi? |
| Çıktı çıkmıyor | client.log → yazıcı çözümleme/hata satırı; iş `failed`'de mi? |
| Panelde iş görünmüyor | Sunucu ajanı çalışıyor mu? SQL erişilebilir mi? (db.ini) |
| "403 / yetkisiz" | İstemci şifresi uyuşmuyor → Yetkiler → İstemci Şifreleri → Sıfırla |
| Deneme limiti engelliyor | Lisans anahtarı girildi mi? (Yetkiler → Lisans) |
| Panele girilemiyor | `https://sunucu:8443`, sertifika uyarısında "Devam"; kullanıcı/şifre doğru mu? |

---

## Geri Alma Planı (pilot başarısız olursa)

1. Kullanıcılar eski yazdırma yöntemine döner (paralelde tutulduğu için anında).
2. İstemci: Program Ekle/Kaldır → "Print360 Client" kaldır.
3. Sunucu: Program Ekle/Kaldır → "Print360 Server" kaldır (yazıcıları da siler).
4. İstenirse `Print360` SQL veritabanı silinir (raporları saklamak isterseniz durabilir).
5. Toplanan loglar ve sorun listesi geliştirmeye aktarılır → düzeltip yeniden pilot.

---

**Not:** Pilot boyunca topladığınız gerçek loglar, engellenen/başarısız iş
örnekleri ve kullanıcı geri bildirimleri ürünün en değerli çıktısıdır —
saklayın; bir sonraki sürümün yol haritasını bunlar belirler.
