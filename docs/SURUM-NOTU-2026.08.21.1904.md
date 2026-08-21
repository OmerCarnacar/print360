## Print360 2026.08.21.1904

Bu sürümde iki şey var: **işlerin istemciye inmemesi sorununun asıl sebebi** ve
**sürüm numarasının tarih tabanlı hale gelmesi.**

---

### 🐞 İşler istemciye inmiyordu — sebep bulundu

Belirti: sunucu kuyruğunda işler birikiyor, istemci "BAĞLI" görünüyor, yazıcı
"Hazır" — ama çıktı gelmiyor ya da yalnızca ilki geliyor.

Ölçüm sonucu:

| | |
|---|---|
| TCP el sıkışması (ağ) | **~25 ms** |
| İlk HTTPS isteği | **17–27 saniye** |
| Sonraki HTTPS istekleri | **~70 ms** |

Ağ hızlı, veri boyutu önemsiz. Maliyetin tamamı **TLS el sıkışmasında**:
kurulumun ürettiği kendinden imzalı sertifikanın zincir doğrulaması, ulaşılamayan
bir sertifika iptal listesini sorguluyor ve Windows zaman aşımını bekliyor. Bedel
bir kez ödendikten sonra sonuç önbelleğe giriyor.

Bir önceki sürümde keep-alive kapatılmıştı. Bu, farklı bir sorunu (sunucunun
boşta kalan bağlantıyı kapatması) çözüyordu ama **her isteği yeni bağlantıya
zorladı** — yani her yoklamada 20+ saniyelik el sıkışma bedeli. İstemcinin zaman
aşımı 20 saniyeydi; istekler tam sınırda takıldı.

**Düzeltme:**
- Keep-alive geri açıldı — pahalı el sıkışma bir kez ödeniyor
- Zaman aşımı 20 → **60 saniye**
- Keep-alive'ın bilinen riski için **tek seferlik yeniden deneme**

> **İpucu:** Sunucuya iç ağdan ya da VPN üzerinden erişiyorsanız, istemciyi HTTPS
> yerine HTTP portuna (8360) yönlendirmek bu TLS bedelini tamamen ortadan
> kaldırır. `C:\Print360\Print360.ini` içinde `UseHttps=0` yapmanız yeterli.
> İnternet üzerinden gidiyorsanız HTTPS'te kalın.

---

### 🔢 Sürüm numarası artık üretim tarihi

Yeni biçim: **`YIL.AY.GÜN.SAATDK`** — örnek `2026.08.21.1904`.

Elle artan `1.1.58` gibi bir numara, bir kurulumun ne zaman üretildiğini
söylemiyordu. Sahada bu defalarca soruna yol açtı: "güncelledim" denip eski
paket kuruldu, hangi paketin daha yeni olduğu anlaşılamadı. Tarih tabanlı
sürümde bakışla belli oluyor.

**Sürüm artık her alanda aynı.** Kurulum sihirbazlarının sürüm alanı derlemeden
geliyor (önceden `.iss` dosyalarında elle `1.1` yazıyordu). Derlemenin sonunda
beş bileşen ve iki kurulum dosyasının aynı numarayı taşıdığı denetleniyor; sapan
varsa yapım durduruluyor.

> Sunucu ve istemci **aynı numarayı taşımalıdır**. Farklı görüyorsanız
> taraflardan biri güncellenmemiş demektir — panelin **Tanı** sayfası bileşen
> sürümlerini tek tek listeler.

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
