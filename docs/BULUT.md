# Print360 — Bulut RDP Kurulum Kılavuzu

Sunucunuz bir **bulut sanal makinesinde** (Azure VM, AWS EC2, VPS...) ve
kullanıcı bilgisayarları farklı yerlerden (ev, ofis) internet üzerinden
bağlanıyorsa bu kılavuzu izleyin.

## Neden sorunsuz çalışır?

Print360'ta iş aktarımı **istemci → sunucu** yönündedir: istemci ajanı sunucuyu
periyodik yoklar (pull), sunucu istemciye bağlanmaz. Bu yüzden:

- İstemciler NAT / güvenlik duvarı arkasında olabilir (ev/ofis) — yalnızca
  **giden** HTTPS bağlantısı kurarlar.
- Sunucunun tek yapması gereken **public erişilebilir** olmak ve **8443**
  portunu dinlemek.

```
[Ev/Ofis PC — dinamik IP, NAT]         [Bulut VM — public IP/domain]
Print360 istemci ajanı                  Print360 sunucu + panel
      └──── HTTPS :8443 (giden) ────────────► (istemci şifresi + TLS)
```

---

## Adım 1 — Bulut VM hazırlığı

- [ ] **Windows Server** çalışan bir VM (2016/2019/2022/2025), yönetici erişimi
- [ ] VM'e **statik public IP** veya bir **DNS adı** (ör. `print360.firmam.com`)
- [ ] RDP (3389) zaten açıktır; kurulumu buradan yaparsınız
- [ ] Tercihen MSSQL (Express de olur) VM'de veya erişilebilir

## Adım 2 — Print360 sunucu kurulumu

