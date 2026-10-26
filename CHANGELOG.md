# Değişiklik Günlüğü

Sürüm numarası **üretim tarihidir**: `YIL.AY.GÜN.SAATDK`
(örnek: `2026.08.21.1904` → 21 Ağustos 2026, saat 19:04).

Sunucu ve istemci bileşenlerinin **tamamı aynı numarayı taşır**; farklı
numaralar görüyorsanız taraflardan biri güncellenmemiş demektir.

_2026.08.21 öncesi sürümler `1.1.x` şemasıyla numaralandırılmıştır._

---

## [Yayınlanmamış]

### Düzeltildi
- **Birden fazla RDP oturumu:** istemci yalnızca İLK oturumun sunucusunu
  izliyordu. İkinci bir sunucuya açılan oturum ne günlüğe yazılıyor ne de
  bağlantı döngüsünü hemen uyandırıyordu (60 sn'ye kadar gecikme). Oturumlardan
  biri kapanınca hiçbir şey yazılmıyordu. Bağlantı listesinin sırası değişince
  de sahte "RDP oturumu AÇILDI" satırları çıkıyordu. Artık oturumların tamamı
  izleniyor; her açılan ve kapanan oturum adıyla yazılıyor ("açık kalan: ..."
  bilgisiyle).
- **Veritabanısız modda makine bilgileri:** boş gelen bir kalp atışı, bellekteki
  yazıcı adını siliyordu. Makineler sayfasındaki kullanıcı ve işletim sistemi
  sütunları da boş kalıyordu. Boş gelen alan artık mevcut değeri ezmiyor.
- **Testler gerçek veriye yazıyordu:** uzman ve performans testleri, kimlik
  defterini yalnızca 13. senaryoda geçici klasöre yönlendiriyordu. Önceki kalp
  atışları (performans testinin 300 sahte makinesi dahil) testin çalıştığı
  makinedeki GERÇEK `C:\Print360\stats\kimlikler.csv` dosyasına yazılıyordu.
  Yönlendirme artık ilk istekten önce yapılıyor. Ayrıca iki test de çalışmadan
  önce ve sonra gerçek `C:\Print360` veri dosyalarını karşılaştırıyor; bir
  dosyaya dokunulmuşsa test KALIYOR.

---

## [2026.10.23.0936]

### Lisans: açık kaynak
- Print360 artık **açık kaynaktır**: GNU GPL v3 veya sonrası (`GPL-3.0-or-later`).
  `LICENSE` dosyası GPL-3.0'ın resmî metnidir; Türkçe özet, marka ve sorumluluk
  notu `NOTICE` dosyasındadır.
- Yazılım yine **ücretsiz ve sınırsızdır**. Değişen şu: eski lisanstaki satış
  yasağının yerine GPL'in koşulu geldi. Dağıtılan her sürümün kaynak kodu da
  GPL-3.0 ile sunulmalıdır; kapalı kaynaklı türev ürün dağıtılamaz.
- **"Print360" adı ve logosu** lisans kapsamında değildir (GPL-3.0 madde 7(e)).
- Kurulum sihirbazı önce Türkçe özeti, ardından GPL metnini gösterir; dağıtım
  paketine `NOTICE.txt` eklendi. Panel, istemci penceresi ve dosya özelliklerindeki
  "para ile satılamaz" ifadeleri güncellendi.
- 2026.10.21.1125 ve önceki sürümler yayınlandıkları lisansla kalır.

### Düzeltildi
- **Kurulum kılavuzu (`KURULUM.txt`) güncel değildi:** kaldırılmış deneme
  modundan ("3 çıktı", lisans anahtarı), zorunlu SQL Server'dan ve internet
  gereksiniminden söz ediyordu. Güncel durumla yeniden yazıldı: lisans anahtarı
  yok, veritabanı isteğe bağlı (yoksa SQLite), internet gerekmez; tanı paketi
  ve hata bildirim adresi eklendi.

## [2026.10.21.1125]

Performans ölçümü ve düzeltmeleri. Ölçümler gerçek sunucu + gerçek istemci
kodu üzerinde yapıldı (`tests/UzmanTest.Performans.cs`, her yapımda ve CI'da çalışır).

### Ölçüm: önce → sonra
| Ölçüm | Önce | Sonra |
|---|---|---|
| 50 makine aynı anda iş çekiyor — iş başına p95 | 524 ms | **57 ms** |
| 300 istemci aynı anda kalp atışı (ilk tur) | 15,6 sn | **0,99 sn** |
| 50.000 işlik geçmişle CSV dışa aktarma | >100 sn (zaman aşımı) | **0,55 sn** |
| 50.000 işlik geçmişle İşler sayfası | 2,1 sn | **0,48 sn** |
| 50.000 işlik geçmişle Ana sayfa | 1,05 sn | **0,53 sn** |
| Hata ayıklama modunda 500 boş yoklama | 500 günlük satırı | dakikada 1 özet |
| İstemci: 20 dosyalık yığının hazırlık denetimi | 10,3 sn | **0,58 sn** |
| Tek makineye teslim hızı | 28 iş/sn | **36 iş/sn** |

### İyileştirildi
- **SQLite bağlantı havuzu ve WAL:** her sorgu yeni bir dosya bağlantısı
  açıyordu; okumalar artık yazmayı bekletmez.
- **Kalp atışı:** her istek kilit altında tablonun tamamını okuyup dosyayı
  baştan yazıyordu. Artık kayıt bellekte tutulur, dosya en fazla 5 sn'de bir
  yazılır, veritabanı işlemleri kilit dışında yapılır.
- **İstemci doğrulama:** her yoklama veritabanına gidiyordu; sonuç 60 sn
  hatırlanır (şifre uyuşmazsa önbellek atlanır).
- **Hata ayıklama günlüğü:** rutin istekler (boş yoklama, başarılı kalp
  atışı) dakikada bir özetlenir; hatalar, teslimler ve 1 sn'yi aşan istekler
  tek tek yazılır. 300 istemcide günde ~9 milyon satır olmaz.
- **İstemci:** hazırlık denetimi tüm yığın için tek beklemeyle yapılır;
  yazıcı durumu 30 sn hatırlanır; baskı sonrası denetim arka planda yapılır.

### Düzeltildi
- **Uyarı seli:** şifresi yanlış bir istemci her yoklamada (3 sn'de bir)
  panele "HATALI İSTEMCİ ŞİFRESİ" uyarısı yazdırıyordu — günde ~29.000 uyarı.
  Artık makine başına saatte bir.

### Test
13 test dosyası, **216 kontrol**.

### Not
Makineler sayfasının ilk açılışı Active Directory sorgusu nedeniyle etki
alanına göre birkaç saniye sürebilir; sonuç 5 dakika saklanır.

---

## [2026.10.15.1040]

Son testler: uzman testine hiç sınanmamış uçtan uca yollar eklendi
(31 → 43 kontrol) ve üç hata düzeltildi.

### Yeni senaryolar (gerçek sunucu + gerçek istemci)
| Senaryo | Sonuç |
|---|---|
| "Basıldı" bildirimi: istemci → sunucu → panel (Türkçe belge ve yazıcı adı) | Panel "Basıldı" gösterir, adlar bozulmaz |
| Sunucu kapalıyken "basıldı" bildirimi | Bekletilir, sunucu gelince gönderilir |
| Kalp atışı ve bilgisayar adı değişikliği (HTTP üzerinden) | Eski adla bekleyen işler yeni ada taşınır |
| Aynı iş iki kez onaylanıyor | İkinci onay da kabul edilir |
| İki sunucu sırayla hata veriyor | Günlük şişmez |
| Yanlış şifre + Windows vekil ayarı | Bağlantı yolu boşuna değişmez |

### Düzeltildi
- **Çift onayda yanlış alarm.** Aynı PC'de iki ajan aynı işi indirdiğinde ya
  da onay yanıtı kaybolup istemci yeniden onayladığında sunucu HTTP 500
  dönüyor, istemci "ONAY REDDEDİLDİ (500)" yazıyordu. Sunucu artık son 10
  dakikada onayladığı işleri hatırlar; tekrar onay sorunsuz kabul edilir.
- **Günlük şişmesi.** İş hatası tekrar filtresi tüm sunucular için tekti; iki
  sunucu sırayla hata verince hiçbir tekrarı bastıramıyordu (testte 40
  yoklama = 40 satır, gerçekte günde on binlerce). Filtre artık sunucu başına;
  aynı senaryo 2 satır.
- **Şifre reddi vekil sorunu sanılıyordu.** Print360'ın kendi 403'ü
  bağlantı yolunu boşuna değiştiriyordu. Sunucu her yanıta `X-Print360`
  başlığı ekler; istemci bizden gelen yanıtta yolu değiştirmez.

### Test
12 test dosyası, **199 kontrol**. Uzman testinin arıza vekilindeki bir hata da
düzeltildi (Host başlığını değiştirirken gövdedeki Türkçe harfleri bozuyordu);
ürünün "basıldı" yolunda Türkçe karakter sorunu yoktur.

---

## [2026.10.12.0853]

`2026.10.01.0854`'ten bu yana yapılan dört çalışmanın birleşik sürümü:
uzman testi ve bulduğu hatalar, hata ayıklama modu ve tanı paketleri,
"bazı makinelerde çalışmıyor" sorunları, eski sürüm kalıntıları.

> **Önemli — SQL Server olan sunucular:** önceki sürümlerde panel, yerel
> SQL Server'a `sa` hesabıyla **boş parolayla** her açılışta ve dakikada bir
> giriş deniyordu. Bu sürüm bunu durdurur. Sunucunuzda SQL Server varsa en
> kısa sürede güncelleyin.

### Uzman testi: gerçek sunucu + gerçek istemci + arıza enjeksiyonu

`tests/UzmanTest.cs` üretimdeki panel sunucusu kodunu yerel bir portta,
üretimdeki istemci kodunu ona bağlı çalıştırır; araya gerçek ağ arızaları
üreten bir TCP vekili girer. 10 senaryo, 31 kontrol:

| Senaryo | Sonuç |
|---|---|
| Normal teslim (Türkçe, `%`, `&`, `=`, `#` içeren adlar) | İçerik bayt bayt aynı, kuyruk boşalır |
| 20 MB dosya | Bütün gelir |
| İndirmenin **ortasında** kopma | Yarım dosya yazıcıya gitmez, iş sunucuda kalır, düzelince bütün gelir |
| Onayın yolda kaybolması | Sunucu yeniden verir, istemci yeniden indirmez — **çift baskı yok** |
| Aynı PC'de iki ajan, aynı anda 20 iş | Her iş bir kez ve bütün |
| Yanlış istemci şifresi | İş verilmez, sebep ve çözüm yazılır |
| Sunucu kapalı | Hızla vazgeçilir, sebep yazılır |
| Güvenlik duvarı paketi sessizce düşürüyor | 2 sn'de elenir, sonraki turlarda bekletmez |
| İstemci işi diske yazamıyor | Karantina, arkadaki iş beklemeden gelir |
| Günlükler | Her adım süresiyle; şifre görünmez |

Aynı test **önceki sürümün istemci koduyla** 5 kontrolde kaldı — aşağıdaki
hataların gerçek olduğunun kanıtı.

### Düzeltildi (uzman testinin bulduğu)
- **SQL Server'a boş parolayla `sa` girişi denemesi.** Kurulum her sunucuda
  `db.ini`'ye `Server=<makine>`, `User=sa`, boş parola yazıyordu. Başarısız
  giriş kayıtları, güvenlik uyarıları ve parola politikası açıksa `sa`
  hesabının kilitlenmesi riski. MSSQL artık yalnızca sunucu **ve** parola
  tanımlıysa denenir; kurulum SQL seçilmediyse `sa` yazmaz.
- **İndirme ortasında kopma hiç günlüğe düşmüyordu**, yarım `.tmp` kalıyordu.
- **Aynı PC'de iki ajan aynı geçici dosyaya yazıyordu** (testte 13 artık `.tmp`).
- **Yolda kaybolan onay "REDDEDİLDİ"** diye yazılıyordu.
- **`Server=` doluysa RDP ile bağlanılan diğer sunucular hiç yoklanmıyordu**;
  onlardan yazdırılan işler makineye ulaşmıyordu.
- **Print360 olmayan bir sunucu her turda 20–60 sn bekletiyordu.** Artık
  ulaşılamayan sunucu 30 sn'den 10 dk'ya artan sürelerle atlanır; hiç yanıt
  vermemiş sunucuya önce 2 sn'lik port denetimi yapılır.

### Eklendi — hata ayıklama modu ve tanı paketleri
- **Hata ayıklama modu** (makine geneli, 24 saatte kendiliğinden kapanır):
  istemcide tepsi > *Hata ayıklama modu* ya da `--hata-ayiklama ac|kapat`;
  sunucuda panel > **Tanı**. Açıkken her HTTP isteği, süre, yazıcı kararı,
  motor çağrısı yazılır; panel her isteği (yöntem, yol, makine, IP, sonuç, süre) kaydeder.
- **İstemci tanı paketi:** tepsi > *Tanı paketi oluştur* ya da
  `Print360.ClientAgent.exe --tani [--sessiz]`. 12 bölümlük rapor (kimlik,
  ajanlar, ayarlar, RDP bağlantıları, ağ testleri, TLS, RDP eklentisi,
  yazıcılar, motor, izinler, güvenlik olayları, son hatalar) + günlükler +
  maskelenmiş ayarlar. En üstte otomatik **BULGULAR**.
- **Sunucu tanı paketi:** panel > Tanı > *Sunucu tanı paketini indir*.
- Belge içerikleri ve parolalar paketlere **dahil değildir**.

### Düzeltildi — makineye bağlı sorunlar
- Güncelleme diğer kullanıcıların sunucu ajanını yeniden başlatmıyordu; artık
  tüm oturumlarda başlatılır, ajansız kalan oturum adıyla raporlanır.
- İstemci ajanı yükseltilmiş kurulumdan (bazen BT hesabıyla) başlıyordu;
  artık oturum sahibi olarak başlar. Yanlış hesapla çalışırsa günlüğe yazar.
- İstemci kurulumu ajanın çalıştığını doğrular; çalışmıyorsa sebebini arar
  (antivirüs, AppLocker, WDAC, .NET).
- Windows vekil (proxy) ayarı bağlantıyı engelliyorsa diğer yol denenir;
  vekilin 403'ü artık "şifre reddedildi" sayılmaz; 407 ayrıca anlatılır.
- Aynı PC'de iki Windows kullanıcısı: iş kilidiyle çift çıktı önlenir.
- 32-bit Windows'ta 64-bit RDP eklentisi kaydedilmez.
- .NET Framework 4.5 yoksa kurulum uyarıp durur.

### Düzeltildi — eski sürümün üzerine kurulum
- Kalıntılar **genel kuralla** temizlenir: bu sürümün sahip olduğu öğeler
  listelidir; Print360'a ait olup listede olmayan ikili/betikler silinir,
  bilinmeyen dosya ve klasörler `eski-surum-kalintilari\<tarih>\` altına
  taşınır, kullanıcı verisine dokunulmaz. Eski Run kayıtları, görevler,
  kısayollar, güvenlik duvarı kuralları, yazıcı portları ve süresi dolmuş
  sertifikalar da temizlenir.
- İstemci kurulumu aynı makinedeki **sunucunun dosyalarını siliyordu**; düzeldi.
- Bekleyen işler korunur ve makine bazında raporlanır; veritabanında eski
  sürümlerden "BEKLİYOR" kalmış kayıtlar gerçek durumla uzlaştırılır.
- Sunucuya iletilemeyen "basıldı" bildirimi kaybolmaz; bekletilip gönderilir.

### Test
12 test dosyası, **187 kontrol** — hepsi üretim kodu üzerinde, her yapımda
ve CI'da çalışır.

### Bilinen sınırlar
- Uzman testi HTTP üzerinden çalışır (yerel HTTPS sertifikası yönetici
  gerektirir); TLS katmanı tanı paketinin ağ testleriyle denetlenir.
- Panelin SYSTEM görevi, ajanın oturum sahibi olarak başlatılması ve tüm
  oturumlarda yeniden başlatma yönetici yetkisi gerektirdiğinden canlı bir
  sunucuda ayrıca denenmedi; başarısız olursa kurulum eski yola döner ve uyarır.

---

## [2026.10.01.0854]

Baskı yolundaki **sessiz kalan** hata durumları tarandı. Her biri artık
**anında**, sebebi ve yapılacak işle birlikte günlüğe yazılıyor.

### Senaryolar

| Senaryo | Eskiden | Şimdi |
|---|---|---|
| **Bilgisayar adı değişti** | Eski adla bekleyen işler eski klasörde kalıyordu; eski ad panelde "çevrimdışı" | İstemci açılışta `BILGISAYAR ADI DEGISTI: A -> B` yazar. Sunucu aynı kurulum kimliğini yeni adla görür, 3 dk gözler; eski ad gelmezse bekleyen işleri yeni ada taşır |
| **Bağlantı koptu** | Hata 2 dk'da bir yazılıyor, araya giren farklı hatalar kayboluyordu | İlk hata anında; aynısı tekrar ederse sayılır; farklı hata hemen; düzelince `DUZELDI`. Sebep insan diliyle: DNS, port, zaman aşımı, sertifika, aktarım sırasında kopma, 403/404/500 |
| **Yazdırma yarım kaldı** (ajan baskı sırasında kapandı) | İz yoktu | Sonraki açılışta `YARIM KALDI ... yeniden yazdırılacak, çıktı iki kez gelebilir` |
| **İndirme yarım kaldı** | `.tmp` sessizce kalıyordu | Açılışta silinir: "iş sunucuda bekliyor, yeniden indirilecek" |
| **Yarım PDF** (uygulama çöktü, RDP koptu) | Yazıcıya gidiyor, bozuk sayfa ya da anlamsız hata kodu | `%PDF` / `%%EOF` denetimi. İstemci yazıcıya göndermez; sunucu tamamlanmasını bekler, 2 dk sonra `spool\_bozuk`'a alır (silmez) |
| **Klasöre erişim yok / disk dolu** | İlk işte belirsiz "Erişim engellendi" | Üç bileşen de açılışta her klasörü dener: klasör + kullanıcı + Windows hatası; 500 MB altında disk uyarısı |
| **Günlüğün kendisi yazılamıyor** | Sorunun kaydı da kayboluyordu | `%TEMP%\Print360-...` yedeğine yazılır; geçiş ve sebebi bir kez |
| **Dosya sürekli kilitli / büyüyor** | İş sonsuza kadar bekliyordu | 2 dk sonra bir kez: dosya, süre, sebep; çözülünce `TAKILMA COZULDU` |
| **Yazıcıda kâğıt yok / çevrimdışı** | "Basıldı" denip sessiz kalınıyordu | Baskıdan önce ve sonra yazıcı durumu ve Windows kuyruğundaki hatalı işler okunur; günlük + tepside uyarı |

### Ayrıca
- Aynı kurulum kimliği iki ayrı adla **sırayla** gelmeye devam ederse (disk
  klonu + aynı BIOS kimliği) iş taşınmaz, `AYNI KURULUM KIMLIGI IKI MAKINEDE`
  uyarısı verilir. Karar "eski ad gözlemlenerek" verilir; hızlı bir yeniden
  başlatmada ad değişikliği yanlışlıkla çakışma sayılmaz.
- Başlangıç satırları sürüm, hesap, bilgisayar adı ve oturum numarasını da yazar.

### Test
- `tests/OlayGunlugu.cs` (yeni, 42 kontrol): klasörün yazma izni Windows'ta
  gerçekten kaldırılarak, `%%EOF`'suz gerçek bir yarım PDF ile, tekrar
  filtresi, takılma izleyici, ad değişikliği ve çakışma kararı.
- Yazıcı durumu geliştirme makinesindeki gerçek yazıcılarla çalıştırıldı;
  çevrimdışı bir yazıcı doğru bildirildi.
- 10 test dosyası, 116 kontrol.

### Bilinen sınırlar
- Kâğıdın fiziksel olarak çıktığı doğrulanamaz; görülebilen son nokta
  Windows'un yazıcı ve kuyruk durumudur.
- Yazıcı durumu her işte iki kez okunur; iş başına ~2,5 sn eklenir.
- İstemci açılış denetimi, sunucu spool denetimi ve ad değişikliğinde taşıma
  canlı bir sunucu-istemci ortamında denenmedi; karar mantığı testlerle sınandı.

---

## [2026.10.01.0825]

Son dört sürümde eklenen kodun baştan sona incelemesi. Altı hata düzeltildi.

### Düzeltildi — panel sunucusu, oturumunu kapatan kullanıcıyla birlikte kapanıyordu

En önemli bulgu. Panel sunucusu (`Print360.Dashboard.exe`) yalnızca web
paneli değildir; **HTTPS ile bağlanan istemcilerin iş aldığı `/api/jobs`** de
odur. Bugüne kadar `HKLM\...\Run` kaydıyla, sunucuda oturum açan **ilk
kullanıcının oturumunda** başlıyordu. Sonuçları:

- O kullanıcı oturumunu **kapattığında** panel de kapanıyordu. Başka biri
  yeni oturum açana kadar hiçbir HTTPS istemcisi çıktı alamıyor, kalp atışı
  gönderemiyordu; panelde işler "Gönderildi"de kalıyordu. Tipik senaryo:
  yönetici güne sunucuya bağlanarak başlar, akşam çıkış yapar.
- Her oturum açılışı yeni bir panel kopyası başlatmaya çalışıyordu. Yönetici
  oturumlarındaki kopyalar `localhost`'a bağlanıp **çalışmaya devam ediyor**,
  uyarıları ve günlük e-posta raporunu iki kez üretebiliyordu.

Artık panel **"Print360 Panel Sunucusu"** zamanlanmış göreviyle **SYSTEM**
olarak çalışıyor:
- sunucu açılışında başlar, kimsenin oturum açmasını beklemez,
- hiçbir kullanıcının oturumuna bağlı değildir,
- çökerse en geç **5 dakikada** geri gelir (bekçi tetikleyicisi),
- makine genelinde tek kopya çalışır (`Global\` kilidi).

Kurulum, görev kurulamaz ya da 15 saniye içinde başlamazsa **eski davranışa
döner** ve bunu uyarı olarak yazar; hiçbir durumda paneli çalışmaz bırakmaz.
Güncelleme sırasında görev durdurulur (kopyalama sırasında paneli yeniden
başlatıp dosyayı kilitlemesin), kaldırmada silinir.

### Düzeltildi — "aynı adlı iki makine" tespiti klonlanmış imajlarda çalışmıyordu

Tespit, her kurulumun diske yazdığı rastgele bir kimliğe dayanıyordu. Oysa
özelliğin hedefi klonlanmış imajlardır ve **klonlanan diskte kimlik dosyası da
aynen kopyalanır** — yani tespit tam da tasarlandığı durumda devre dışı
kalıyordu. Kimlik artık donanım kimliğiyle (SMBIOS UUID) birleştirilerek
üretiliyor: klon disk aynı, donanım farklı. Sunucuya ham UUID değil, yalnızca
özeti (SHA-256) gönderiliyor.

### Düzeltildi — tanı sayfası panel SYSTEM olarak çalışınca hep "BOŞ!" diyecekti

"Bu oturumun RDP istemci adı" kontrolü panelin **kendi** oturumuna
bakıyordu; bu zaten ne paneli açan kişinin ne de yazdıran kullanıcının
oturumuydu. Yerine sunucudaki **tüm kullanıcı oturumları** ve bağlı oldukları
makineler listeleniyor; istemci adı boş olan etkin oturum varsa uyarılıyor.

### Düzeltildi — `/api/surum` var olmayan bir hizmetten söz ediyordu

"Dosyalar yeni ama çalışan panel eski" durumunda "'Print360 Dashboard'
servisini yeniden başlatın" yazıyordu; böyle bir hizmet yok. Artık doğru
komutu veriyor (`schtasks /End` + `/Run`), eski kurulumlar için de yolu
belirtiyor.

### Düzeltildi — panel tabloları ve CSV dışa aktarımı büyük geçmişte yavaşlıyordu

Her satır için üç ayrı dosya kontrolü yapılıyordu (kuyruk, karantina, süresi
dolan). Yılların geçmişini içeren bir CSV raporunda bu on binlerce disk
erişimi demekti. Kuyruk artık tek taramada okunuyor ve 3 saniye tutuluyor;
kuyruğu değiştiren her işlem (karantina, süre, yeniden gönderme, taşıma)
önbelleği hemen bozuyor.

### Düzeltildi — sunucu günlüğü 5 dakikada bir aynı uyarıyı yazıyordu

`update\` klasöründeki istemci sürümü panelden farklıysa uyarı her önbellek
yenilemesinde (günde ~288 kez) tekrar yazılıyordu. Artık her fark bir kez.

### Test
9 test dosyası, 74 kontrol — hepsi üretim kodu üzerinde:
- `PanelGorevi` (yeni, 10): kurulumun ürettiği görev XML'i — SYSTEM, süre
  sınırı yok, tek kopya, açılış + 5 dk bekçi (tekrarın açılış tetikleyicisinde
  **olmadığı**, çünkü orada ancak bir sonraki açılışta başlardı). Ayrıca XML
  **Windows Görev Zamanlayıcı'ya gerçekten kaydettirilip** geri okunuyor ve
  siliniyor (yönetici gerektiren iki öğe kullanıcı eşdeğerleriyle değiştirilerek).
- `SurumRaporu` (+1): tanı sayfasının oturum listesi — `WTSEnumerateSessions`
  yapı hizalaması, bu sürecin kendi oturumunu doğru kullanıcıyla bulmasıyla sınanıyor.
- `KuyrukKorumalari` (+3): anlık görüntü önbelleği gerçekten çalışıyor ve
  karantina onu bozuyor.
- İstemci kimliği derlenmiş istemci üzerinde çalıştırıldı: iki ayrı süreçte
  aynı 32 haneli değer, ham UUID içermiyor.

Panel görevinin **SYSTEM olarak açılışta** çalışması bu sürümde canlı bir
sunucuda denenmedi (geliştirme makinesinde yönetici yetkisi yok). Bu yüzden
kurulum, görev başlamazsa eski yola dönecek şekilde yazıldı.

---

## [2026.09.30.0839]

Proje analizinde öne çıkan üç açığın kapatılması.

### Eklendi — uzaktan kurulum doğrulaması (`/api/surum`)

Sahadaki sunucu, "kuruldu" denmesine rağmen haftalarca eski sürümde kaldı ve
bunu uzaktan anlamanın yolu yoktu. Sunucu artık şu adreste kendi durumunu
raporluyor:

```
https://<sunucu>:8443/api/surum
```

```json
{"calisanPanel":"2026.09.30.0839",
 "disk":{"panel":"…","sunucuAjani":"…","masaustuPanel":"…","dagitilanIstemci":"…"},
 "sonKurulum":{"surum":"…","tarih":"…","sonuc":"OK","ayrinti":""},
 "tutarli":true,
 "not":"Tum bilesenler 2026.09.30.0839 surumunde."}
```

Rapor şu durumları ayrı ayrı teşhis eder:
- **dosyalar yeni ama çalışan panel eski** — servis yeniden başlatılmamış
  (en sinsi durum: kurulum başarılı görünür ama hiçbir şey değişmez),
- **bir bileşen eski kaldı** — hangisi olduğu yazılır,
- **son kurulum HATA ile bitti** — hata ayrıntısıyla,
- **kurulu dosyalar okunamıyor** — kurulum eksik ya da başka klasörde.

Kurulum yapılandırıcısı her kurulumun sonunda
`C:\Print360\logs\kurulum-sonuc.txt` dosyasına sürüm, tarih ve sonucu
(OK / UYARI / HATA) yazar. Rapor yalnızca sürüm ve tarih içerir; yol, makine
adı ve kullanıcı bilgisi içermez.

### Eklendi — Kuyruk sayfası (web paneli ve masaüstü paneli)

Karantinaya alınan ve süresi dolan işler eskiden yalnızca günlüğe
yazılıyordu; uyarı kaçırılırsa iş fiilen kayboluyordu. Yeni **Kuyruk**
sayfası:
- bekleyen işleri hedef makine ve durumla listeler,
- teslim edilemeyip kenara alınan işleri listeler (menüde kırmızı sayaçla),
- **Yeniden gönder** ile işi kuyruğa geri koyar. İşin zamanı sıfırlanır (bir
  dakika sonra yeniden "süresi dolmuş" sayılmaz), deneme sayacı sıfırlanır.

Tanı sayfası da kenara alınan işleri sayar ve Kuyruk sayfasına yönlendirir.

### Düzeltildi — kenara alınan işler panelde "Teslim edildi" görünüyordu

Karantinadaki ve süresi dolan işler kuyruk kökünde olmadığı için panel
onları istemcinin almış olduğunu sanıyordu. Artık `KARANTINADA` ve
`SURESI DOLDU` olarak gösteriliyor.

### Değişti — testler üretim kodunu sınıyor ve her yapımda çalışıyor

- Dört test (QsKodlama, EsZamanli, IstemciDegisimi, RoundTrip) sınanan
  mantığın **kopyasını** içeriyordu; üretim kodu değişse de geçmeye devam
  ederdi. Paylaşılan kod `server/Print360.Ortak.cs` dosyasına taşındı
  (Base64Url kimlik, UTF-8 sorgu çözümü, CSV alanı, kilitli günlük, oturum
  deposu); sunucu, panel ve istemci bu dosyayla derleniyor. Testler artık
  **üretim kaynağıyla birlikte derleniyor**.
- `tests/calistir.ps1`: tüm testleri derleyip çalıştıran tek betik.
  `build.ps1` paketlemeden önce çalıştırır — biri bile başarısızsa **paket
  üretilmez**. GitHub CI aynı betiği çalıştırır; yeşil CI artık yalnızca
  "derlendi" değil "testler geçti" demek.
- Doğrulandı: üretim kodundaki CSV koruması bilerek bozulduğunda çalıştırıcı
  hatayı yakaladı ve çıkış kodu 1 döndü.
- Panel oturum çerezine `SameSite=Strict` eklendi (başka siteden gönderilen
  bir form panelde işlem yapamaz).

### Test
8 test dosyası, 58 kontrol, hepsi üretim kodu üzerinde:

| Test | Kontrol | Sınadığı |
|---|---|---|
| RoundTrip | 6 | Kimlik/ad kodlama zinciri (ağsız) |
| QsKodlama | 5 | Aynı zincir, gerçek HttpListener üzerinde |
| EsZamanli | 6 | Kilitli günlük, günlük devri, oturum deposu, CSV |
| IstemciDegisimi | 7 | Kullanıcı değişiminde iş taşıma |
| KuyrukDurumu | 7 | Panel durum metinleri |
| KuyrukKorumalari | 15 | Karantina, süre sınırı, görünürlük, yeniden gönderme, yol geçişi |
| SurumRaporu | 7 | `/api/surum` teşhisleri, JSON kaçışı |
| PeMimari | 5 | DLL mimarisi ve RDP kayıt dalı |

Kuyruk sayfalarının görsel çıktısı (web HTML'i ve WPF ekranı) çalıştırılarak
denenmedi; altlarındaki mantık yukarıdaki testlerle sınandı.

---

## [2026.09.19.2001]

Baskı yolu uçtan uca (yazdır → yakala → kuyruk → istemci → yazıcı → onay)
senaryo senaryo tarandı. Bu sürüm, **henüz yaşanmamış** ama kodda karşılığı
olmayan altı senaryoyu kapatıyor.

### Eklendi — zehirli iş karantinası (genel sigorta)

Bir iş üst üste verilip hiç onaylanmıyorsa kuyruğun **başını tıkar** ve
arkasındaki bütün işler bekler: "ilk çıktı geliyor, devamı gelmiyor". Sahada
bu belirti bir ay sürdü ve sebebi her seferinde farklıydı (başlık kodlaması,
sorgu dizesi kodlaması...). Karantina, **sebebi henüz bilinmeyen** gelecekteki
hatalara karşı genel korumadır: iş en az **8 kez** verilmiş ve ilk denemeden
beri en az **3 dakika** geçmişse `queue\<makine>\_sorunlu\` klasörüne alınır,
kuyruk akmaya devam eder, yönetici uyarılır. Dosya silinmez.
Eşik iki koşulludur; yalnızca sayıya bakmak dengesiz bir ağda sağlam işi
haksız yere kenara alırdı.

### Eklendi — kuyruk süre sınırı

7 günden (`db.ini` → `KuyrukSaat=`) uzun bekleyen iş artık teslim edilmez;
`C:\Print360\queue-suresi-dolan\` klasörüne alınır ve uyarı üretilir. Haftalar
sonra yeniden bağlanan bir makinede eski — belki gizli — belgelerin birden
yazıcıdan çıkması hem şaşırtıcı hem risklidir. Dosya silinmez.

### Eklendi — aynı adla iki makine tespiti

Klonlanmış Windows imajları aynı bilgisayar adını taşır. İki makine aynı adla
bağlanırsa **aynı kuyruğu paylaşırlar** ve çıktı hangisi önce yoklarsa ona
gider. Her istemci kurulumu artık rastgele bir kimlik (`kimlik.txt`) taşıyor;
sunucu aynı adı üç dakika içinde A→B→A şeklinde iki farklı kimlikle görürse
`AYNI ADLA IKI MAKINE` uyarısı üretir. Tek yönlü değişim (yeniden kurulum)
uyarı üretmez.

### Eklendi — aynı kullanıcı, iki oturum uyarısı

Aynı hesap iki ayrı RDP oturumu açarsa iki ajan aynı spool dosyasını izler ve
çıktı rastgele birinin bilgisayarına gider. Bu yapısal bir sınırdır; artık en
azından sessiz değil: ikinci ajan durumu günlüğe ve panel uyarılarına yazar.

### Değişti — istemci şifresi reddedilince anlaşılır mesaj

Sunucu `403` döndürdüğünde istemci günlüğünde yalnızca "(403) Yasak"
görünüyordu. Artık sebep ve çözüm yazılıyor: makine sunucuda başka bir
şifreyle kayıtlı; `ClientKey` eşitlenmeli ya da paneldeki kayıt silinmeli.

### Düzeltildi — istemcide `failed` klasörü hiç temizlenmiyordu

Basılamayan her iş diskte süresiz kalıyordu. Son 100 dosya / 30 gün tutulur.

### Bilinen sınırlar (bu sürümde çözülmedi, bilinçli)
- Aynı kullanıcının iki eşzamanlı oturumu yalnızca **uyarılır**, ayrıştırılmaz.
- `jobs.csv` süresiz büyür; yıllar içinde panel açılışını yavaşlatabilir.
- Kendinden imzalı sertifikanın süresi dolduğunda yenisi üretilir; parmak izi
  sabitlemiş istemcilerde `CertHash` güncellenmelidir.

### Test
- `tests/KuyrukKorumalari.cs` — üretimdeki `Kuyruk` sınıfını doğrudan derler:
  dengesiz ağda yanlış karantina yok, onaylanmayan iş kenara alınıyor ve
  **arkadaki iş veriliyor**, onay sayacı sıfırlıyor, 18 günlük iş kuyruktan
  çıkıyor, taze işlere dokunulmuyor. **7/7**
- Aynı adlı makine tespiti, çift oturum uyarısı ve 403 mesajı derlendi;
  canlı ortamda ayrıca sınanmadı.

---

## [2026.09.19.1910]

Sahada yaşanan son sorunların ortak kalıpları çıkarıldı ve kod aynı kalıplar
için tarandı. Bu sürüm, **henüz patlamamış** beş örneği kapatıyor. Ortak
tema: *sorun var ama kimse görmüyor.*

### Değişti — "Gönderildi" artık üç ayrı durum

Eskiden "Gönderildi" yalnızca "iş kuyruğa yazıldı, basıldı onayı gelmedi"
demekti. İstemci işi **hiç almamış** olsa da, almış ama basamamış olsa da
aynı kelime görünüyordu. Bir iş yanlış makinenin kuyruğuna yazıldığında
saatlerce "Gönderildi" göründü. Artık (panel, web paneli ve CSV raporu):

| Durum | Anlamı |
|---|---|
| `Kuyrukta (2 dk)` | İş sunucuda bekliyor, hedef makine çevrimiçi — birazdan alınır |
| `BEKLIYOR (40 dk) - PC-ADI cevrimdisi, son: 19.09 16:02` | İş sunucuda bekliyor ve hedef makine **bağlı değil** |
| `BEKLIYOR (...) - PC-ADI sunucuya HIC baglanmadi` | Hedef makinede istemci kurulu değil ya da iş yanlış ada gitti |
| `Teslim edildi` | İstemci işi aldı; basıldı onayı henüz gelmedi |
| `Basıldı ✓` | Değişmedi |

### Eklendi — çevrimdışı hedefe gönderimde ve unutulan işlerde uyarı

- Sunucu ajanı bir işi kuyruğa yazarken hedef makinenin kalp atışına bakıyor.
  Makine çevrimdışıysa iş yine kuyruğa yazılır, ama günlüğe ve panel
  uyarılarına açıkça düşer: `HEDEF MAKINE CEVRIMDISI - 'PC-ADI' su an isi ALAMAZ`.
- Panel servisi dakikada bir kuyruğu tarıyor; **10 dakikadır alınmayan** her iş
  için bir kez uyarı üretiyor ve makinenin çevrimiçi mi çevrimdışı mı olduğunu
  belirtiyor.

### Eklendi — sürüm uyuşmazlığı artık görünür

İstemci sunucudan **yeni** ise eskiden sessizce geçiliyordu. Oysa
düzeltmelerin çoğu sunucu tarafındadır; "güncelledim ama düzelmedi"
durumunun en sık sebebi buydu ve hiçbir yerde görünmüyordu. İstemci günlüğü
artık şunu yazar (6 saatte bir):
`UYARI: SUNUCU ESKI SURUMDE - sunucu X, bu istemci Y. Sunucu tarafindaki duzeltmeler ETKIN DEGIL`

### Düzeltildi — otomatik güncelleme sonsuz döngüye girebiliyordu

`/api/clientversion` panelin **kendi** derleme sürümünü bildiriyordu,
dağıttığı dosyanınkini değil. Kurulum `update\` klasöründeki exe'yi
güncelleyemezse istemci her 30 dakikada bir "yeni sürüm" indirip kendini
yeniden başlatır, sürüm yine eski olur ve bu sonsuza kadar tekrarlanırdı.
- Sunucu artık dağıttığı dosyanın gerçek sürümünü bildiriyor; panel sürümünden
  farklıysa günlüğe uyarı yazıyor.
- İstemci, indirdiği dosyanın gerçekten daha yeni olduğunu doğrulamadan
  kendini değiştirmiyor (eski sunuculara karşı da korur).

### Düzeltildi — veritabanı motoru bir kez seçiliyordu

Sunucu yeniden başladığında panel servisi SQL Server'dan **önce** ayağa
kalkarsa MSSQL'e bağlanamıyor, SQLite'a düşüyor ve süreç kapanana kadar orada
kalıyordu: panelde geçmiş boş görünür, istemci anahtarları "kayıtsız" olur,
veri iki veritabanına bölünürdü. `db.ini`'de bir MSSQL sunucusu tanımlıysa
artık dakikada bir **arka planda** yeniden deneniyor (istekler bekletilmez);
bağlantı kurulunca geçiş yapılıp panele uyarı düşülüyor. Yalnızca MSSQL
kullanan kurulumları etkiler.

### Test
- `tests/KuyrukDurumu.cs` — üretimdeki `Kuyruk` sınıfını **doğrudan derleyerek**
  (kopyasını değil) sınar: çevrimdışı makinedeki iş, çevrimiçi makinedeki yeni
  iş, alınmış iş, hiç bağlanmamış makine, saatlik bekleme, makinenin geri
  gelmesi ve boş girdiler. **7/7**
- Güncelleme doğrulaması ve MSSQL yeniden deneme derlendi; canlı sunucuda
  ayrıca sınanmadı.

---

## [2026.09.19.1840]

### Düzeltildi — "Gönderildi" görünüyor ama çıktı alınmıyor (sunucu yeniden başlatıldıktan sonra)

Sunucu ajanı, RDP istemci makinesinin adını **yalnızca başlarken bir kez**
okuyor ve sonra hiç tazelemiyordu (yalnızca ad boşsa yeniden soruyordu).
Oysa RDP oturumu aynı kalıp **bağlanan makine değişebilir**:

1. Sunucu yeniden başlatılır; kullanıcı hesabıyla önce A makinesinden
   oturum açılır (örneğin yeniden başlatmayı yapan kişi tarafından).
   Ajan "A" adını ezberler.
2. Kullanıcı aynı oturuma kendi makinesi B'den yeniden bağlanır.
   Oturum ve içindeki ajan aynen devam eder.
3. Ajan işleri hâlâ **A'nın kuyruğuna** yazar. Panelde "Gönderildi" görünür,
   B'deki yazıcıdan hiçbir şey çıkmaz.

Artık istemci adı her döngü turunda **ve her işten hemen önce** Windows'a
(WTS API) yeniden soruluyor. Değişiklik günlüğe açıkça yazılıyor:

```
ISTEMCI DEGISTI: 'DESKTOP-A' -> 'EVOPC' (ayni oturuma baska makineden
baglanildi); ciktilar artik 'EVOPC' makinesine gidecek
```

Oturum kopukken WTS boş döner; o durumda son bilinen ad korunur.

### Eklendi — yanlış kuyrukta kalan işler kullanıcıyı takip eder

İstemci değişimi algılandığında, eski makinenin kuyruğunda bekleyen işler
yeni makinenin kuyruğuna taşınır. Güvenlik sınırları:
- yalnızca **o kullanıcının** işleri (adı aynı harflerle başlayan başka
  kullanıcılar dahil, başkasının işine dokunulmaz),
- yalnızca **son 24 saatin** işleri (günler önceki bir belge sürpriz
  şekilde yazıcıdan çıkmaz).

### Test
- `tests/IstemciDegisimi.cs` — gerçek dosyalarla: kullanıcının üç işi
  taşındı; `FS` ile başlayan `FSADMIN` kullanıcısının, başka kullanıcının ve
  24 saatten eski işin yerinde kaldığı doğrulandı. **6/6**

---

## [2026.09.10.1344]

Proje genelinde yapılan eşzamanlılık ve güvenlik incelemesinin sonuçları.

### Düzeltildi — günlük satırlarının yarısından fazlası kayboluyordu

`Log()` her iki tarafta da kilitsiz `File.AppendAllText` + `catch { }`
kullanıyordu. Sunucuda istekler `ThreadPool` üzerinde işleniyor, istemcide
**yedi ayrı thread** (RDP izleyici, kalp atışı, iş yoklama, güncelleme, iş
döngüsü, arayüz) aynı dosyaya yazıyor. Kilit olmadan eşzamanlı çağrılar
"dosya başka bir işlem tarafından kullanılıyor" hatası veriyor ve `catch { }`
bunu sessizce yutuyordu. Ölçüm:

```
7 thread x 200 satir
  beklenen : 1400
  yazilan  :  605
  KAYIP    :  795   (%56,8)
```

Bu, adım adım günlüğü tam da en gerekli olduğu anda — sistem yoğunken —
işe yaramaz hale getiriyordu. Artık tüm günlük ve istatistik yazımları tek
bir kilit üzerinden, kısa yeniden denemeyle yapılıyor. Ölçüm sonrası kayıp: **0**.

Aynı düzeltme bağlantı günlüğü (`connections.log`), istemci istatistik
dosyaları ve istemcideki `printed.csv` için de uygulandı.

### Düzeltildi — oturum sözlüğü kilitsizdi (sunucu kilitlenebilirdi)

Panel oturumları düz bir `Dictionary` içinde tutuluyor ve istek
thread'lerinden kilitsiz olarak okunup yazılıyordu. Dinleyici
`2026.08.22` sürümünden beri çok iş parçacıklı (yavaş bir istemciye yazım
tüm sunucuyu bloke etmesin diye); kilitsiz `Dictionary`'ye eşzamanlı yazma
iç yapısını bozar ve klasik sonucu **%100 CPU'da sonsuz döngüdür**.
Aynı anda giriş yapan iki kullanıcı sunucuyu yanıt veremez hale
getirebilirdi. Tüm erişimler tek kilit altına alındı.

`PingAll` önbelleği de düzeltildi: `Task.WaitAll` zaman aşımına uğradığında
geciken ping görevleri sözlüğe yazmaya devam ediyor, onu okuyan istek
thread'i "koleksiyon değiştirildi" hatası alabiliyordu. Artık kopyası
yayımlanıyor.

### Düzeltildi — CSV dışa aktarımında formül enjeksiyonu

Belge ve kullanıcı adları dışarıdan gelir. `=`, `+`, `-`, `@` ile başlayan
bir alan, rapor Excel'de açıldığında **formül olarak yorumlanıyordu**
(örn. `=cmd|'/c calc'!A1`). Bu alanlar artık başa tek tırnak eklenerek
metne çevriliyor; görüntüde tırnak görünmez.

### Test
- `tests/EsZamanli.cs` — üç düzeltme de ölçülerek doğrulandı: 7×200 satırlık
  günlükte kayıp yok, 3200 eşzamanlı oturum döngüsünde bozulma yok, formül
  başlangıçlı altı alan metne çevriliyor. **3/3**

---

## [2026.09.01.1254]

### Kökten çözüldü — "ilk yazdırma oluyor, devamı gelmiyor"

Bu sürüm, sahada aylardır süren belirtinin **kök sebebini** ortadan kaldırıyor.

**Ne oluyordu?** Bir baskı işinin adı, sunucudan istemciye ve onay ile geri
dönerken üç ayrı katmandan geçiyor ve **her katman kendi karakter kodlamasını**
uyguluyordu:

| Katman | Kodlama | Sonuç |
|---|---|---|
| HTTP yanıt başlığı | yalnızca ASCII taşır | `Yazdır` → `Yazd1r` |
| HTTP sorgu dizesi | `HttpListener` bunu **işletim sisteminin ANSI kod sayfasıyla** çözer (Türkçe sunucuda `windows-1254`) | `%C4%B1` → iki bozuk karakter |
| Disk üzerindeki ad | UTF-16 | doğru |

Adı taşıyan onay (ACK) sunucuya bozuk ulaşınca, sunucu **o adda bir dosya
bulamıyordu**. Bulamayınca "demek ki zaten silinmiş" diyerek istemciye `OK`
dönüyordu. İş kuyrukta duruyor, bir sonraki yoklamada **aynı iş yeniden
veriliyor** ve bu sonsuza kadar sürüyordu. Bu yüzden ilk çıktı alınıyor,
devamı hiç gelmiyordu.

**Çözüm.** Artık iş kimliği olarak **adın kendisi taşınmıyor.** Ad, UTF-8
baytlarının Base64Url karşılığı olarak gönderiliyor (`X-Job-Id`,
`X-File-Name-B64`). Kimlikte yalnızca `A-Z a-z 0-9 - _` bulunur; hiçbir kod
sayfası, vekil sunucu veya HTTP başlık kuralı onu değiştiremez. Yan kazanç
olarak `%`, `&`, `=` içeren belge adları da artık sorunsuz çalışıyor.

### Düzeltildi
- Sorgu dizesi artık ham URL'den **UTF-8** ile çözülüyor (`Q()`); işletim
  sisteminin ANSI kod sayfası devrede değil. Bu, Türkçe karakter içeren
  **makine adlarını** da düzeltir.
- Onaylanan iş kuyrukta bulunamıyor ama kuyrukta başka dosyalar duruyorsa,
  artık sessizce `OK` dönülmüyor: HTTP 500 ile bildiriliyor. Sorun gizlenmiyor.
- İstemcinin "sunucudaki klasörü ve izinlerini kontrol edin" uyarısı yanlış
  yere işaret ediyordu; sorun hiçbir zaman klasör izni değildi. Uyarı metni
  doğru yönlendirmeyle değiştirildi.

### Uyumluluk
- Eski sürüm istemciler için yüzde-kodlu yol korundu; karışık sürümlü
  kurulumlar çalışmaya devam eder. Yine de **her iki tarafı da** bu sürüme
  yükseltmek gerekir: düzeltmenin tamamı sunucu tarafındadır.

### Test
- `tests/QsKodlama.cs` — gerçek bir `HttpListener` ve gerçek dosyalar üzerinde
  tüm zincir (başlık → sorgu dizesi → disk → kuyruktan düşürme) doğrulandı.
  Sahada takılan işin gerçek adı, `Ş Ö Ğ Ç İ` içeren ad, `%`/`&`/`=` içeren ad
  ve geriye uyumluluk senaryosu dahil: **5/5**.

---

## [2026.08.29.1316]

### 🐞 Kurulum, üzerine kurulum ve kaldırma akışları düzeltildi

Üç akış baştan sona incelendi; dört gerçek kusur bulundu.

**1. Üzerine kurulum bekleyen işleri siliyordu — veri kaybı.** Temizlik adımı
`queue` ve `spool` klasörlerini boşaltıyordu. Bunlar kullanıcı verisidir:
`queue` = istemciye teslim edilmemiş baskı işleri, `spool` = yakalanmış ama
gönderilmemiş çıktılar. Sunucuyu güncelleyen yönetici, kullanıcıların bekleyen
çıktılarını farkında olmadan yok ediyordu. Artık yalnızca `update` boşaltılıyor
ve korunan iş sayısı günlüğe yazılıyor.

**2. Eski sürüm çalışmaya devam edebiliyordu.** Temizlik süreçleri durduruyor
ama otomatik başlatmayı kapatmıyordu; zamanlanmış görev ajanı dosyalar
kopyalanırken yeniden başlatabiliyor, exe kilitli kalıyor ve kopyalama
başarısız oluyordu — kurulum yine "tamamlandı" diyordu. Artık görev önce askıya
alınıyor, kurulum sonunda yeniden kuruluyor; kurulum yarıda kalırsa hata yolunda
tekrar etkinleştiriliyor.

**3. Kopyalama sessizce başarısız olabiliyordu.** Tek denemede pes edip yalnızca
uyarı yazıyordu. Artık dört kez deneniyor, kilitli süreç durduruluyor ve kopyanın
boyutu doğrulanıyor; yine olmazsa *"bu dosya eski sürümde kaldı"* diye açıkça
bildiriliyor.

**4. Kaldırmada bekleyen işler sessizce kalıyordu.** Kuyrukta teslim edilmemiş iş
varsa artık söyleniyor; dosyalar silinmiyor, tekrar kurulunca teslim ediliyor.

**Doğrulama:** kuyruğa ve spool'a bekleyen işler konup üzerine kurulum yapıldı —
`queue` ve `spool` korundu, `update` temizlendi, günlük *"Bekleyen 2 iş KORUNDU"*
yazdı ve kurulum tek satırla bitti.

---

## [2026.08.29.1257]

### ⚠️ Bazı makinelerde uzak masaüstü açılmıyordu

Bazı bilgisayarlarda **mstsc.exe hiç açılmıyordu.** Kullanıcının bunu Print360
ile ilişkilendirmesi mümkün değildi.

**Sebep:** kurulum, RDP sanal kanal eklentisini iki kayıt dalına birden
yazıyordu:

```
SOFTWARE\Microsoft\...\AddIns\Print360              (64-bit görünüm)
SOFTWARE\WOW6432Node\Microsoft\...\AddIns\Print360  (32-bit görünüm)
```

`Print360.VC.dll` ise **yalnızca x64**. `mstsc.exe`, bu anahtarda kayıtlı DLL'i
kendi mimarisinde yüklemeye çalışır; 32-bit mstsc 64-bit DLL'i yükleyemeyince
uzak masaüstü hiç açılmıyordu.

İkinci risk: kayıt varken dosya yoksa (yarım kaldırma, dosyanın silinmesi, virüs
tarayıcının karantinaya alması) aynı arıza oluşuyordu.

**Düzeltmeler:**
- Kurulum artık DLL'in gerçek mimarisini PE başlığından okuyup **yalnızca doğru
  dala** yazıyor. Önceki sürümlerin yanlış dala yazdığı kayıt varsa kurulum
  sırasında temizleniyor — o makinelerde RDP tekrar açılıyor.
- İstemci ajanı her açılışta kaydı doğruluyor: kayıt var ama dosya yoksa kaydı
  kaldırıp günlüğe `ONARIM` satırı yazıyor. Bozuk kalmış makineler kendiliğinden
  düzeliyor.

**Doğrulama:** `tests/PeMimari.cs` — PE mimari okuyucusu Windows'un kendi
ikilileriyle karşılaştırılarak test edildi (`System32
otepad.exe` → x64,
`SysWOW64
otepad.exe` → x86, `Print360.VC.dll` → x64) ve x64 DLL için seçilen
dalın `WOW6432Node` olmadığı denetlendi.

---

## [2026.08.22.1135]

### Eklenenler

- **Adım adım açıklamalı günlük.** Bir işin yolculuğu artık her iki tarafta da
  numaralı ve açıklamalı yazılıyor; "yazdırdım ama bir şey olmadı" dendiğinde
  hangi adıma kadar gelindiği tartışmasız görülüyor.

  ```
  Sunucu   [1/6] SPOOL        admin.pdf (512 KB)  -> kullanici sanal yaziciya yazdirdi
           [2/6] BELGE ADI    Teklif Formu | 3 sayfa -> olay gunlugunden okundu
           [3/6] HEDEF        DESKTOP-01          -> cikti bu makineye gonderilecek
           [4/6] KANAL        HTTPS-kuyruk        -> kuyruga yazildi
           [5/6] SPOOL SIL    admin.pdf           -> gecici cikti kaldirildi
           [6/6] TAMAM        ...  | HTTPS-kuyruk -> arsivlendi, sira istemcide

  İstemci  [1/7] IS BULUNDU   belge.pdf           -> kuyruktaki is alindi
           [2/7] INDIRILDI    184 KB -> 512 KB    -> sikistirilmis veri acildi
           [3/7] ONAYLANDI    belge.pdf           -> kuyruktan dusuruldu
           [4/7] HEDEF YAZICI Pantum M7100DW      -> oncelik sirasindan secildi
           [5/7] YAZDIRMA     belge.pdf           -> SumatraPDF ile basiliyor
           [6/7] BASILDI      belge.pdf           -> done klasorune tasindi
           [7/7] RAPORLANDI   Teklif Formu        -> panele islendi
  ```

  Her satır adımın **ne anlama geldiğini** de yazıyor; günlüğe bakan kişinin kodu
  bilmesi gerekmiyor. Bir adım başarısız olduğunda açıklama sebebi söylüyor
  (örn. *"ajan bir RDP oturumunda değil; iş bekletilecek"*).

  Kapatmak için sunucuda `db.ini`, istemcide `Print360.ini` içine
  `AyrintiliGunluk=0`. Günlükler zaten 5 MB'ta devrediyor.

---

## [2026.08.22.1124]

### 🐞 İş kimliği HTTP başlığında bozuluyordu — kök sebep bulundu

Belirti: ilk yazdırma oluyor, devamı gelmiyor. İstemci aynı işi durmadan
onaylıyor, kuyruk hiç ilerlemiyordu.

Sunucudan alınan başlık:

```
X-Job-Id : F:20260821_193128_109_Administrator~Belgeyi Yazd1r.pdf.gz
                                                       ^^^
```

Diskteki gerçek ad `...Belgeyi **Yazdır**.pdf.gz` — Türkçe `ı` harfi (U+0131).
HTTP başlıkları ASCII taşır; bu harf aktarımda `1` rakamına dönüşüyordu.

Zincir şöyle kırılıyordu:

1. Sunucu işi verir, başlıkta ad bozulur
2. İstemci indirir ve basar — **bu yüzden ilk yazdırma çalışıyordu**
3. İstemci onayı bozuk adla gönderir
4. Sunucu o adda dosya bulamaz, silemez
5. Aynı iş kuyruğun başında kalır → sonsuza kadar tekrar

Belge adında **boşluk, `%`, `&` veya `=`** olması da aynı şekilde kırıyordu.

**Düzeltme:** ad içeren başlıklar yüzde-kodlanıyor; değer artık ASCII. İstemci
kimliği aynen geri gönderiyor, sunucunun sorgu çözümlemesi orijinali veriyor.
`%` içermeyen değerler olduğu gibi bırakıldığı için sürüm karışımı da çalışır.

Ayrıca onaylanan iş kuyrukta **hiç bulunamazsa** artık uyarı yazılıyor ve
kuyruktaki gerçek adlar günlüğe dökülüyor. "Dosya yok" ile "sildim" aynı şey
değildir; öyle sayılması bu hatayı gizliyordu.

**Doğrulama:** `tests/RoundTrip.cs` — kimliğin sunucu → başlık → istemci → sorgu
dizesi → sunucu yolculuğu beş senaryoda test edildi (Türkçe harf, boşluk, `%`,
`&`, `=`). Hepsi geçti.

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
