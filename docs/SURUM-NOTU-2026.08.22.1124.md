## Print360 2026.08.22.1124

**"İlk yazdırma oluyor, devamı gelmiyor" sorununun kök sebebi bulundu ve
düzeltildi.** İki tarafı da güncelleyin.

---

### 🎯 Sebep: Türkçe karakter HTTP başlığında bozuluyordu

Sunucudan alınan iş kimliği:

```
X-Job-Id : F:20260821_193128_109_Administrator~Belgeyi Yazd1r.pdf.gz
                                                       ^^^
```

Diskteki gerçek dosya adı `...Belgeyi **Yazdır**.pdf.gz` — Türkçe `ı` harfi.
HTTP başlıkları ASCII taşır; bu harf aktarımda `1` rakamına dönüşüyordu.

Kırılma zinciri:

| Adım | Sonuç |
|---|---|
| 1. Sunucu işi verir | Başlıkta ad bozulur: `Yazdır` → `Yazd1r` |
| 2. İstemci indirir ve basar | ✅ **İlk yazdırma çalışır** |
| 3. İstemci onayı gönderir | Bozuk adla |
| 4. Sunucu dosyayı arar | ❌ O adda dosya yok |
| 5. Kuyruktan düşürülemez | Aynı iş sonsuza kadar tekrar verilir |

Belge adında **boşluk**, **`%`**, **`&`** veya **`=`** olması da aynı şekilde
kırıyordu — bunlar sorgu dizesini bozuyordu.

**Düzeltme:** ad içeren başlıklar yüzde-kodlanıyor, değer artık tamamen ASCII.
İstemci kimliği aynen geri gönderiyor; sunucunun sorgu çözümlemesi orijinal adı
veriyor. Eski sunucularla uyum için `%` içermeyen değerler olduğu gibi bırakılır.

---

### 🔍 Sessiz başarısızlık kapatıldı

Onaylanan iş kuyrukta hiç bulunamazsa artık günlüğe yazılıyor:

```
UYARI: Onaylanan is kuyrukta bulunamadi <- DESKTOP-01
       | aranan: ...Yazd1r.pdf.gz
       | kuyrukta: ...Yazdır.pdf.gz
```

İki adı yan yana gösteriyor. "Dosya yok" ile "sildim" aynı şey değildir; öyle
sayılması bu hatayı aylarca gizleyebilirdi.

---

### ✅ Doğrulama

`tests/RoundTrip.cs` eklendi. İş kimliğinin **sunucu → HTTP başlığı → istemci →
sorgu dizesi → sunucu** yolculuğu beş senaryoda test ediliyor: Türkçe harf,
boşluk, yüzde işareti, `&` ve `=`. Beşi de geçiyor; üretilen başlıkların ASCII
olduğu ayrıca denetleniyor.

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