- [ ] `Print360-Server-Setup.exe`'yi VM'e kopyalayıp **yönetici olarak** çalıştırın
- [ ] Sihirbazda **portları not edin** (varsayılan 8360/8443; 8443'ü kullanacağız)
- [ ] Kurulum bitince sunucudaki **sertifika parmak izini** kaydedin:
      `C:\Print360\cert-thumbprint.txt` — istemcilerde sabitleme için gerekli
- [ ] Panel açılıyor mu: VM'de `https://localhost:8443`

## Adım 3 — Bulut güvenlik duvarında SADECE 8443'ü aç

Bu en kritik adımdır. **Yalnızca HTTPS (8443)** girişe açılmalı; HTTP (8360)
internete kapalı kalmalı.

**Azure (Network Security Group):**
- Inbound rule → Port `8443`, Protocol `TCP`, Source `Any` (veya bilinen IP
  aralıkları), Action `Allow`.
- `8360` için **kural EKLEMEYİN** (kapalı kalsın).

**AWS (Security Group):**
- Inbound → Type `Custom TCP`, Port `8443`, Source `0.0.0.0/0` (veya kısıtlı).

**VPS / Windows Firewall:**
- Sunucu kurulumu Windows Firewall'da 8443'ü zaten açar. VPS panelinde de
  (Hetzner/DigitalOcean vb. dış firewall) 8443 TCP giriş açılmalı.

> İpucu: Mümkünse Source'u ofis/bilinen IP aralıklarıyla sınırlayın. Ev
> kullanıcıları dinamik IP'liyse `Any` bırakıp güvenliği istemci şifresi +
> sertifika sabitleme ile sağlayın.

## Adım 4 — Güvenlik sağlamlaştırma (internet için zorunlu)

- [ ] **İstemci şifresi (ClientKey) zorunlu** — internete açık sunucuda mutlaka kullanın
- [ ] **Sertifika sabitleme (pinning):** İstemcilerde `CertHash` = sunucudaki
      parmak izi. Araya girmeyi (MITM) engeller.
- [ ] **Panel şifresi güçlü** olsun (panel internetten erişilebilir olacak)
- [ ] **HTTP (8360) internete kapalı** (Adım 3)
- [ ] (Önerilen) Gerçek domaininiz varsa self-signed yerine **Let's Encrypt**
      ücretsiz sertifikası kullanın — tarayıcı uyarısı kalkar, istemcilerde
      pinning'e gerek kalmaz. Aşağıdaki bölüme bakın.

## Let's Encrypt (ücretsiz güvenilir sertifika)

Sunucunuzun gerçek bir domaini varsa (ör. `print360.firmam.com` → sunucunun
public IP'sine DNS A kaydı), kurulumla gelen script tek komutla ücretsiz
Let's Encrypt sertifikası alıp panel portuna bağlar ve 90 günde bir otomatik
yeniler:

```powershell
# Sunucuda (yönetici PowerShell), Print360 kurulum klasöründe:
.\Print360-LetsEncrypt.ps1 -Domain print360.firmam.com -Email siz@firma.com
# Özel port: -HttpsPort 9443
```

Script `win-acme`'yi indirir, HTTP-01 doğrulamasını kendi geçici sunucusuyla
yapar, sertifikayı Windows deposuna kurar, 8443'e bağlar ve otomatik yenileme
görevini kurar (yenilemede porta yeniden bağlanır).

**Ön koşullar:**
- Domain, sunucunun public IP'sine yönlenmiş olmalı (DNS A kaydı)
- Doğrulama sırasında **80 (HTTP) portu geçici açık** olmalı (bulut NSG/SG'de);
  doğrulama bitince kapatabilirsiniz. 80'i tutan bir uygulama varsa geçici durdurun.

**Önemli:** Let's Encrypt kullanınca istemcilerde **sertifika sabitlemeyi
(CertHash) KULLANMAYIN** — sertifika 90 günde bir yenilenir ve parmak izi
değişir. İstemci `Print360.ini`'de `CertHash=` satırını **boş** bırakın
(güvenilir CA olduğu için pinning'e gerek yoktur).

## Adım 5 — İstemci kurulumu (kullanıcı bilgisayarları)

- [ ] `Print360-Client-Setup.exe`'yi kullanıcı PC'sine **yönetici olarak** kurun
- [ ] Sihirbazda:
  - **Sunucu adı/IP:** bulut VM'in **public adresi** (ör. `print360.firmam.com`
    veya `52.x.x.x`)
  - **İstemci şifresi:** sunucuyla aynı ClientKey
  - **Port:** varsayılan dışıysa yazın (8443 için boş bırakılabilir)
  - **Sertifika parmak izi:** sunucudaki `cert-thumbprint.txt` içeriği (pinning)
- [ ] RDP'de kullanıcı yazdırınca lokal PC'de yazıcı penceresi açılır → çıktı çıkar

## Bağlantı testi

İstemci PC'de tarayıcıdan:
```
https://<public-adres>:8443
```
Panel giriş ekranı geliyorsa istemci sunucuya ulaşabiliyor demektir (sertifika
uyarısında "Gelişmiş → Devam"). Gelmiyorsa: bulut firewall'da 8443 açık mı,
public adres doğru mu kontrol edin.

## Sorun giderme

| Belirti | Kontrol |
|---|---|
| İstemci bağlanamıyor | Bulut NSG/SG'de 8443 açık mı? `Server=` public adres doğru mu? |
| "403 / yetkisiz" | İstemci şifresi (ClientKey) sunucudakiyle aynı mı? |
| Sertifika reddi | `CertHash` sunucudaki `cert-thumbprint.txt` ile birebir mi? |
| Panel çok açık | 8360'ı firewall'da kapatın; panel şifresini güçlendirin |
| Yavaşlık | GZip sıkıştırma zaten var; VM ağ bant genişliğini kontrol edin |

## Notlar

- Otomatik güncelleme bulutta da çalışır: sunucuda yeni Setup → istemciler
  HTTPS üzerinden kendini günceller.
- Sunucu tek VM olduğu için yedekleme (VM snapshot + `Print360` SQL veritabanı
  yedeği) önerilir.
- Sürücü yönlendirme (`\\tsclient`) bulut RDP'de genelde çalışır ama Print360
  buna ihtiyaç duymaz; birincil kanal HTTPS'tir.
