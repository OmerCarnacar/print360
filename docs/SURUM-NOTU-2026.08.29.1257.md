## Print360 2026.08.29.1257

> ### ⚠️ Önemli — bu sürüm bir RDP arızasını düzeltiyor
>
> Önceki sürümler **bazı bilgisayarlarda uzak masaüstünün (mstsc.exe) hiç
> açılmamasına** yol açabiliyordu. Print360 kurulu tüm istemcilerin
> güncellenmesi önerilir.

---

### Sorun

Kurulum, RDP sanal kanal eklentisini **iki kayıt dalına birden** yazıyordu:

```
SOFTWARE\Microsoft\...\AddIns\Print360               (64-bit görünüm)
SOFTWARE\WOW6432Node\Microsoft\...\AddIns\Print360   (32-bit görünüm)
```

`Print360.VC.dll` ise yalnızca **x64**.

`mstsc.exe` açılırken bu anahtarda kayıtlı DLL'i **kendi mimarisinde** yüklemeye
çalışır. 32-bit uzak masaüstü istemcisi 64-bit DLL'i yükleyemez ve **program hiç
açılmaz.** Kullanıcı için bu, sebebi görünmeyen bir arızadır — Print360 ile
ilişkilendirmesi mümkün değildir.

İkinci risk: kayıt varken dosya yoksa (yarım kaldırma, dosyanın silinmesi, virüs
tarayıcının karantinaya alması) aynı arıza oluşur.

### Düzeltme

**Kurulum**, DLL'in gerçek mimarisini PE başlığından okuyup **yalnızca doğru
dala** yazıyor. Önceki sürümlerin yanlış dala yazdığı kayıt varsa kurulum
sırasında temizleniyor:

```
Uyumsuz mimarideki eski kayit temizlendi (RDP acilmama sebebi).
```

**İstemci ajanı** her açılışta kaydı doğruluyor. Kayıt var ama dosya yoksa kaydı
kaldırıp günlüğe yazıyor:

```
ONARIM: RDP eklenti kaydi bozuktu (dosya yok: ...). Kayit kaldirildi
        - uzak masaustu artik normal acilir.
```

Böylece bozuk kalmış makineler ajan çalıştığı anda kendiliğinden düzeliyor.

---

### RDP'si şu an açılmayan bir makine varsa

Print360 kurulumunu yapamıyorsanız (RDP'ye ihtiyacınız varsa) kaydı elle
silmek yeterlidir. Yönetici olarak açılmış bir komut isteminde:

```
reg delete "HKLM\SOFTWARE\WOW6432Node\Microsoft\Terminal Server Client\Default\AddIns\Print360" /f
```

Bu komut yalnızca 32-bit görünümdeki hatalı kaydı siler; uzak masaüstü hemen
açılır. Ardından bu sürümü kurabilirsiniz.

---

### ✅ Doğrulama

`tests/PeMimari.cs` eklendi. PE mimari okuyucusu Windows'un kendi ikilileriyle
karşılaştırılarak doğrulandı:

| Dosya | Beklenen | Sonuç |
|---|---|---|
| `System32\notepad.exe` | x64 | ✅ |
| `SysWOW64\notepad.exe` | x86 | ✅ |
| `Print360.VC.dll` | x64 | ✅ |

Ayrıca x64 DLL için seçilen kayıt dalının `WOW6432Node` **olmadığı** denetleniyor.

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
