# Değişiklik Günlüğü

Bu proje [Semantic Versioning](https://semver.org/lang/tr/) benzeri bir
`ANA.ALT.YAPIM` şeması kullanır. Yapım numarası her derlemede artar ve
paket adında üretim tarihi de yer alır (`v1.1.49-2707-2141`).

---

## [1.1.6] — 2026-08-21

### 🔒 Güvenlik

- **Parolasız panel artık uzaktan açılamıyor.** Ne `PanelUsers` tablosu ne
  `panel.pwd` tanımlıysa panel kimlik doğrulaması olmadan servis ediliyordu.
  Panel tüm arayüzlerden dinlediği için ağdaki herkes baskı geçmişini, belge
  adlarını, tanı sayfasını ve arşivlenmiş çıktıları parolasız okuyabiliyordu.

  Panel korumasızken artık yalnızca **sunucunun kendisinden** açılabiliyor;
  uzaktan gelen istek gerekçesini ve çözümünü anlatan bir sayfayla reddedilip
  güvenlik günlüğüne yazılıyor. Yönetici kilitlenmiyor — sunucu üzerinden panel
  çalışmaya devam ediyor. Genel Bakış'a "Bu panel parolasız" uyarısı eklendi ve
  kurulum sihirbazındaki metin netleştirildi.

  İstemci `/api/*` uç noktaları bu kontrolden **önce** karşılandığı için
  yazdırma işleyişi etkilenmez.

### Düzeltilenler

- **Günlük dosyaları süresiz büyüyordu.** Ne boyut sınırı ne devretme vardı;
  yoğun bir sunucuda aylar içinde diski doldurabilirdi. Üç bileşene de
  (sunucu ajanı, istemci ajanı, panel) 5 MB sınırı ve tek kuşaklık devretme
  eklendi.

### Kaldırılanlar

- **Ölü lisans kodu.** Sunucu ajanında deneme sürümü engeli duruyordu: iş
  sayısını sayıp limiti aşarsa spool dosyasını siliyordu. Sınır sonsuz
  ayarlandığı için çalışmıyordu, ancak kod, RSA doğrulama altyapısı ve
  `license.key` okuma mantığı yerindeydi. Kaynağa bakan biri "çıktı limiti var"
  izlenimi ediniyordu — ücretsiz bir üründe yanıltıcı. Sunucu ajanından 24,
  panelden 40 satır kaldırıldı; `Print360.License.cs` 84 satırdan 29'a indi ve
  yalnızca ürün kimliği kaldı.

---

## [1.1.5] — 2026-07-28

Belge ve depo politikası sürümü. **Yazılımda değişiklik yoktur**; kurulum
paketi v1.1.4 ile aynıdır.

### Değişenler

- **`main` dalı korumaya alındı.** Doğrudan gönderim kapatıldı; değişiklikler
  pull request ile gelir. Zorla gönderim (force push) ve dal silme engellendi.
  Depo sahibi baypas edebilir, böylece sürüm çıkarma akışı bozulmaz.
- **Katkı yolu netleştirildi:** katkı vermek için depoyu **çatallamak (fork)**
  gerekiyor. README'ye uyarı kutusu, CONTRIBUTING'e `fork → dal → PR` akışı
  kopyalanabilir komutlarla eklendi.
- CONTRIBUTING'e, fork'un katkı verenin kendisine ait olduğu ve lisans
  koşulları içinde kendi sürümünü geliştirip dağıtabileceği yazıldı.

---

## [1.1.4] — 2026-07-28

### Eklenenler

- **Panelde bağlı yazıcılar yeşil "Aktif" rozetiyle görünüyor.** Yazıcı
  Sağlığı sayfasına **Bağlantı** sütunu ve kartlara **● Aktif (bağlı)**
  sayacı eklendi. Dört durum var: 🟢 Aktif · 🟡 Sorunlu · 🔴 Çevrimdışı ·
  ⚪ Pasif. Yeşil noktanın nabız animasyonu canlı bağlantıyı gözle ayırt
  etmeyi kolaylaştırır.

  "Aktif" için **iki şart birden** aranır: istemci ajanının raporu taze
  olmalı (son 5 dakika) **ve** yazıcı yazdırmaya hazır olmalı. Ajan
  durduğunda yazıcı "Hazir" görünse bile rozet **Pasif**'e düşer — çünkü o
  an gerçekten yazdırılamaz. Yalnızca son bilinen duruma bakmak, panelde
  yeşil görünen ama çıktı vermeyen yazıcılara yol açardı.

- Genel Bakış → **Bağlı İstemciler** tablosundaki düz "● Çevrimiçi" metni
  aynı rozet diline geçirildi.

---

## [1.1.3] — 2026-07-28

### Eklenenler

- **Tepsi simgesi ipucu canlı durum gösteriyor.** Fareyle simgenin üzerine
  gelindiğinde üç satır görünüyor: bağlantı durumu (birden fazla sunucuya
  bağlıysa sayısıyla), çıktının gideceği yazıcı ve bekleyen iş sayısı —
  bekleyen iş yoksa son temas saati. Bağlantı yokken `Sunucu araniyor` /
  `RDP bekleniyor` yazıyor. İpucu 4 saniyede bir tazeleniyor.

  Önceden ipucu yalnızca bağlantı durumu değiştiğinde güncelleniyor ve
  tek satır (`Print360 - Bagli`) gösteriyordu.

  Not: Windows tepsi ipucu alanı .NET tarafında 63 karakterle sınırlıdır;
  sabit satırlar yazıldıktan sonra yazıcı adı kalan yere göre kısaltılır.
  Bu nedenle bazı etiketler kısaltıldı (`Son temas` artık saniye
  göstermiyor).

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
