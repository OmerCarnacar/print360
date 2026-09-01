
## 2026.09.01.1250

### Duzeltildi
- **KOK SEBEP: "ilk yazdirma oluyor, devami gelmiyor" (kesin cozum).**
  `HttpListener.QueryString`, yuzde-kodlu sorgu degerlerini isletim sisteminin
  ANSI kod sayfasiyla (Turkce sunucuda windows-1254) cozuyor; istemci ise
  UTF-8 kodluyordu. "Yazdır" icindeki `ı` (`%C4%B1`) sunucuda iki bozuk
  karaktere donusuyor, onaylanan is kuyrukta bulunamiyor, sunucu "dosya yok =
  zaten silinmis" sanip **OK** donuyor ve ayni is sonsuza kadar yeniden
  veriliyordu. Sorgu dizesi artik ham URL'den UTF-8 ile cozuluyor (`Q()`).
- Onaylanan is kuyrukta bulunamaz ama kuyrukta baska dosyalar varsa, artik
  sessizce "OK" donulmuyor: HTTP 500 ile bildiriliyor, sorun gizlenmiyor.

### Test
- `tests/QsKodlama.cs` - gercek HttpListener uzerinde 4 ad (sahadaki takilan
  is dahil) ACK ile birebir ayni POST istegiyle dogrulandi: 4/4.
