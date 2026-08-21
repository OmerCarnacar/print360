## Print360 v1.1.7

**Kurulum artık sessizce başarısız olmuyor.**

---

### 🔍 Kurulum hiçbir şey yapmadığında uyarıyor

Kurulum sihirbazı iki iş yapar: dosyaları kopyalar ve ardından yapılandırıcıyı
çalıştırır. Asıl işi ikincisi yapar — sanal yazıcıları oluşturur, ajanları
başlatır, panel portlarını hazırlar.

Yapılandırıcı herhangi bir sebeple çalışmazsa sihirbaz yine de **"kurulum
tamamlandı"** diyordu, çünkü dosyaları kopyalamıştı. Sistemde ise hiçbir şey
değişmiyordu: sürüm aynı kalıyor, sanal yazıcılar oluşmuyor, kurulum günlüğüne
tek satır düşmüyordu. Kullanıcının bunu fark etmesinin tek yolu günlüğe bakıp
tarih kontrol etmekti.

**Artık:** sihirbaz her çalışmada benzersiz bir işaret üretip yapılandırıcıya
geçiriyor. Yapılandırıcı işini bitirince bu işareti
`C:\Print360\logs\son-kurulum.txt` dosyasına yazıyor. Sihirbaz kurulum sonunda
dosyayı okuyup işareti arıyor; bulamazsa şu uyarıyı gösteriyor:

> Dosyalar kopyalandı, ANCAK yapılandırma adımı çalışmadı.
> Bu yüzden sanal yazıcılar oluşturulmamış, ajanlar başlatılmamış ve panel
> güncellenmemiş olabilir.
>
> 1. Kurulumu KAPATIN.
> 2. Kurulum dosyasına SAĞ TIKLAYIP "Yönetici olarak çalıştır" seçin.
> 3. Sihirbazı sonuna kadar tamamlayın.

Hem sunucu hem istemci kurulumunda geçerli.

---

### 🐞 Kurulum günlüğündeki iki yanıltıcı uyarı düzeltildi

**`netsh urlacl` — Error 183.** Port ayrımı zaten varsa `netsh` 183
(`ERROR_ALREADY_EXISTS`) döndürür. Bu bir hata değil; istenen sonuç zaten
sağlanmış demektir. Ama günlüğe kırmızı görünen bir satır olarak düşüyor ve
kullanıcıyı endişelendiriyordu. Artık ayrım önce siliniyor, sonra ekleniyor.

**`icacls` zaman aşımı.** 30 saniyelik sınır, büyük bir `C:\Print360` ağacında
yetmiyordu ve her kurulumda "adım zaman aşımına uğradı" uyarısı düşüyordu. Süre
60 saniyeye çıkarıldı.

Bu ikisi düzeldiği için, doğru yapılan bir kurulumun günlüğü artık tek satırla
bitiyor: **"Kurulum sorunsuz tamamlandi."** Uyarı görüyorsanız gerçekten bir
sorun var demektir.

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
