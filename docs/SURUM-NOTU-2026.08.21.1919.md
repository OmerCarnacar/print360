## Print360 2026.08.21.1919

**İşler sırayla inmiyorsa bu sürüm gereklidir.** Düzeltme hem sunucuda hem
istemcide; iki tarafı da güncelleyin.

---

### 🐞 Sunucu tek iş parçacıklıydı

Belirti: ilk yazdırma sorunsuz tamamlanıyor, hemen ardından gelen istek zaman
aşımına uğruyor.

```
19:14:11  Tamamlandi: ...Belgeyi Yazdir.pdf -> Pantum M7100DW
19:14:11  Is alinamadi: Islem zaman asimina ugradi
```

**Sebep:** panelin dinleyici döngüsü isteği döngünün içinde, tek iş parçacığında
işliyordu:

```csharp
var ctx = listener.GetContext();   // sıradaki isteği al
... isteği burada işle ...          // bitene kadar BAŞKA İSTEK ALINMAZ
```

Yavaş bir istemciye 184 KB'lık iş yazarken sunucunun tamamı bloke oluyordu.
İstemci ilk işi alıp hemen ikincisini istiyor, sunucu hâlâ birinciyi bitirmediği
için istek zaman aşımına uğruyordu. Aynı sebeple ikinci bir istemcinin kalp atışı
ve panelin kendisi de bekliyordu.

**Düzeltme:** her istek artık iş parçacığı havuzuna devrediliyor. Yavaş bir istek
diğerlerini bekletmiyor.

---

### 🔍 Adım adım günlük — sorun sunucuda mı, istemcide mi?

Artık her aşama süresi ve aktarılan boyutla birlikte kaydediliyor.

**Başarılı bir iş:**

```
İstemci   Is alindi [HTTPS]: belge.pdf | sunucu yaniti 180 ms | indirme 420 ms
                              | 184 KB sikistirilmis -> 512 KB
İstemci   Onay gonderildi: belge.pdf  (95 ms)
Sunucu    Is verildi -> DESKTOP-01 | belge.pdf | 184 KB | 340 ms
Sunucu    Onay alindi <- DESKTOP-01 | belge.pdf | kuyruktan dusuruldu
```

**Hata durumunda** hangi aşamada takıldığı yazılıyor:

```
IS ALINAMADI [https://sunucu:8443] asama: sunucudan yanit bekleniyor
             | gecen 60012 ms | durum: Timeout | ...
```

| Aşama | Sorun nerede |
|---|---|
| `sunucudan yanit bekleniyor` | **Sunucu** geç cevap veriyor |
| `is indiriliyor` | **Ağ / aktarım** yavaş |
| `onay (ACK) gonderiliyor` | **Sunucu** meşgul |

İki tarafın süresini karşılaştırınca gecikmenin nerede olduğu tartışmasız görülür:
sunucu "340 ms" deyip istemci "12000 ms" diyorsa arada ağ vardır.

İlk hata artık hemen kaydediliyor; önceden iki dakika bekliyordu ve sorunun
başlangıcı günlükte görünmüyordu.

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
