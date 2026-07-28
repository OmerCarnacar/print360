## Print360 v1.1.1

**Lisans ve hukuki metin sürümü.** Yazılımın çalışmasında değişiklik yoktur —
v1.1 kullanıyorsanız güncellemek zorunda değilsiniz.

---

### ⚠️ Lisans türü netleştirildi

Print360 **açık kaynak (open source) değildir.**

Kaynak kodu herkese açıktır ve incelenebilir; ancak **satışı yasak** olduğu için
lisans, Açık Kaynak Girişimi'nin (OSI) tanımını karşılamaz. MIT, GPL veya Apache
lisansı **değildir**.

Doğru tanım: **kaynağı açık, ücretsiz, tescilli (source-available proprietary).**

| | |
|---|---|
| ✅ Serbest | Kişisel, kurumsal ve ticari ortamlarda ücretsiz kullanım · bedelsiz kopyalama ve dağıtım · kaynağı inceleyip kendi ihtiyacınıza göre değiştirme |
| ❌ Yasak | Para karşılığı satmak · kiralamak · abonelikle sunmak · ücretli bir ürünün parçası olarak vermek · geliştirici bilgisini kaldırmak |

> Yazılımı kuran bir hizmet sağlayıcının **kendi emeği** için ücret alması bu
> yasağın dışındadır. Yasak olan, yazılımın kendisinin bedel karşılığı
> devredilmesidir.

---

### 🛡️ Sorumluluk reddi

Bu yazılım **hiçbir bedel alınmadan**, **"olduğu gibi"** sunulmaktadır.
**Geliştirici hiçbir sorumluluk üstlenmez ve hiçbir garanti vermez.**

Yazılımın kullanılmasından, kullanılamamasından veya hatalı çalışmasından
doğabilecek hiçbir zarardan geliştirici sorumlu tutulamaz — veri ve belge kaybı,
çıktının yanlış yazıcıya gitmesi ve bundan doğan gizlilik ihlali, iş kesintisi,
kâr kaybı, sarf malzemesi maliyeti ve üçüncü kişilerin zararları dahil.

Geliştiricinin **destek, bakım veya güncelleme sağlama yükümlülüğü yoktur.**

**Kurmadan önce:** üretim ortamına almadan mutlaka kendi ortamınızda test edin ve
sistem yedeği alın. Kurulum; yazıcı, kayıt defteri ve zamanlanmış görev ayarlarını
değiştirir.

**Kişisel veriler (KVKK / GDPR):** Print360 kullanıcı adı, bilgisayar adı, belge
adı ve sayfa sayısı kaydeder. Bu kayıtlar bakımından **veri sorumlusu yazılımı
kuran kurumdur**, geliştirici değildir. Yazılım dışarıya hiçbir veri göndermez.

Tam metin: [LICENSE](https://github.com/OmerCarnacar/print360/blob/main/LICENSE)

---

### 🐞 Düzeltilenler

- **Kurulum sihirbazının lisans sayfasında Türkçe karakterler bozuk görünüyordu.**
  Inno Setup, BOM'suz düz metni seçili dilin ANSI kod sayfasıyla okuyor; UTF-8
  olan lisans dosyası bu yüzden okunaksız çıkıyordu. Derleme artık sihirbaz için
  BOM'lu bir kopya üretiyor. Kullanıcının kabul ettiği metin artık okunabilir.
- **GitHub Actions derlemesi başarısız oluyordu** (`CS1567: Error generating Win32
  resource`). `csc.exe` varsayılan Win32 kaynağını çıktı klasöründe geçici dosyaya
  yazıyor, ancak klasör derleme öncesi oluşturulmuyordu.

---

### 📦 Kurulum

1. Aşağıdaki ZIP dosyasını indirin.
2. **Sunucuda** `Print360-Server-Setup.exe` — yönetici olarak çalıştırın.
3. **Kullanıcı bilgisayarlarında** `Print360-Client-Setup.exe`.
4. RDP oturumunuzda `Print360 - <kullanıcı>` yazıcısına yazdırın.

Kurulum dosyaları kod imzalı değildir; Windows SmartScreen uyarı verebilir
(**Daha fazla bilgi → Yine de çalıştır**).

Ayrıntılı belgeler: [README](https://github.com/OmerCarnacar/print360#readme) ·
Sorun giderme: panelde **Tanı** sayfası (`/tani`)

---

**Ömer ÇARNAÇAR** — Geliştirici
[omer.carnacar@outlook.com.tr](mailto:omer.carnacar@outlook.com.tr) ·
[LinkedIn](https://www.linkedin.com/in/omercarnacar/)
