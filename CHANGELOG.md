# Değişiklik Günlüğü

Sürüm numarası **üretim tarihidir**: `YIL.AY.GÜN.SAATDK`
(örnek: `2026.08.21.1904` → 21 Ağustos 2026, saat 19:04).

Sunucu ve istemci bileşenlerinin **tamamı aynı numarayı taşır**; farklı
numaralar görüyorsanız taraflardan biri güncellenmemiş demektir.

_2026.08.21 öncesi sürümler `1.1.x` şemasıyla numaralandırılmıştır._

---

## [2026.08.21.1945]

### 🐞 Sonsuz onay döngüsü kesildi

İstemci aynı işi saniyede ~8 kez onaylıyor ve durmuyordu. Sebep: iş çekme
döngüsü "kuyruk boşalana kadar" çalışıyordu ve dosya zaten alınmışsa bile onay
gönderip *başarılı* sayılıyordu. Sunucu onayı "OK" ile yanıtlayıp dosyayı
**silemeyince** aynı iş kuyruğun başında kalıyor, istemci onu tekrar görüyor,
tekrar onaylıyordu.

**Sunucu:**
- Makine başına kuyruk kilidi — çok iş parçacıklı sunucuda aynı makinenin eş
  zamanlı iki isteği aynı dosyayı alamaz.
- Silme beş kez denenir ve **sonucu doğrulanır**. Silinemezse istemciye `500`
  dönülür ve günlüğe `ONAY ISLENEMEDI ... sebep` yazılır. Önceden silme sessizce
  başarısız olup yine "OK" dönülüyordu.

**İstemci:**
- Zaten alınmış iş için döngü durur.
- Onay reddedilirse `ONAY REDDEDILDI` yazılır, başarılı sanılmaz.
- Sunucu aynı işi arka arkaya veriyorsa dakikada bir uyarı yazılıp beklenir.
- Tur başına sert sınır: en fazla 50 iş. Hiçbir koşulda sonsuz döngü olamaz.

---

## [2026.08.21.1919]

### 🐞 Sunucu tek iş parçacıklıydı — işler sırayla inmiyordu

İlk yazdırma sorunsuz tamamlanıyor, hemen ardından gelen istek zaman aşımına
uğruyordu:

```
19:14:11  Tamamlandi: ...Belgeyi Yazdir.pdf -> Pantum M7100DW
19:14:11  Is alinamadi: Islem zaman asimina ugradi
```

**Sebep:** panelin dinleyici döngüsü isteği döngünün içinde, tek iş parçacığında
işliyordu. Yavaş bir istemciye 184 KB'lık iş yazarken **sunucunun tamamı bloke
oluyordu**. İstemci ilk işi alıp hemen ikincisini istiyor, sunucu hâlâ birinciyi
bitirmediği için istek zaman aşımına uğruyordu. Aynı sebeple ikinci bir
istemcinin kalp atışı ve panelin kendisi de bekliyordu.

**Düzeltme:** her istek artık iş parçacığı havuzuna devrediliyor; yavaş bir istek
diğerlerini bekletmiyor.

### 🔍 Adım adım günlük — sorun sunucuda mı istemcide mi?

Her aşama süresiyle birlikte kaydediliyor:

```
İstemci  Is alindi [HTTPS]: belge.pdf | sunucu yaniti 180 ms | indirme 420 ms
                             | 184 KB sikistirilmis -> 512 KB
İstemci  Onay gonderildi: belge.pdf  (95 ms)
Sunucu   Is verildi -> DESKTOP-01 | belge.pdf | 184 KB | 340 ms
Sunucu   Onay alindi <- DESKTOP-01 | belge.pdf | kuyruktan dusuruldu
```

Hata durumunda **hangi aşamada** takıldığı yazılıyor:

| Aşama | Sorun nerede |
|---|---|
| `sunucudan yanit bekleniyor` | Sunucu geç cevap veriyor |
| `is indiriliyor` | Ağ / aktarım yavaş |
| `onay (ACK) gonderiliyor` | Sunucu meşgul |

İki tarafın süresi karşılaştırılınca gecikmenin nerede olduğu tartışmasız görülür.
İlk hata artık hemen kaydediliyor (önceden iki dakika bekliyordu).

---

## [2026.08.21.1904]

### Değişenler

- **Sürüm numarası artık üretim tarihi.** `YIL.AY.GÜN.SAATDK` biçiminde
  (`2026.08.21.1904`). Elle artan `1.1.58` gibi bir numara, bir kurulumun ne
  zaman üretildiğini söylemiyordu; sahada "güncelledim" denip eski paketin
  kurulması tekrar tekrar yaşandı. Tarih tabanlı sürümde hangi paketin daha yeni
  olduğu bakışla anlaşılır.

- **Sürüm her alanda aynı.** Kurulum sihirbazlarının `AppVersion` alanı artık
  derlemeden geliyor; `.iss` dosyalarında elle `1.1` yazıyordu ve güncelliğini
  yitirmişti. Paket adı da sadeleşti: `Print360-Kurulum-v<sürüm>.zip`.

