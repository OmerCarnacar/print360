## Print360 v1.1.4

**Panelde bağlı yazıcılar artık yeşil "Aktif" rozetiyle görünüyor.**

---

### 🟢 Bağlantı rozeti

Yazıcı Sağlığı sayfasına **Bağlantı** sütunu, kartlara da **● Aktif (bağlı)**
sayacı eklendi. Dört durum var:

| Rozet | Ne zaman çıkar |
|---|---|
| 🟢 **Aktif** | İstemci ajanı son 5 dakikada rapor gönderdi **ve** yazıcı hazır/yazdırıyor |
| 🟡 **Sorunlu** | Rapor taze ama yazıcı hata bildiriyor (kâğıt bitti vb.) ya da durdurulmuş |
| 🔴 **Çevrimdışı** | Yazıcının kendisi çevrimdışı |
| ⚪ **Pasif** | Ajan 5 dakikadır rapor göndermiyor |

Yeşil noktanın hafif nabız animasyonu, canlı bağlantıyı listede gözle ayırt
etmeyi kolaylaştırır.

Aynı rozet **Genel Bakış → Bağlı İstemciler** tablosuna da uygulandı.

### Neden iki şart birden aranıyor

"Aktif" sayılmak için yazıcının hazır olması yetmiyor; istemci ajanının da
**canlı** olması gerekiyor.

Ajan durduğunda yazıcı kaydı hâlâ "Hazir" yazar — çünkü bu, en son alınan
rapordur. Yalnızca ona bakılsaydı panelde yeşil görünen ama yazdırıldığında
hiçbir şey yapmayan yazıcılar olurdu. Rozet, **son bilinen durumu değil, şu an
gerçekten yazdırılabilir mi** sorusunu yanıtlıyor.

---

### 📌 Lisans

Print360 **açık kaynak değildir.** Kaynağı açıktır ve incelenebilir; satışı
yasak olduğu için OSI tanımını karşılamaz — MIT/GPL/Apache değildir.

Ücretsiz kullanım her ortamda serbesttir. Yazılım **"olduğu gibi"** sunulur;
**geliştirici hiçbir sorumluluk üstlenmez ve hiçbir garanti vermez.** Üretim
ortamına almadan önce kendi ortamınızda test edin.

Tam metin: [LICENSE](https://github.com/OmerCarnacar/print360/blob/main/LICENSE)

---

### 📦 Kurulum

Bu değişiklik **sunucu panelindedir** — görebilmek için sunucu kurulumunu
güncellemeniz gerekir.

1. Aşağıdaki ZIP dosyasını indirin.
2. **Sunucuda** `Print360-Server-Setup.exe` — yönetici olarak çalıştırın.
3. **Kullanıcı bilgisayarlarında** `Print360-Client-Setup.exe`.

Kurulum dosyaları kod imzalı değildir; SmartScreen uyarı verebilir
(**Daha fazla bilgi → Yine de çalıştır**).

Sorun giderme: panelde **Tanı** sayfası (`/tani`)

---

**Ömer ÇARNAÇAR** — Geliştirici
[omer.carnacar@outlook.com.tr](mailto:omer.carnacar@outlook.com.tr) ·
[LinkedIn](https://www.linkedin.com/in/omercarnacar/)
