# Katkıda Bulunma

Katkılarınız memnuniyetle karşılanır. Küçük düzeltmeler için doğrudan PR
açabilirsiniz; büyük değişikliklerden önce bir **Issue** açıp konuşalım.

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
