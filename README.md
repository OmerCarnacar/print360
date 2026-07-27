<div align="center">

<img src="docs/img/logo.png" alt="Print360" width="110">

# Print360

**RDP / Terminal Server oturumlarından yerel yazıcılara sorunsuz yazdırma**
_Sürücü uyumsuzluğu yok · Merkezi raporlama · Kurulumu tek tıkla_

[![Derleme](https://github.com/OmerCarnacar/print360/actions/workflows/build.yml/badge.svg)](https://github.com/OmerCarnacar/print360/actions/workflows/build.yml)
[![Son sürüm](https://img.shields.io/github/v/release/OmerCarnacar/print360?label=s%C3%BCr%C3%BCm&color=4f46e5)](https://github.com/OmerCarnacar/print360/releases/latest)
[![İndirme](https://img.shields.io/github/downloads/OmerCarnacar/print360/total?label=indirme&color=0ea5e9)](https://github.com/OmerCarnacar/print360/releases)
[![Lisans](https://img.shields.io/badge/lisans-Ücretsiz%20(satılamaz)-059669)](LICENSE)

[![Platform](https://img.shields.io/badge/platform-Windows%20Server%202016%2B-0078d4)](#gereksinimler)
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.x-512bd4)](#gereksinimler)
[![Bağımlılık](https://img.shields.io/badge/harici%20bağımlılık-yok-16a34a)](#neden-print360)
[![Yıldız](https://img.shields.io/github/stars/OmerCarnacar/print360?style=social)](https://github.com/OmerCarnacar/print360/stargazers)

</div>

---

## Sorun

RDP ile bağlanan kullanıcılar kendi bilgisayarlarındaki yazıcıdan çıktı alamaz. Klasik sebepler:

- Sunucuda **yazıcı sürücüsü yok** ya da sürüm uyuşmuyor
- **Easy Print** eski/özel yazıcılarda hatalı çalışıyor veya bozuk çıktı veriyor
- Yazıcı yönlendirmesi kapalı; açılsa bile bağlantı koptuğunda işler kayboluyor
- Kim, ne kadar, hangi kâğıda bastı — **hiçbir kayıt yok**

## Çözüm

Print360, çıktıyı sunucuda **PDF'e** çevirir ve RDP oturumu üzerinden kullanıcının kendi bilgisayarına taşır; baskı orada, kullanıcının **kendi yazıcısıyla** yapılır.

```
┌── RDP SUNUCUSU ────────────────┐        ┌── KULLANICININ BİLGİSAYARI ──┐
│                                │        │                              │
│  Ctrl+P → "Print360 - ali"     │        │  Print360 istemcisi          │
│         ↓                      │        │         ↓                    │
│  PDF olarak yakalanır          │        │  Kendi yazıcısına basar      │
│         ↓                      │        │         ↓                    │
│  Sıkıştırılır ────── RDP / HTTPS ──────► │  "Yazdırıldı" bildirimi      │
│         ↓                      │        │         ↓                    │
│  Panel: kim/ne/kaç sayfa  ◄────────────── onay + sayaç                 │
└────────────────────────────────┘        └──────────────────────────────┘
```

**Sunucuda tek bir gerçek yazıcı sürücüsü kurulmaz.** Yazıcı eski ya da yeni olsun fark etmez — baskıyı zaten o yazıcıyı tanıyan bilgisayar yapar.

---

## Ekran görüntüleri

### Yönetim paneli (web)
<img src="docs/img/web-panel.png" alt="Web paneli">

### İstemci durum penceresi
<p>
<img src="docs/img/istemci-durum.png" alt="İstemci durumu" width="49%">
<img src="docs/img/istemci-yazicilar.png" alt="Yazıcı seçimi" width="49%">
</p>

### Yazdırma bildirimi
<img src="docs/img/bildirim.png" alt="Bildirim" width="420">

---

## Neden Print360

| | |
|---|---|
| 🔌 **Sıfır ayar** | İstemci, açık RDP oturumundan sunucuyu kendisi bulur. IP/port yazmanız gerekmez. |
| 🗄️ **Veritabanı zorunlu değil** | MSSQL varsa kullanır; yoksa **SQLite**'a, o da yoksa CSV'ye yazar. Hiçbir kurulum gerektirmez. |
| 📦 **Harici bağımlılık yok** | Yalnızca .NET Framework 4.x. Panel saf WPF, kurulum native — PowerShell bile çalıştırmaz. |
| 🖨️ **Kişiye özel yazıcı** | Her kullanıcı kendi **öncelik sırasını** belirler: 1. yazıcı kapalıysa 2.'ye düşer. |
| 🔀 **Çoklu RDP** | Aynı anda birden fazla sunucuya bağlıysanız hepsinden iş alır. |
| 📊 **Merkezi raporlama** | Kullanıcı/makine/kâğıt/yazıcı bazlı sayaçlar, maliyet, kota, uyarılar. |
| 🧩 **Tanı sayfası** | "Yazdırdım ama gelmedi" durumunda 7 adımlık kontrol listesi sorunu adıyla söyler. |
| 🆓 **Ücretsiz** | Lisans anahtarı yok, çıktı limiti yok. |

---

## Kurulum

### Gereksinimler

| | Sunucu | İstemci |
|---|---|---|
| İşletim sistemi | Windows Server 2016 / 2019 / 2022 / 2025 | Windows 10 / 11 |
| Çalışma zamanı | .NET Framework 4.x | .NET Framework 4.x |
| Yazıcı özelliği | "Microsoft Print to PDF" _(kurulum kendisi açar)_ | — |
| Veritabanı | **isteğe bağlı** (MSSQL) | — |

### Adımlar

1. **[⬇ Son sürümü indirin](https://github.com/OmerCarnacar/print360/releases/latest)** (ZIP).
2. **Sunucuda** `Print360-Server-Setup.exe` — yönetici olarak çalıştırın.
   Sihirbaz kullanıcıları, yazdırma modunu, portları ve (isterseniz) MSSQL'i sorar.
3. **Kullanıcı bilgisayarlarında** `Print360-Client-Setup.exe`.
   Açık bir RDP oturumu varsa sunucu adresi **otomatik dolar**.
4. Sunucudaki RDP oturumunuzda `Print360 - <kullanıcı>` yazıcısına yazdırın.

> Çıktı, kullanıcının kendi bilgisayarındaki varsayılan yazıcıdan çıkar ve
> ekranın sağ altında **"Yazdırıldı"** bildirimi görünür.

### Panel

```
https://<sunucu>:8443      (veya http://<sunucu>:8360)
```
Masaüstünde **Print360 Panel** kısayolu da oluşturulur.

---

## Nasıl çalışır

| Katman | Görev |
|---|---|
| **Sanal yazıcı** | Kullanıcı başına `Print360 - <kullanıcı>`; çıktıyı doğrudan spool dosyasına yazar |
| **Sunucu ajanı** | Spool'u izler, belge adı/sayfa sayısını olay günlüğünden okur, GZip'leyip kuyruğa alır |
| **Taşıma** | 1) RDP Virtual Channel 2) HTTPS kuyruğu 3) `\\tsclient` — sırayla denenir |
| **İstemci ajanı** | İşi alır, kullanıcının öncelik sırasına göre yazıcıya basar, onay döner |
| **Panel** | Web (HttpListener) + masaüstü (WPF); MSSQL / SQLite / CSV üzerinde çalışır |

Ayrıntılar: [docs/VIRTUALCHANNEL.md](docs/VIRTUALCHANNEL.md)

---

## Kaynaktan derleme

```powershell
# Gereken: .NET Framework 4.x SDK (csc.exe) + Inno Setup 6
powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.1
```

Üretilenler:
- `dist/Print360-Server-Setup.exe`
- `dist/Print360-Client-Setup.exe`
- `Print360-Kurulum-v<sürüm>-<ggAA-ssdd>.zip`

RDP Virtual Channel eklentisini yeniden derlemek için (MSVC + Windows SDK):
```cmd
cd vc && build-vc.cmd
powershell -File test-protokol.ps1      :: protokol testi (kanal gerekmez)
```

---

## Sorun giderme

Panelde **Tanı** sayfası (`/tani`) şunları adım adım gösterir:

1. Yazıcı sürücüsü kurulu mu
2. Print360 yazıcıları oluşmuş mu (+ port yolları)
3. Sunucu ajanı çalışıyor mu
4. Oturumun RDP istemci adı belirlenebiliyor mu
5. Spool'da bekleyen iş var mı
6. Gönderim kuyruğu
7. Ajan ve kurulum günlüklerinin son satırları

Günlükler: `C:\Print360\logs\`

---

## SSS

<details>
<summary><b>MSSQL kurmak zorunda mıyım?</b></summary><br>
Hayır. Kurulumda atlayabilirsiniz; sistem SQLite'a yazar (kurulum gerektirmez).
İsterseniz sonradan panelden <b>Veritabanı</b> sayfasından MSSQL'e geçebilirsiniz.
</details>

<details>
<summary><b>Kullanıcının birden fazla yazıcısı varsa hangisine basar?</b></summary><br>
Kullanıcının kendi belirlediği <b>öncelik sırasına</b> göre. İlk yazıcı kapalı/hatalıysa
otomatik olarak yedeğe düşer. Hiç seçim yapılmamışsa Windows varsayılan yazıcısı kullanılır.
Rastgele bir yazıcıya <b>asla</b> gönderilmez.
</details>

<details>
<summary><b>İstemci sunucuyu nasıl buluyor?</b></summary><br>
Açık RDP bağlantılarını (TCP 3389) tespit edip her birinde Print360 arar.
Aynı anda birden fazla sunucuya bağlıysanız hepsinden iş alır.
İsterseniz elle de yazabilirsiniz: <code>Server=SRV01,SRV02</code>
</details>

<details>
<summary><b>Çıktılar sunucuda saklanıyor mu?</b></summary><br>
Evet, <code>C:\Print360\archive</code> altında 90 gün (GZip'li). Panelden indirilebilir.
Süre dolunca otomatik temizlenir.
</details>

---

## Lisans

**Ücretsiz sürüm.** Sınırsız kullanılabilir, bedelsiz dağıtılabilir.
**Para karşılığı satılamaz**, kiralanamaz, ücretli bir ürünün parçası olarak sunulamaz.
Ayrıntılar: [LICENSE](LICENSE)

---

<div align="center">

**Ömer ÇARNAÇAR** — Geliştirici

[omer.carnacar@outlook.com.tr](mailto:omer.carnacar@outlook.com.tr) · [LinkedIn](https://www.linkedin.com/in/omercarnacar/)

<sub>Faydalı bulduysanız ⭐ vermeyi unutmayın.</sub>

</div>
