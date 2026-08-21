## Print360 2026.08.21.1945

**Sonsuz onay döngüsü düzeltildi.** İki tarafı da güncelleyin.

---

### 🐞 İstemci aynı işi durmadan onaylıyordu

```
19:37:44  Onay gonderildi: ..._193128_109_... (117 ms)
19:37:44  Onay gonderildi: ..._193128_109_... (110 ms)
19:37:43  Onay gonderildi: ..._193128_109_... (108 ms)
```

Saniyede yaklaşık sekiz kez, hep aynı dosya.

**Sebep:** iş çekme döngüsü "kuyruk boşalana kadar" çalışıyordu. Dosya zaten
alınmışsa bile onay gönderiliyor ve *başarılı* sayılıyordu. Sunucu onayı "OK"
ile yanıtlayıp dosyayı **silemeyince** aynı iş kuyruğun başında kalıyor; istemci
onu tekrar görüyor, tekrar onaylıyor, sonsuza kadar.

Çok iş parçacıklı sunucuda (önceki sürüm) ayrıca aynı makinenin eş zamanlı iki
isteği aynı dosyayı alabiliyordu.

**Düzeltme — sunucu:**
- Makine başına kuyruk kilidi
- Silme beş kez denenir ve **sonucu doğrulanır**; silinemezse istemciye `500`
  dönülür ve günlüğe `ONAY ISLENEMEDI <- makine | dosya | sebep` yazılır

**Düzeltme — istemci:**
- Zaten alınmış iş için döngü durur
- Onay `500` alırsa `ONAY REDDEDILDI` yazılır
- Sunucu aynı işi arka arkaya veriyorsa dakikada bir uyarı: *"Sunucu aynı işi
  tekrar veriyor (kuyruktan düşüremiyor)"*
- Tur başına en fazla 50 iş — sonsuz döngü artık mümkün değil

---

### 🔍 Hâlâ takılıyorsa

Sunucudaki `C:\Print360\logs\dashboard.log` dosyasında artık şu satır
görünecek:

```
ONAY ISLENEMEDI <- DESKTOP-01 | belge.pdf.gz | dosya silinemiyor: <sebep>
```

`<sebep>` genellikle bir izin sorunudur (`C:\Print360\queue` klasörüne yazma
izni) ya da dosyayı açık tutan başka bir işlemdir. Bu satır sorunun tam adresini
verir.

---

### 📌 Lisans

Print360 **açık kaynak değildir.** Kaynağı açıktır ve incelenebilir; satışı
yasak olduğu için OSI tanımını karşılamaz. Ücretsiz kullanım her ortamda
serbesttir. Yazılım **"olduğu gibi"** sunulur; geliştirici hiçbir sorumluluk
üstlenmez ve hiçbir garanti vermez.

Dosya doğrulama: [SHA256SUMS.txt](https://github.com/OmerCarnacar/print360/blob/main/SHA256SUMS.txt)

---

**Ömer ÇARNAÇAR** — Geliştirici
[omer.carnacar@outlook.com.tr](mailto:omer.carnacar@outlook.com.tr) ·
[LinkedIn](https://www.linkedin.com/in/omercarnacar/)
