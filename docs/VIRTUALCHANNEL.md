# Print360 — RDP Virtual Channel Yol Haritası

Bu belge, işlerin **RDP sanal kanalı** üzerinden (IP/port/firewall
gerektirmeden) taşınması için hazırlanan **referans iskeletin** derlenmesi,
kurulması ve test edilmesini anlatır.

> **Durum (güncellendi):**
> - ✅ Sunucu tarafı (C# P/Invoke, `Print360.VChannel.cs`) **derlendi ve çalıştı**
>   (kanal yokken `false` → HTTPS'e düşer).
> - ✅ İstemci eklentisi (`Print360.VC.dll`) **derlendi** — Windows SDK 10.0.18362
>   ve MSVC kurulup `cl.exe` ile hatasız (114 KB); `dumpbin /exports` ile
>   **`VirtualChannelEntry`** giriş noktasının doğru export edildiği doğrulandı
>   (mstsc'nin aradığı SVC sözleşmesi). Referans binary: `vc/Print360.VC.dll`.
> - ✅ **İstemci Setup'ına DLL + registry kaydı eklendi** — Setup `Print360.VC.dll`'i
>   `C:\Print360`'a kopyalar ve mstsc `AddIns` registry kaydını yapar (HKLM +
>   WOW6432Node); kaldırmada temizlenir. Registry yazma/temizleme mantığı test edildi.
> - ✅ **ServerAgent'a `VChannel.Gonder` çağrısı eklendi** — `db.ini`'de
>   `VirtualChannel=1` bayrağıyla (VARSAYILAN KAPALI). Kanal 1. tercih; kanal
>   yoksa/bayrak kapalıysa mevcut HTTPS kuyruğuna düşer. Test edildi: bayrak
>   kapalı ve açık (kanal yok) durumlarının ikisinde de iş HTTPS'e düştü, akış bozulmadı.
> - ✅ **ÇİFT YÖNLÜ (v1.1):** Artık yalnızca iş değil, **onay + sayaç + heartbeat +
>   yazıcı sağlığı** da aynı `P360` kanalından **ters yönde** (istemci→sunucu)
>   akabilir. İstemci `Print360.ClientAgent` mesajı `C:\Print360\vc-outbox\*.msg`
>   olarak bırakır; mstsc içindeki `Print360.VC.dll` bunu `pVirtualChannelWrite`
>   ile kanala yazar; sunucu `WTSVirtualChannelRead` ile okuyup **aynı SQL
>   tablolarına** (Printed / Heartbeat / PrinterHealth) işler. Böylece
>   `VCMode=1` istemcide ve `VirtualChannel=1` sunucuda açıkken **HTTPS/IP/port/
>   Server ayarı hiç gerekmez.** Hepsi derlendi (C++ DLL 122 KB,
>   C# sunucu+istemci hatasız).
> - ⏳ Kalan tek adım: **gerçek bir RDS sunucusu + RDP oturumunda uçtan uca test**
>   (bu tek makinede yapılamaz — çift yönlü kanal gerçek RDP oturumunun iki ucunu
>   gerektirir). HTTPS kanalı **güvenlik ağı olarak duruyor**: `VCMode=0` (varsayılan)
>   ile her şey eskisi gibi HTTPS'ten akar; sahada VC doğrulanınca `VCMode=1` yapılır.

## Neden Virtual Channel?

Bu yaklaşımda işler RDP oturumunun **kendi tüneli** içinden taşınır — ayrı bağlantı,
IP, port, firewall yoktur. Print360'ın mevcut kanalı HTTPS'tir (8443); bu ek
bileşen aynı "sıfır ayar" deneyimini getirir. Mevcut HTTPS kanalı **değişmez**;
Virtual Channel yalnızca 1. tercih olarak eklenir, yoksa HTTPS'e düşülür.

## Mimari

```
[Sunucu — RDS oturumu]                       [İstemci PC — mstsc.exe]
ServerAgent işi yakalar (PDF)                 Print360.VC.dll (SVC eklentisi,
      │                                        mstsc'ye registry ile kayıtlı)
      ▼                                             │
VChannel.Gonder("P360", pdf)  ──RDP tüneli──────────► kanaldan al, birleştir
  (wtsapi32: WTSVirtualChannelOpen/Write)            │
                                            C:\Print360\jobs\<ad>.pdf yaz
                                                     ▼
                                    Mevcut ClientAgent basar (DEĞİŞMEZ:
                                    yazıcı seçimi, çift motor, PDF modu...)
```

**Kilit tasarım:** Eklenti yalnızca "kanaldan al → `jobs`'a yaz" yapar. Tüm
baskı zekâsı mevcut ClientAgent'ta kalır. Böylece VC sadece yeni bir **taşıma**
katmanıdır.

## Dosyalar

| Dosya | Ne |
|---|---|
| `vc/Print360.VirtualChannel.cpp` | İstemci SVC eklentisi (C++) — kanaldan alıp `jobs`'a yazar |
| `vc/Print360.VirtualChannel.def` | DLL export tanımı (`VirtualChannelEntry`) |
| `vc/build-vc.cmd` | Derleme komutu (MSVC + Windows SDK) |
| `server/Print360.VChannel.cs` | Sunucu tarafı gönderim (C# P/Invoke, wtsapi32) |

## Protokol v2 — uygulama seviyesinde parçalama

**Neden v2?** v1'de PDF **tek** kanal mesajı olarak gönderiliyordu. SVC'de tek
mesaj boyutu sınırlıdır; birkaç MB'lık bir PDF sessizce başarısız olurdu.
v2'de PDF ≤30.000 baytlık bloklara bölünür, her blok ayrı çerçevedir →
**dosya boyutu sınırsız**.

Çerçeve (little endian, 20 bayt başlık):
```
[0]  sihir[4] = "P360"   [4] sürüm(1)=2   [5] tip(1)   [6] rezerv(2)
[8]  isId(4)             [12] blokNo(4)   [16] uzunluk(4)   [20] veri...

tip 1 BAŞLA : veri = [toplamBoyut:4][adUzunluğu:4][ad UTF-8]
tip 2 VERİ  : veri = ham PDF bloğu
tip 3 BİTTİ : veri = [blokSayısı:4]   (doğrulama)
```

İstemci BAŞLA'da `<ad>.part` açar, VERİ'lerde ekler, BİTTİ'de **toplam boyut ve
blok sayısını doğrulayıp** `<ad>` olarak atomik yeniden adlandırır.
Doğrulama başarısızsa `.part` silinir — **yarım iş asla basılmaz**.

### Kanal aktif işareti
Eklenti kanalı açınca `C:\Print360\vc-outbox\.vc-aktif` dosyasını oluşturur,
kanal kapanınca siler. İstemci ajanı buna bakarak tahmin yürütmeden karar verir:
işaret varsa kanal modu, yoksa otomatik HTTPS.

### Protokol testi (kanal olmadan çalıştırılabilir)
```
powershell -ExecutionPolicy Bypass -File vc\test-protokol.ps1
```
C# üreteci (`VChannel.Cerceveler`) çerçeveleri üretir, C++ çözücü
(`P360_CerceveIsle`) dosyayı yeniden kurar, SHA-256 karşılaştırılır.
1 KB / 30000 B / 30001 B / 256 KB / 5 MB ve yarım-akış senaryoları test edilir.

---

## Adım 1 — İstemci eklentisini derle (SDK'lı makinede)

Gereksinim: Visual Studio + **Desktop development with C++** + **Windows SDK**.

```cmd
cd vc
build-vc.cmd          :: Print360.VC.dll üretir
```
`build-vc.cmd`, Developer Command Prompt içinde de, normal komut isteminde de
çalışır (ikincisinde MSVC/SDK yollarını kendi bulur; sürüm farklıysa dosyadaki
`VSROOT`/`SDK_DIR` satırlarını düzenleyin).

> Bu depoda `vc/Print360.VC.dll` zaten derlenmiş referans binary olarak
> bulunabilir (SDK 10.0.18362 + MSVC ile üretildi, `VirtualChannelEntry`
> export doğrulandı). Yine de kendi ortamınızda yeniden derlemeniz önerilir.

## Adım 2 — İstemci PC'de eklentiyi kaydet ✅ (Setup otomatik yapar)

**Bu adım artık istemci Setup'ına dahildir.** `Print360-Client-Setup.exe`,
pakette `Print360.VC.dll` varsa onu `C:\Print360\Print360.VC.dll`'e kopyalar ve
mstsc `AddIns` kaydını otomatik yapar (hem `HKLM\SOFTWARE`, hem
`HKLM\SOFTWARE\WOW6432Node` — 64/32-bit mstsc için):

```
[HKLM\SOFTWARE\Microsoft\Terminal Server Client\Default\AddIns\Print360]
"Name"="C:\Print360\Print360.VC.dll"
```
Kaldırmada bu kayıtlar da temizlenir. Kayıt, **bir sonraki RDP oturumunda**
etkinleşir (mevcut oturum yeniden başlatılmalı).

`build.ps1`, `vc/Print360.VC.dll` mevcutsa otomatik istemci paketine ekler.

## Adım 3 — Sunucuya entegre et

`server/Print360.VChannel.cs`'i ServerAgent derlemesine ekleyin ve
`Dispatch()` içinde, HTTPS kuyruğundan **önce** deneyin:

```csharp
// pdf: isin baytlari, name: hedef dosya adi
string kanal = null;
if (pdf != null && VChannel.Gonder(name, pdf))
    kanal = "VirtualChannel";          // RDP tunelinden gitti (ayar gerekmez)
// kanal == null ise mevcut HTTPS kuyruk / tsclient akisi devreye girer
```
`VChannel.Gonder`, kanal yoksa (eklenti kurulu değil / RDP dışı) `false` döner
ve sistem otomatik HTTPS'e düşer — **geriye tam uyumlu**.

build.ps1'de ServerAgent derleme satırına `$root\server\Print360.VChannel.cs`
eklenmelidir.

## Adım 4 — Gerçek RDS'de test

1. Sunucuda ServerAgent'ı (VChannel entegre) çalıştırın.
2. İstemci PC'de eklenti kayıtlıyken RDP ile sunucuya bağlanın.
3. Sunucuda "Print360 - kullanıcı" yazıcısına yazdırın.
4. Beklenen: İş RDP tünelinden gelir, istemcide `C:\Print360\jobs`'a düşer,
   ClientAgent basar. Sunucu logunda `[VirtualChannel]` görünür.
5. Eklenti kaldırılırsa: sistem otomatik `[HTTPS-kuyruk]`'a döner (regresyon yok).

---

## Notlar ve öneriler

- **SVC vs DVC:** Bu iskelet **Static Virtual Channel** kullanır (basit). Üretim
  için **Dynamic Virtual Channel** (`IWTSPlugin` COM arayüzü) daha modern ve
  esnektir (Windows Vista+); geçiş, istemci eklentisini COM olarak yeniden
  yazmayı gerektirir, protokol ve sunucu tarafı benzer kalır.
- **Kanal adı** SVC'de en fazla 7 karakter — `P360` kullanıldı.
- **Güvenlik:** Kanal RDP'nin kendi şifreli tüneli içindedir; ek TLS gerekmez.
  İstemci şifresi (ClientKey) burada gerekmez çünkü RDP oturumu zaten kimlik
  doğrulanmıştır — bu, VC'nin bir başka avantajıdır.
- **Ters yön (UYGULANDI, çift yönlü):** İstemci→sunucu onay/sayaç/heartbeat/
  yazıcı-sağlığı aynı kanaldan gider. Akış:
  `ClientAgent → vc-outbox\*.msg → Print360.VC.dll (pVirtualChannelWrite) → kanal
  → ServerAgent (WTSVirtualChannelRead) → SQL`.
  Ters protokol: `[turLen:4 LE][tur][veriLen:4 LE][veri]`, tur ∈ {`SAYAC`,`HB`,`YAZICI`}.
  `VCMode=1` (istemci) + `VirtualChannel=1` (sunucu) ile HTTPS tamamen devre dışı
  kalır. `VCMode=0` ise onaylar eskisi gibi HTTPS API'sinden gider (geriye uyumlu).
- **Bakım:** VC eklentisi native olduğu için otomatik güncelleme (self-update)
  kapsamı dışındadır; sürüm değişiminde istemci Setup ile güncellenmelidir.

## Özet

Sunucu tarafı hazır ve derlendi; istemci eklentisi referans olarak yazıldı.
Tamamlamak için: (1) SDK'lı makinede `Print360.VC.dll`'i derle, (2) istemci
Setup'ına DLL + registry kaydı ekle, (3) ServerAgent'a `VChannel.Gonder`
çağrısını ekle, (4) gerçek RDS'de test et. Her adım mevcut HTTPS sistemini
bozmadan, ona ek olarak çalışır.
