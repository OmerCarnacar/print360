# Katkıda Bulunma

Katkılarınız memnuniyetle karşılanır.

## Önce şunu bilin: `main` dalına doğrudan gönderim yapılamaz

Bu deponun `main` dalı **korumalıdır** ve yalnızca depo sahibi tarafından
yönetilir. Katkı vermek için **çatallamanız (fork)** gerekir — başka bir yol
yoktur ve bu bilinçli bir tercihtir.

```bash
# 1) GitHub'da sağ üstten "Fork" düğmesine basın, sonra:
git clone https://github.com/<kullanici-adiniz>/print360.git
cd print360

# 2) Kendi dalınızda çalışın (fork'unuzun main'inde değil)
git switch -c duzeltme/yazici-secimi

# 3) Değişikliği yapıp kendi fork'unuza gönderin
git push origin duzeltme/yazici-secimi

# 4) GitHub kendiliğinden "Compare & pull request" önerecek;
#    hedef: OmerCarnacar/print360  ->  main
```

Küçük düzeltmeler için doğrudan PR açabilirsiniz. Büyük değişikliklerden önce
bir **Issue** açıp konuşalım — emeğiniz boşa gitmesin.

**Fork'unuz sizindir.** İsterseniz kendi sürümünüzü geliştirip dağıtabilirsiniz;
lisans buna izin verir (satmamak ve geliştirici bilgisini korumak kaydıyla —
bkz. [LICENSE](LICENSE)).

## Geliştirme ortamı

| Gereken | Neden |
|---|---|
| Windows 10/11 veya Windows Server | Yazıcı ve RDP API'leri |
| .NET Framework 4.x SDK (`csc.exe`) | Derleme |
| [Inno Setup 6](https://jrsoftware.org/isdl.php) | Kurulum paketleri |
| MSVC + Windows SDK _(isteğe bağlı)_ | RDP Virtual Channel eklentisi (C++) |

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.1
```

## Proje yapısı

```
server/    Sunucu ajanı, web paneli, WPF paneli, veri katmanı
client/    İstemci ajanı (durum penceresi + baskı motoru)
setup/     Native yapılandırıcı (C#) + Inno Setup betikleri
vc/        RDP Virtual Channel eklentisi (C++) + protokol testi
assets/    Logo üreteci
docs/      Belgeler ve ekran görüntüleri
```

## Kod tarzı

- Yorumlar ve kullanıcıya görünen metinler **Türkçe**
- Mevcut dosyanın biçimine uyun (girinti, adlandırma, yorum yoğunluğu)
- Harici bağımlılık **eklemeyin** — projenin temel ilkesi budur
- Değişikliği mümkünse gerçekten çalıştırarak doğrulayın

## Commit mesajları

```
Kisa ozet - vX.Y.Z

Neyin neden degistigi. Bir hata duzeltiliyorsa SEBEBI yazin.
Dogrulama: nasil test edildi.
```

## Hata bildirimi

Sorun yazdırmayla ilgiliyse panelde **Tanı** sayfasının (`/tani`) ekran
görüntüsünü ve `C:\Print360\logs\` altındaki ilgili günlüğü ekleyin.
