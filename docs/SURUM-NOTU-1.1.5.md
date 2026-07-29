## Print360 v1.1.5

**Belge ve depo politikası sürümü.** Yazılımda değişiklik yoktur — kurulum
paketi v1.1.4 ile birebir aynıdır. v1.1.4 kullanıyorsanız güncellemeniz
gerekmez.

---

### 🔒 `main` dalı korumaya alındı

Bu deponun `main` dalı artık korumalıdır ve yalnızca depo sahibi tarafından
yönetilir:

- `main`'e **doğrudan gönderim kapalı** — değişiklikler pull request ile gelir
- **Zorla gönderim (force push) engellendi** — geçmiş silinemez
- **Dal silme engellendi**
- PR'da yanıtlanmamış yorum varken birleştirme engellendi

### 🍴 Katkı vermek için çatallayın (fork)

Katkılara açığım. Yol şu:

```bash
# 1) GitHub'da "Fork" düğmesine basın, sonra:
git clone https://github.com/<kullanici-adiniz>/print360.git
cd print360

# 2) Kendi dalınızda çalışın
git switch -c duzeltme/yazici-secimi

# 3) Fork'unuza gönderin
git push origin duzeltme/yazici-secimi

# 4) OmerCarnacar/print360 -> main hedefine pull request açın
```

Büyük değişikliklerden önce bir **Issue** açıp konuşalım — emeğiniz boşa
gitmesin.

**Fork'unuz sizindir.** Lisans koşulları içinde (satmamak ve geliştirici
bilgisini korumak kaydıyla) kendi sürümünüzü geliştirip dağıtabilirsiniz.

Ayrıntı: [CONTRIBUTING.md](https://github.com/OmerCarnacar/print360/blob/main/CONTRIBUTING.md)

---

### 📌 Lisans

Print360 **açık kaynak değildir.** Kaynağı açıktır ve incelenebilir; satışı
yasak olduğu için OSI tanımını karşılamaz — MIT/GPL/Apache değildir.

Ücretsiz kullanım her ortamda serbesttir. Yazılım **"olduğu gibi"** sunulur;
**geliştirici hiçbir sorumluluk üstlenmez ve hiçbir garanti vermez.** Üretim
ortamına almadan önce kendi ortamınızda test edin.

Tam metin: [LICENSE](https://github.com/OmerCarnacar/print360/blob/main/LICENSE)

---

### 📦 Kurulum

1. Aşağıdaki ZIP dosyasını indirin.
2. **Sunucuda** `Print360-Server-Setup.exe` — yönetici olarak çalıştırın.
3. **Kullanıcı bilgisayarlarında** `Print360-Client-Setup.exe`.

Kurulum dosyaları kod imzalı değildir; SmartScreen uyarı verebilir
(**Daha fazla bilgi → Yine de çalıştır**).

---

**Ömer ÇARNAÇAR** — Geliştirici
[omer.carnacar@outlook.com.tr](mailto:omer.carnacar@outlook.com.tr) ·
[LinkedIn](https://www.linkedin.com/in/omercarnacar/)
