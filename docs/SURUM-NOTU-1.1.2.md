## Print360 v1.1.2

**Bakım sürümü.** Yazılımın çalışmasında değişiklik yoktur — v1.1.1 kullanıyorsanız
güncellemeniz gerekmez. Bu sürüm yalnızca metin, belge ve ekran görüntüsü
temizliği içerir.

---

### 🏷️ Marka atıfları kaldırıldı

Kod yorumları, belgeler, kurulum sihirbazı metinleri ve depo konularında geçen
**başka bir ürünün ticari markasına** yapılan tüm atıflar (26 satır) nötr
ifadelerle değiştirildi: "kanal mantığı", "iş türü modeli", "PDF modu",
"yazıcı seçim modu".

Print360 artık kendini başka bir ürüne benzeterek değil, kendi işleviyle
anlatıyor. İşlevsel değişiklik yoktur.

### 🔒 Özel ortam bilgileri temizlendi

Depodaki ekran görüntüleri ve kod örnekleri, geliştirme ortamına ait gerçek
bilgiler taşıyordu. Hepsi örnek değerlerle değiştirildi:

| | Eskiden | Şimdi |
|---|---|---|
| Makine adı | gerçek bilgisayar adı | `OFIS-PC` |
| Sunucu adresi | gerçek IP | `SUNUCU01` |
| Yazıcı | ağdaki gerçek cihazın adı | `OFIS-YAZICI (HP LaserJet MFP M428fdw)` |

Ekran görüntülerindeki yazılar aynı yazı tipi, boyut ve renkle yeniden çizildi;
bulanıklaştırma veya karartma yapılmadı.

`Print360.ClientAgent.cs` içindeki çoklu sunucu örneği de gerçek bir sunucu
adresi içeriyordu; jenerik `SRV01,SRV02,SRV03` ile değiştirildi.

---

### 📌 Lisans hatırlatması

Print360 **açık kaynak değildir.** Kaynağı açıktır ve incelenebilir; ancak
satışı yasak olduğu için OSI tanımını karşılamaz — MIT/GPL/Apache değildir.

Ücretsiz kullanım her ortamda serbesttir (kurumsal ve ticari dahil). Satmak,
kiralamak veya ücretli bir ürünün parçası olarak sunmak yasaktır.

Yazılım **"olduğu gibi"**, hiçbir bedel alınmadan sunulur; **geliştirici hiçbir
sorumluluk üstlenmez ve hiçbir garanti vermez.** Üretim ortamına almadan önce
kendi ortamınızda test edin ve sistem yedeği alın.

Tam metin: [LICENSE](https://github.com/OmerCarnacar/print360/blob/main/LICENSE)

---

### 📦 Kurulum

1. Aşağıdaki ZIP dosyasını indirin.
2. **Sunucuda** `Print360-Server-Setup.exe` — yönetici olarak çalıştırın.
3. **Kullanıcı bilgisayarlarında** `Print360-Client-Setup.exe`.
4. RDP oturumunuzda `Print360 - <kullanıcı>` yazıcısına yazdırın.

Kurulum dosyaları kod imzalı değildir; Windows SmartScreen uyarı verebilir
(**Daha fazla bilgi → Yine de çalıştır**).

Sorun giderme: panelde **Tanı** sayfası (`/tani`) ·
Belgeler: [README](https://github.com/OmerCarnacar/print360#readme)

---

**Ömer ÇARNAÇAR** — Geliştirici
[omer.carnacar@outlook.com.tr](mailto:omer.carnacar@outlook.com.tr) ·
[LinkedIn](https://www.linkedin.com/in/omercarnacar/)