- **Sürüm tutarlılık denetimi.** Derlemenin sonunda beş bileşen ve iki kurulum
  dosyasının aynı numarayı taşıdığı denetleniyor; sapan varsa yapım durduruluyor.
  Kısmi bir derleme, sunucu ile istemcinin farklı sürümde kalmasına yol
  açabiliyordu.

- **`SURUM.txt` artık üretiliyor.** Statik bir dosyaydı: sürümü "1.1" diyor,
  özellik listesinde kaldırılmış bileşenlerden ve zorunlu olmayan MSSQL'den
  bahsediyordu.

### Düzeltilenler

- **İşler istemciye inmiyordu — asıl sebep bulundu.** Ölçüm: TCP el sıkışması
  ~25 ms, ama **ilk HTTPS isteği 17-27 saniye**; sonraki istekler ~70 ms. Maliyet
  TLS el sıkışmasındaki sertifika zinciri doğrulamasında (kendinden imzalı
  sertifikanın iptal listesine ulaşılamıyor, Windows zaman aşımını bekliyor).

  Bir önceki sürümde keep-alive kapatılmıştı; bu, her isteği yeni bağlantıya
  zorlayarak o bedeli her yoklamada ödetti ve istekler 20 saniyelik sınırda
  zaman aşımına uğradı. **Keep-alive geri açıldı**, zaman aşımı 60 saniyeye
  çıkarıldı ve keep-alive'ın bilinen riski (sunucunun boşta kalan bağlantıyı
  kapatması) için tek seferlik yeniden deneme eklendi.

---

## [1.1.8] — 2026-08-21

### 🐞 İşler istemciye inmiyordu

Sunucu kuyruğunda işler birikiyor, istemci "bağlı" görünüyor ama hiçbir işi
almıyordu. İstemci günlüğünde işlerle ilgili tek satır bile yoktu.

**Sebep:** keep-alive uyuşmazlığı. Sunucu boşta kalan HTTP bağlantısını
kapatıyor, .NET onu hâlâ canlı sanıp yeniden kullanıyor ve istek
*"Canlı tutulacağı beklenen bir bağlantı sunucu tarafından kapatıldı"* hatasıyla
düşüyordu. Üç saniyede bir yoklama yapan bir istemcide bu sürekli tekrarlıyordu.

**Düzeltme:** tüm istemci HTTP istekleri artık taze bağlantı açıyor
(`KeepAlive = false`); iş indirmesi için okuma zaman aşımı 60 saniyeye çıkarıldı.

### Düzeltilenler

- **İş çekme hatası sessizce yutuluyordu.** `catch (WebException) { return false; }`
  yüzünden sunucuda işler birikirken istemci günlüğünde hiçbir iz olmuyordu.
  Artık kaydediliyor — döngü 3 saniyede bir döndüğü için 2 dakikada bir ile
  kısıtlı.
- **Güvenlik ağı:** RDP kanalı açık görünse bile HTTPS kuyruğu ~30 saniyede bir
  yoklanıyor. Kanalın açık sayılması `.vc-aktif` dosyasının varlığına bakıyordu;
  RDP oturumu anormal koptuğunda bu dosya geride kalıp istemcinin kuyruğu hiç
  yoklamamasına yol açabilirdi.

---

## [1.1.7] — 2026-08-21

### Eklenenler

- **Kurulum sessizce hiçbir şey yapmadığında artık uyarıyor.** Sihirbaz
  dosyaları kopyalayıp "tamamlandı" diyebiliyor, ancak asıl işi yapan
  yapılandırma adımı hiç çalışmamış olabiliyordu; kullanıcının bunu anlamasının
  tek yolu günlüğe bakmaktı.

  Sihirbaz artık her çalışmada benzersiz bir işaret üretip yapılandırıcıya
  geçiriyor. Yapılandırıcı işini bitirince bu işareti
  `C:\Print360\logs\son-kurulum.txt` dosyasına yazıyor; sihirbaz kurulum
  sonunda dosyayı okuyup işareti arıyor. Bulamazsa **ne olmadığını ve ne
  yapılması gerektiğini** anlatan bir uyarı gösteriyor. Hem sunucu hem istemci
  kurulumunda geçerli.

### Düzeltilenler

- **Kurulum günlüğündeki iki yanıltıcı uyarı.** `netsh urlacl` port ayrımı zaten
  varsa 183 (`ERROR_ALREADY_EXISTS`) dönüyordu; bu bir hata değil, istenen sonuç
  zaten sağlanmış demek. Artık önce siliniyor sonra ekleniyor, günlüğe hata
  satırı düşmüyor. `icacls` adımının zaman aşımı 30 saniyeden 60'a çıkarıldı —
  büyük klasörlerde her kurulumda "zaman aşımına uğradı" uyarısı düşüyordu.

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
