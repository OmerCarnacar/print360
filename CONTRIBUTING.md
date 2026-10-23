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
lisans ([GPL-3.0](LICENSE)) buna izin verir. Koşulları: dağıttığınız sürümün
kaynak kodunu da GPL-3.0 ile sunmak ve telif bildirimlerini korumak.
**"Print360" adı ve logosu lisans kapsamında değildir** — çatalınızı farklı bir
adla dağıtın (bkz. [NOTICE](NOTICE)).

Gönderdiğiniz katkılar projenin lisansıyla (GPL-3.0-or-later) yayınlanır.

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
tests/     Regresyon testleri + çalıştırıcı (tests/calistir.ps1)
assets/    Logo üreteci
docs/      Belgeler ve ekran görüntüleri
```

## Testler

```powershell
powershell -ExecutionPolicy Bypass -File tests\calistir.ps1
```

Her test **üretim kaynağıyla birlikte derlenir** (kopyasıyla değil); test
geçiyorsa üretimdeki kod gerçekten doğru çalışıyor demektir. `build.ps1`
testleri paketlemeden önce çalıştırır ve biri bile başarısızsa paket
üretmez; GitHub CI da aynı betiği çalıştırır.

Yeni bir test yazarken sınanan mantığı test dosyasına **kopyalamayın**:
paylaşılan kod `server/Print360.Ortak.cs` veya `server/Print360.Db.cs`
içindeyse onu derleyin, bir bileşenin içindeyse `internal` yapıp o dosyayı
derleyin. Ardından testi `tests/calistir.ps1` listesine ekleyin.

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

Sorun yazdırmayla ilgiliyse **tanı paketlerini** ekleyin. Paketler belge
içeriklerini ve parolaları içermez.

1. **Hata ayıklama modunu açın:** istemcide tepsi simgesi > *Hata ayıklama
   modu*, sunucuda panel > **Tanı** > *Hata ayıklama modunu aç*. Mod her adımı
   ayrıntılı yazar ve 24 saat sonra kendiliğinden kapanır.
2. **Sorunu tekrarlayın** (yazdırmayı yeniden deneyin).
3. **Paketleri oluşturun:**
   - İstemci: tepsi simgesi > *Tanı paketi oluştur* (masaüstüne zip) ya da
     `C:\Print360\Print360.ClientAgent.exe --tani`
   - Sunucu: panel > **Tanı** > *Sunucu tanı paketini indir*

İstemci raporunun başındaki **BULGULAR** bölümü, bulunan sorunları
(yanlış hesapla çalışan ajan, çevrimdışı yazıcı, yoklanmayan RDP sunucusu,
kapalı port, eski sunucu sürümü…) kendiliğinden listeler.
