## Print360 2026.08.29.1316

**Kurulum, üzerine kurulum ve kaldırma akışları baştan sona gözden geçirildi.**
Dört gerçek kusur düzeltildi; biri veri kaybına yol açıyordu.

---

### 1. Üzerine kurulum bekleyen işleri siliyordu ⚠️

Temizlik adımı `queue` ve `spool` klasörlerini boşaltıyordu. Bunlar **kullanıcı
verisidir**:

| Klasör | İçerik |
|---|---|
| `queue` | İstemciye henüz teslim edilmemiş baskı işleri |
| `spool` | Yakalanmış ama gönderilmemiş çıktılar |

Yani sunucuyu güncelleyen yönetici, kullanıcıların **bekleyen çıktılarını
farkında olmadan yok ediyordu.** Güncelleme veri kaybettirmemelidir.

Artık yalnızca `update` (otomatik güncelleme için indirilen geçici ikililer)
boşaltılıyor ve korunan iş sayısı günlüğe yazılıyor:

```
Bekleyen 2 is KORUNDU (kuyruk ve spool silinmedi).
```

### 2. Eski sürüm çalışmaya devam edebiliyordu

Temizlik süreçleri durduruyordu ama otomatik başlatmayı kapatmıyordu.
Zamanlanmış görev — oturum açılışı veya RDP bağlantısı tetikleyicisiyle — ajanı
tam dosyalar kopyalanırken yeniden başlatabiliyordu. Exe kilitli kalıyor,
kopyalama başarısız oluyor, kurulum yine **"tamamlandı"** diyordu.

Artık görev önce askıya alınıyor, süreçler zorla durduruluyor, kurulum sonunda
görev yeniden kuruluyor. Kurulum yarıda kalırsa hata yolunda görev tekrar
etkinleştiriliyor — aksi halde ajan hiç başlamazdı.

### 3. Kopyalama sessizce başarısız olabiliyordu

Tek denemede pes edip yalnızca uyarı yazıyor, sonucu doğrulamıyordu. Artık dört
kez deneniyor, her denemede kilitli süreç durduruluyor ve kopyanın **boyutu
doğrulanıyor.** Yine olmazsa açıkça bildiriliyor:

```
GUNCELLENEMEDI: Print360.ServerAgent.exe (...). Bu dosya ESKI SURUMDE kaldi.
Bilgisayari yeniden baslatip kurulumu tekrarlayin.
```

### 4. Kaldırmada bekleyen işler sessizce kalıyordu

Kuyrukta teslim edilmemiş iş varsa artık söyleniyor. Dosyalar silinmiyor;
Print360 tekrar kurulursa teslim ediliyor.

---

### ✅ Doğrulama

Kuyruğa ve spool'a bekleyen işler konup üzerine kurulum yapıldı:

| | Sonuç |
|---|---|
| `queue` | 1 dosya **korundu** |
| `spool` | 1 dosya **korundu** |
| `update` | 0 dosya (temizlendi) |
| Kurulum günlüğü | tek satır: *"Kurulum sorunsuz tamamlandi."* |

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
