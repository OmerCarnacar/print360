## Print360 v1.1.3

Küçük ama günlük kullanımda hissedilen bir iyileştirme: **sistem tepsisindeki
simgenin üzerine gelince durum görünüyor.**

---

### 🖱️ Tepsi ipucunda canlı durum

Simgeye fareyle dokunduğunuzda üç satır çıkıyor:

```
Print360 - BAGLI
Yazici: HP LaserJet MFP M428..
Son temas: 16:18
```

| Satır | Ne gösteriyor |
|---|---|
| 1 | Bağlı mı, değil mi. Birden fazla sunucuya bağlıysa sayısıyla: `BAGLI (3 sunucu)` |
| 2 | Çıktının gideceği yazıcı — kişisel önceliğinizin ilki. Seçim yoksa `(secilmedi)` |
| 3 | Bekleyen iş varsa `Bekleyen: 2 is`, yoksa son temas saati. Bağlantı yokken `Sunucu araniyor` veya `RDP bekleniyor` |

İpucu **4 saniyede bir** tazeleniyor; fareyi götürdüğünüzde gördüğünüz veri
anlık. Pencereyi açmadan "çalışıyor mu, nereye basacak, bekleyen var mı"
sorularının üçüne birden cevap veriyor.

Önceden ipucu yalnızca bağlantı durumu değiştiğinde güncelleniyor ve tek satır
gösteriyordu.

> **Teknik not:** Windows'un tepsi ipucu alanı .NET tarafında 63 karakterle
> sınırlı. Sabit satırlar yazıldıktan sonra yazıcı adı kalan yere göre
> kısaltılıyor. Bu yüzden bazı etiketler kısaldı — `Son temas` artık saniye
> göstermiyor. Uzun yazıcı adlarında yazıcı satırının tümden düşmemesi için
> gerekliydi.

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

1. Aşağıdaki ZIP dosyasını indirin.
2. **Sunucuda** `Print360-Server-Setup.exe` — yönetici olarak çalıştırın.
3. **Kullanıcı bilgisayarlarında** `Print360-Client-Setup.exe`.
4. RDP oturumunuzda `Print360 - <kullanıcı>` yazıcısına yazdırın.

Kurulum dosyaları kod imzalı değildir; SmartScreen uyarı verebilir
(**Daha fazla bilgi → Yine de çalıştır**).

Sorun giderme: panelde **Tanı** sayfası (`/tani`)

---

**Ömer ÇARNAÇAR** — Geliştirici
[omer.carnacar@outlook.com.tr](mailto:omer.carnacar@outlook.com.tr) ·
[LinkedIn](https://www.linkedin.com/in/omercarnacar/)
