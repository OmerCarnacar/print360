# Değişiklik Günlüğü

Bu proje [Semantic Versioning](https://semver.org/lang/tr/) benzeri bir
`ANA.ALT.YAPIM` şeması kullanır. Yapım numarası her derlemede artar ve
paket adında üretim tarihi de yer alır (`v1.1.49-2707-2141`).

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
- RDP Virtual Channel (TSPrint mantığı) — IP/port/firewall gerekmez
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
