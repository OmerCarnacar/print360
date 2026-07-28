# Değişiklik Günlüğü

Bu proje [Semantic Versioning](https://semver.org/lang/tr/) benzeri bir
`ANA.ALT.YAPIM` şeması kullanır. Yapım numarası her derlemede artar ve
paket adında üretim tarihi de yer alır (`v1.1.49-2707-2141`).

---

## [1.1.2] — 2026-07-28

Bakım sürümü. **Yazılım işlevinde değişiklik yoktur.**

### Değişenler

- Başka bir ürünün **ticari markasına** yapılan tüm atıflar (26 satır) kod
  yorumlarından, belgelerden, kurulum sihirbazı metinlerinden ve depo
  konularından kaldırıldı; nötr ifadelerle değiştirildi ("kanal mantığı",
  "iş türü modeli", "PDF modu", "yazıcı seçim modu").
- Depodaki **ekran görüntülerinden** geliştirme ortamına ait makine adı,
  sunucu IP'si ve ağdaki gerçek yazıcının adı kaldırıldı; örnek değerlerle
  (`OFIS-PC`, `SUNUCU01`, `OFIS-YAZICI`) yeniden çizildi.
- `Print360.ClientAgent.cs` içindeki çoklu sunucu örneği gerçek bir sunucu
  adresi içeriyordu; jenerik `SRV01,SRV02,SRV03` ile değiştirildi.

---

## [1.1.1] — 2026-07-28

Lisans ve hukuki metin sürümü. **Yazılım işlevinde değişiklik yoktur**;
1.1 kullananların güncellemesi zorunlu değildir.

### Değişenler

**Lisans**
- Lisans **türü** açıkça belirtildi: kaynağı açık, ücretsiz, **tescilli
  (proprietary)**. Satış yasağı içerdiği için OSI'nin Açık Kaynak Tanımı'nı
  karşılamadığı, MIT/GPL/Apache **olmadığı** ayrıca yazıldı.
- **Garanti reddi** genişletildi: uyumluluk, kesintisiz çalışma ve çıktının
  üretileceği garantisi verilmediği kapsama alındı.
- **Sorumluluk reddi** ayrı bölüm oldu. Ürün bedelsiz sunulduğu için toplam
  sorumluluk **sıfır**; veri/belge kaybı, çıktının yanlış yazıcıya gitmesi ve
  doğan gizlilik ihlali, iş kesintisi, kâr kaybı, sarf malzemesi maliyeti ve
  üçüncü kişi zararları açıkça kapsam dışında bırakıldı.
- **Kullanıcının sorumluluğu** eklendi: üretim öncesi test, sistem yedeği,
  güvenlik ve mevzuat uyumu.
- **KVKK / GDPR**: kayıt altına alınan kullanıcı adı, bilgisayar adı, belge
  adı ve sayfa sayısı için **veri sorumlusunun yazılımı kuran kurum** olduğu
  belirtildi. Yazılım dışarıya veri göndermez.
- **Destek yükümlülüğü bulunmadığı** ve geliştirmenin haber verilmeksizin
  durdurulabileceği belirtildi.
- İngilizce bölüm özet olmaktan çıkarılıp **tam çeviriye** dönüştürüldü
  (uyuşmazlıkta Türkçe metin asıldır).

### Düzeltilenler

- Kurulum sihirbazının **lisans sayfasında Türkçe karakterler bozuk**
  görünüyordu. Inno Setup, BOM'suz düz metni seçili dilin ANSI kod sayfasıyla
  okuyor; UTF-8 olan lisans dosyası bu yüzden okunaksız çıkıyordu. Derleme
  artık sihirbaz için BOM'lu bir kopya üretiyor.
- Sürekli tümleştirme (GitHub Actions) derlemesi `CS1567: Error generating
  Win32 resource` ile başarısız oluyordu; `csc.exe` varsayılan Win32
  kaynağını çıktı klasöründe geçici dosyaya yazdığı hâlde klasör
  oluşturulmuyordu.

---

## [1.1] — 2026-07

İlk açık sürüm.

### Eklenenler

**Yazdırma**
- RDP / Terminal Server oturumlarından yerel yazıcılara sürücüsüz yazdırma
- Kullanıcı başına tek sanal yazıcı: `Print360 - <kullanıcı>`
- Üç yazdırma modu: doğrudan varsayılan yazıcıya · yazıcı seçim penceresi · PDF olarak aç
- Belgeler **orijinal adıyla** kaydedilir (tarih-saat yerine)
- Çift baskı motoru (SumatraPDF + Windows `printto`) ve 3 deneme
- Başarısız işler `failed` klasörüne alınır, kaybolmaz

**İstemci**
- Kişiye özel **öncelik sıralı** yazıcı seçimi; 1. yazıcı kapalıysa yedeğe düşer
- İlk açılışta Windows varsayılan yazıcısı otomatik atanır
- Durum penceresi: bağlantı · yazıcılar · görevler · günlük
- Yazdırma bitince kısa "Yazdırıldı" bildirimi
- Açık RDP oturumundan sunucuyu otomatik bulma (IP/port girmeye gerek yok)
- **Çoklu RDP**: aynı anda birden fazla sunucudan iş alma
- Otomatik güncelleme (sunucudaki sürümü izler)

**Sunucu / panel**
- Web paneli (HttpListener) + masaüstü paneli (saf WPF)
- Bağlı istemciler, makine/kullanıcı/kâğıt/yazıcı bazlı sayaçlar
- Maliyet hesabı, günlük sayfa kotası, kullanıcı/makine engelleme
- Yazıcı sağlık takibi (WMI) ve uyarılar
- PDF arşivi (90 gün) ve panelden indirme
- Active Directory entegrasyonu
- Günlük e-posta raporu
- **Tanı sayfası**: yazdırma sorununu 7 adımda gösterir

**Veri katmanı**
- MSSQL **isteğe bağlı**; yoksa **SQLite**, o da yoksa CSV
- Panelden MSSQL'e geçiş yapılabilir

**Taşıma**
- RDP Virtual Channel (kanal mantığı) — IP/port/firewall gerekmez
- HTTPS kuyruğu (GZip sıkıştırmalı, dosya tabanlı — veritabanı gerekmez)
- `\\tsclient` sürücü yönlendirmesi (yedek)

**Kurulum**
- Native yapılandırıcı — **PowerShell çalıştırmaz**
- Yazıcılar Windows API ile oluşturulur (`winspool.drv`)
- "Microsoft Print to PDF" özelliği eksikse otomatik etkinleştirilir
- Zamanlanmış görev: oturum açılışı + RDP bağlantısı + çökme kurtarma
- Her adımda zaman aşımı — kurulum asla kilitlenmez
- Kurulumda eski sürüm kökten temizlenir
- Başlat menüsünde kaldırma kısayolu; kaldırmada ajan gerçekten durdurulur

### Bilinen sınırlamalar

- RDP Virtual Channel eklentisi gerçek bir RDS ortamında uçtan uca doğrulanmayı bekliyor;
  kanal açılmazsa sistem otomatik olarak HTTPS kuyruğuna düşer.
- Sunucu bileşeni yalnızca Windows Server sürümlerine kurulabilir.
