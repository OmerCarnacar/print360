# Güvenlik Politikası

## Güvenlik açığı bildirimi

Güvenlik açığını **herkese açık Issue olarak açmayın**.
Doğrudan e-posta gönderin: **omer.carnacar@outlook.com.tr**

Bildiriminizde şunlar olursa hızlı ilerleriz:
- Etkilenen bileşen (sunucu ajanı / panel / istemci / kurulum)
- Yeniden üretme adımları
- Etki değerlendirmeniz

Makul sürede yanıt vermeye ve düzeltmeyi bir sürümle yayınlamaya çalışırım.

## Kurulum için güvenlik notları

| Konu | Öneri |
|---|---|
| **Panel şifresi** | Kurulumda mutlaka belirleyin. Boş bırakılırsa panele ağdaki herkes erişebilir. |
| **MSSQL şifresi** | Kurulum varsayılanı `sa/Password1`'dir — **değiştirin**. Kullanmıyorsanız MSSQL'i hiç kurmayın (SQLite yeterlidir). |
| **HTTPS** | Kurulum self-signed sertifika üretir. İnternete açacaksanız `Print360-LetsEncrypt.ps1` ile gerçek sertifika kullanın. |
| **Sertifika sabitleme** | İstemcide `CertHash` alanına sunucunun parmak izini yazarsanız MITM'e karşı korunursunuz. |
| **İstemci şifresi** | `ClientKey` tanımlarsanız yalnızca kayıtlı makineler iş çekebilir. |
| **Ağ** | Panel portlarını (8360/8443) internete doğrudan açmayın; VPN veya güvenlik duvarı kuralı kullanın. |

## Kapsam dışı

- Yönetici yetkisiyle yapılan yerel değişiklikler
- `C:\Print360` klasörüne fiziksel/yönetici erişimi gerektiren senaryolar
