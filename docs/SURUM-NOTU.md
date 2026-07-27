# v1.1 — İlk açık sürüm

> Bu metni GitHub'da **Releases → Draft a new release** ekranına yapıştırabilirsiniz.

RDP / Terminal Server oturumlarından yerel yazıcılara **sürücüsüz** yazdırma.
Sunucuya hiçbir yazıcı sürücüsü kurulmaz; baskıyı, o yazıcıyı zaten tanıyan
kullanıcı bilgisayarı yapar.

## Öne çıkanlar

- **Sıfır ayar** — istemci, açık RDP oturumundan sunucuyu kendisi bulur
- **Veritabanı zorunlu değil** — MSSQL yoksa SQLite, o da yoksa CSV
- **Harici bağımlılık yok** — yalnızca .NET Framework 4.x
- **Kişiye özel yazıcı sırası** — 1. yazıcı kapalıysa yedeğe düşer
- **Çoklu RDP** — aynı anda birden fazla sunucudan iş alır
- **Tanı sayfası** — "yazdırdım ama gelmedi" sorununu 7 adımda gösterir

## Kurulum

1. Aşağıdaki ZIP'i indirin
2. **Sunucuda** `Print360-Server-Setup.exe` (yönetici olarak)
3. **Kullanıcı bilgisayarlarında** `Print360-Client-Setup.exe`
4. Sunucuda `Print360 - <kullanıcı>` yazıcısına yazdırın

## Gereksinimler

| | |
|---|---|
| Sunucu | Windows Server 2016 / 2019 / 2022 / 2025 |
| İstemci | Windows 10 / 11 |
| Çalışma zamanı | .NET Framework 4.x |

## Bilinen sınırlama

RDP Virtual Channel eklentisi gerçek bir RDS ortamında uçtan uca
doğrulanmayı bekliyor. Kanal açılmazsa sistem otomatik olarak HTTPS
kuyruğuna düşer — yazdırma her durumda çalışır.

---

**Lisans:** Ücretsiz — sınırsız kullanım, **para ile satılamaz**.
