## Print360 v1.1.8

**İşler istemciye inmiyorsa bu sürüm gereklidir.** Düzeltmeler istemci
tarafındadır; sunucuyu güncellemenize gerek yoktur.

---

### 🐞 Sunucuda işler birikiyor, istemci almıyordu

Belirti şuydu: sunucu kuyruğunda işler birikiyor, istemci penceresi **"BAĞLI"**
gösteriyor, yazıcı **"Hazır"** görünüyor — ama hiçbir çıktı gelmiyor. İstemci
günlüğünde işlerle ilgili tek satır bile yok.

**Sebep:** keep-alive uyuşmazlığı. Sunucu boşta kalan HTTP bağlantısını
kapatıyor, .NET onu hâlâ canlı sanıp yeniden kullanıyor ve istek şu hatayla
düşüyordu:

> Temel alınan bağlantı kapatıldı: Canlı tutulacağı beklenen bir bağlantı
> sunucu tarafından kapatıldı.

Üç saniyede bir yoklama yapan bir istemcide bu sürekli tekrarlıyor; bağlantı bir
kuruluyor bir kopuyordu.

**Düzeltme:** tüm istemci HTTP istekleri artık taze bağlantı açıyor
(`KeepAlive = false`). İş indirmesi için okuma zaman aşımı 60 saniyeye çıkarıldı,
böylece yavaş hatlarda büyük çıktılar yarıda kalmıyor.

---

### 🔍 Hata sessizce yutuluyordu

İş çekme başarısız olduğunda hiçbir kayıt tutulmuyordu. Sunucuda işler birikirken
istemci günlüğünde hiçbir iz olmadığı için sorunun nerede olduğu anlaşılamıyordu.

Artık günlüğe yazılıyor:

```
Is alinamadi (https://sunucu:8443): <hata mesaji>
```

Döngü üç saniyede bir döndüğü için kayıt iki dakikada bir ile sınırlı — günlüğü
doldurmaz ama sorunu görünür kılar.

---

### 🛡️ Güvenlik ağı

RDP kanalı açık görünse bile HTTPS kuyruğu artık ~30 saniyede bir yoklanıyor.

Kanalın açık sayılması `.vc-aktif` işaret dosyasının varlığına bakıyordu. RDP
oturumu anormal koptuğunda (uzak masaüstü penceresi zorla kapatılır, ağ giderse)
bu dosya geride kalabiliyor; istemci kanalı sonsuza kadar açık sanıp kuyruğu hiç
yoklamayabiliyordu. Artık böyle bir durumda işler en geç yarım dakika içinde
yine de iniyor.

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
