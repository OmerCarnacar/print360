## Print360 2026.08.22.1135

**Adım adım açıklamalı günlük.** Bir işin yolculuğunun her aşaması, ne anlama
geldiğiyle birlikte kaydediliyor.

---

### 📋 Sunucu tarafı — 6 adım

```
[1/6] SPOOL        admin.pdf (512 KB)      -> kullanici sanal yaziciya yazdirdi,
                                              cikti dosyaya dustu
[2/6] BELGE ADI    Teklif Formu | 3 sayfa  -> PrintService olay gunlugunden okundu
[3/6] HEDEF        DESKTOP-01              -> cikti bu makineye gonderilecek
                                              (RDP istemci adi)
[4/6] KANAL        HTTPS-kuyruk            -> kuyruga yazildi; istemci
                                              yoklayinca alacak
[5/6] SPOOL SIL    admin.pdf               -> is teslim edildi, gecici cikti
                                              dosyasi kaldirildi
[6/6] TAMAM        ...pdf | HTTPS-kuyruk   -> arsivlendi; siradaki adim istemcide
```

### 📋 İstemci tarafı — 7 adım

```
[1/7] IS BULUNDU   belge.pdf               -> sunucu kuyrugunda bekleyen is alindi
[2/7] INDIRILDI    184 KB -> 512 KB        -> sikistirilmis veri indirilip acildi
[3/7] ONAYLANDI    belge.pdf               -> teslim onayi gonderildi, is
                                              kuyruktan dusuruldu
[4/7] HEDEF YAZICI Pantum M7100DW          -> kisisel oncelik sirasindan secildi
[5/7] YAZDIRMA     belge.pdf               -> SumatraPDF motoru ile basiliyor
[6/7] BASILDI      belge.pdf               -> done klasorune tasindi
[7/7] RAPORLANDI   Teklif Formu            -> panelde sayaclara islendi
```

---

### Neden böyle

Günlüğe bakan kişinin **kodu bilmesi gerekmiyor.** Her satır ne yapıldığını ve
neden yapıldığını söylüyor. Bir adım başarısız olduğunda açıklama sebebi
veriyor:

```
[3/6] HEDEF        (BELIRLENEMEDI)  -> ajan bir RDP oturumunda degil;
                                       is bekletilecek
[4/6] KANAL        (ikisi de olmadi) -> sanal kanal yok ve kuyruga
                                        yazilamadi; tsclient denenecek
```

Numaralar sayesinde nerede durulduğu bir bakışta görülüyor: sunucu `[6/6]`
yazmış ama istemcide `[1/7]` yoksa, iş kuyrukta bekliyor demektir.

Kapatmak için sunucuda `db.ini`, istemcide `Print360.ini` içine
`AyrintiliGunluk=0` yazın. Günlükler zaten 5 MB'ta devrediyor, disk dolmaz.

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
