## Print360 v1.1.6

**Güvenlik düzeltmesi içerir — güncellenmesi önerilir.**

---

### 🔒 Parolasız panel artık uzaktan açılamıyor

Panel erişim parolası tanımlanmamış kurulumlarda (ne `PanelUsers` tablosu ne
`panel.pwd` varsa) yönetim paneli **kimlik doğrulaması olmadan** servis
ediliyordu. Panel tüm ağ arayüzlerinden dinlediği için, adresi bilen herkes
şunları parolasız okuyabiliyordu:

- Baskı geçmişi: kullanıcı adları, **belge adları**, sayfa sayıları
- Tanı sayfası ve kurulum günlükleri
- Arşivlenmiş çıktılar (PDF indirme)

Belge adları çoğu zaman içeriği ele verir (`Ocak_Bordro.pdf` gibi), dolayısıyla
bu ciddi bir bilgi sızıntısıydı.

**Artık:** panel korumasızken yalnızca sunucunun kendisinden açılabiliyor.
Uzaktan gelen istek, sebebini ve çözümünü anlatan bir sayfayla reddediliyor ve
güvenlik günlüğüne kaynak IP'siyle yazılıyor.

Yönetici kilitlenmez — sunucu üzerinden panel çalışmaya devam eder. Uzaktan
yönetmek için kurulumu tekrar çalıştırıp **panel erişim parolasını** tanımlamak
yeterlidir.

> İstemci ajanlarının `/api/*` uç noktaları bu kontrolden önce karşılanır;
> yazdırma işleyişi etkilenmez.

**Etkilenen kurulumlar:** panel parolası boş bırakılarak kurulmuş tüm sunucular.
Kontrol için sunucuda `C:\Print360\panel.pwd` dosyasının var olup olmadığına
bakabilirsiniz.

---

### 🐞 Günlük dosyaları süresiz büyüyordu

Günlükler için ne boyut sınırı ne devretme vardı. Arşiv için 90 günlük temizlik
bulunuyordu ama günlükler hiç temizlenmiyordu; yoğun bir sunucuda aylar içinde
yüzlerce megabayta çıkıp diski doldurabilirdi.

Üç bileşene de 5 MB sınırı ve tek kuşaklık devretme eklendi: dosya sınırı
geçince `.1` uzantısıyla devrediyor, yeni günlük sıfırdan başlıyor.

---

### 🧹 Ölü lisans kodu kaldırıldı

Sunucu ajanında deneme sürümü engeli duruyordu: iş sayısını sayıp limiti aşarsa
spool dosyasını siliyor ve "ENGEL" kaydı düşüyordu. Sınır sonsuz ayarlandığı
için hiç çalışmıyordu, ancak kod, RSA doğrulama altyapısı ve `license.key`
okuma mantığı yerinde duruyordu.

Kaynak kodu herkese açık olduğu için, koda bakan biri "çıktı limiti var"
izlenimi ediniyordu — ücretsiz bir üründe tam ters mesaj. Tamamen kaldırıldı:
sunucu ajanından 24, panelden 40 satır; `Print360.License.cs` 84 satırdan 29'a
indi ve yalnızca ürün kimliği kaldı.

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
