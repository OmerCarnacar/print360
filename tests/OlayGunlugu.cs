// REGRESYON TESTI: hata senaryolarinin gunluge DOGRU ve ANINDA dusmesi
//
// URETIM KODUNU SINAR: server\Print360.Ortak.cs + server\Print360.Db.cs.
// Senaryolar:
//   - gunlugun KENDISI yazilamiyor            -> yedek gunluge yazilir, sebep belirtilir
//   - klasore yazma izni yok / klasor olusmuyor -> hangi klasor, hangi kullanici, ne hata
//   - baglanti koptu / DNS / sertifika / 403   -> insan diliyle sebep ve yapilacak
//   - aktarim yarida kesildi (yarim PDF)       -> yaziciya GONDERILMEZ, sebep yazilir
//   - ayni hata surekli tekrar ediyor          -> ilk anda yazilir, tekrarlar sayilir
//   - dosya surekli kilitli / buyuyor          -> esik asilinca BIR KEZ bildirilir
//   - bilgisayar adi degisti                   -> bekleyen isler yeni ada tasinir
//   - ayni kimlik iki makinede                 -> isler TASINMAZ, cakisma bildirilir
//   - arka arkaya iki belge yazdirildi         -> her is KENDI belge adini/sayfasini alir
//   - belge adinda "__SEC" / "__PDF" geciyor    -> is turu belge adindan OKUNMAZ
//   - uzun yazici + uzun belge adi              -> dosya adi yol sinirina sigdirilir
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

class OlayGunlugu
{
    static int gecti, toplam;
    static void Kontrol(bool ok, string ad, string ayrinti)
    {
        toplam++; if (ok) gecti++;
        Console.WriteLine((ok ? "GECTI " : "KALDI ") + ad);
        if (!ok && ayrinti != null) Console.WriteLine("       -> " + ayrinti);
    }

    static string kok;

    static int Main()
    {
        kok = Path.Combine(Path.GetTempPath(), "p360-olay-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(kok);
        try
        {
            GunlukYedegi();
            KlasorDenetimi();
            BaglantiHatalari();
            YarimDosya();
            Tekrar();
            Takilma();
            AdDegisimi();
            HataAyiklama();
            BelgeOlayiEslestirme();
            IsAdiCozumleme();
            VpnSenaryolari();
        }
        finally { try { Directory.Delete(kok, true); } catch { } }
        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        return gecti == toplam ? 0 : 1;
    }

    // --- gunlugun kendisi yazilamiyor
    static void GunlukYedegi()
    {
        Ortak.YedekKlasor = Path.Combine(kok, "yedek");
        Directory.CreateDirectory(Ortak.YedekKlasor);
        string normal = Path.Combine(kok, "normal.log");
        Ortak.Gunluk(normal, "olagan satir");
        Kontrol(File.ReadAllText(normal).Contains("olagan satir") && !File.Exists(Ortak.YedekGunlukYolu(normal)),
                "olagan durumda gunluge yazilir, yedek kullanilmaz", null);

        // Gunluk yolu bir KLASOR: dosyaya eklenemez (izin yok / kilitli ile ayni sonuc)
        string bozuk = Path.Combine(kok, "kilitli.log");
        Directory.CreateDirectory(bozuk);
        Ortak.Gunluk(bozuk, "kayit 1");
        Ortak.Gunluk(bozuk, "kayit 2");
        string yedek = Ortak.YedekGunlukYolu(bozuk);
        string icerik = File.Exists(yedek) ? File.ReadAllText(yedek) : "";
        Kontrol(icerik.Contains("GUNLUK YAZILAMIYOR") && icerik.Contains("kayit 1") && icerik.Contains("kayit 2"),
                "gunluk yazilamazsa kayitlar yedege gider, sebep ilk satirda", icerik);
        int kac = icerik.Split(new[] { "GUNLUK YAZILAMIYOR" }, StringSplitOptions.None).Length - 1;
        Kontrol(kac == 1, "yedege gecis yalnizca BIR KEZ duyurulur", kac + " kez");
    }

    // --- klasor izinleri
    static void KlasorDenetimi()
    {
        string iyi = Path.Combine(kok, "iyi");
        var s1 = Ortak.KlasorDenetle(new[] { iyi });
        Kontrol(s1.Count == 0 && Directory.Exists(iyi), "yazilabilir klasor: sorun yok (yoksa olusturulur)", string.Join(" | ", s1.ToArray()));

        string dosya = Path.Combine(kok, "aslinda-dosya");
        File.WriteAllText(dosya, "x");
        var s2 = Ortak.KlasorDenetle(new[] { dosya });
        Kontrol(s2.Count == 1 && s2[0].StartsWith("KLASOR OLUSTURULAMADI") && s2[0].Contains(dosya),
                "klasor olusturulamiyor: hangi klasor oldugu yaziliyor", s2.Count > 0 ? s2[0] : "(yok)");

        // Yazma izni yok: bu kullaniciya klasorde dosya olusturma YASAKLANIR
        string yasak = Path.Combine(kok, "yasak");
        Directory.CreateDirectory(yasak);
        var kim = WindowsIdentity.GetCurrent().User;
        var kural = new FileSystemAccessRule(kim, FileSystemRights.CreateFiles | FileSystemRights.WriteData,
                                             AccessControlType.Deny);
        var ds = Directory.GetAccessControl(yasak);
        ds.AddAccessRule(kural);
        Directory.SetAccessControl(yasak, ds);
        try
        {
            var s3 = Ortak.KlasorDenetle(new[] { yasak });
            Kontrol(s3.Count == 1 && s3[0].StartsWith("YAZMA IZNI YOK") && s3[0].Contains(yasak)
                    && s3[0].Contains(Environment.UserName),
                    "yazma izni yok: klasor + kullanici + Windows hatasi yaziliyor", s3.Count > 0 ? s3[0] : "(yok)");
        }
        finally
        {
            ds = Directory.GetAccessControl(yasak);
            ds.RemoveAccessRule(kural);
            Directory.SetAccessControl(yasak, ds);
        }
    }

    // --- baglanti hatalari
    static void BaglantiHatalari()
    {
        Func<WebExceptionStatus, int, string> a = (d, k) => Ortak.BaglantiHatasiAcikla(d, k, "ham", "PC1");
        Kontrol(a(WebExceptionStatus.ConnectFailure, 0).StartsWith("SUNUCUYA BAGLANILAMADI"), "baglanti kurulamadi", null);
        Kontrol(a(WebExceptionStatus.NameResolutionFailure, 0).StartsWith("SUNUCU ADI COZULEMEDI"), "DNS", null);
        Kontrol(a(WebExceptionStatus.TrustFailure, 0).Contains("CertHash"), "sertifika degisti / sabitleme uyusmadi", null);
        Kontrol(a(WebExceptionStatus.ReceiveFailure, 0).StartsWith("BAGLANTI AKTARIM SIRASINDA KOPTU")
             && a(WebExceptionStatus.ConnectionClosed, 0).Contains("yeniden indirilir"), "aktarim sirasinda kopma", null);
        Kontrol(a(WebExceptionStatus.Timeout, 0).StartsWith("ZAMAN ASIMI"), "zaman asimi", null);
        Kontrol(a(WebExceptionStatus.ProtocolError, 403).Contains("'PC1'") && a(WebExceptionStatus.ProtocolError, 403).Contains("ClientKey"),
                "403: hangi makine ve ne yapilacagi", null);
        Kontrol(a(WebExceptionStatus.ProtocolError, 500).Contains("dashboard.log"), "500: sunucu gunlugune yonlendirir", null);
        Kontrol(a(WebExceptionStatus.UnknownError, 0).Contains("ham"), "bilinmeyen durumda ham mesaj korunur", null);

        // 403 kimden geldi? Print360 sifre reddinde govde "YETKISIZ"dir.
        Func<int, string, string> g = (k, govde) => Ortak.BaglantiHatasiAcikla(WebExceptionStatus.ProtocolError, k, "ham", "PC1", govde);
        Kontrol(g(403, "YETKISIZ").StartsWith("ISTEMCI SIFRESI REDDEDILDI"), "403 + YETKISIZ: gercekten sifre reddi", null);
        Kontrol(g(403, "<html>Access Denied by proxy</html>").Contains("YANIT PRINT360'TAN GELMIYOR"),
                "403 baska govdeyle: VEKIL/guvenlik duvari (eskiden yanlislikla 'sifre reddedildi' deniyordu)", null);
        Kontrol(g(407, "").StartsWith("VEKIL SUNUCU KIMLIK DOGRULAMASI"), "407: vekil kimlik dogrulamasi", null);
        Kontrol(a(WebExceptionStatus.SecureChannelFailure, 0).Contains("TLS"), "TLS uyusmazligi ayri anlatiliyor", null);

        // Vekil karari: Windows vekili bu adres icin tanimliysa yol degistirilir
        var hedef = new Uri("https://203.0.113.10:8443/");
        Ortak.DogrudanBaglan = false;
        Ortak.VekilBul = delegate(Uri u) { return new Uri("http://vekil.firma.local:8080/"); };
        string m1 = Ortak.VekilDegerlendir(hedef, WebExceptionStatus.ConnectFailure, 0);
        Kontrol(m1 != null && Ortak.DogrudanBaglan && m1.Contains("DOGRUDAN"), "vekil uzerinden ulasilamadi -> dogrudan dene", m1);
        var istek = (HttpWebRequest)WebRequest.Create(hedef);
        Ortak.VekilUygula(istek);
        Kontrol(istek.Proxy == null, "dogrudan modda istek vekilsiz gider", null);
        string m2 = Ortak.VekilDegerlendir(hedef, WebExceptionStatus.Timeout, 0);
        Kontrol(m2 != null && !Ortak.DogrudanBaglan && m2.Contains("vekil"), "dogrudan da olmazsa vekile geri doner", m2);
        Kontrol(Ortak.VekilDegerlendir(hedef, WebExceptionStatus.TrustFailure, 0) == null && !Ortak.DogrudanBaglan,
                "sertifika hatasi yol degistirmez (yol sorunu degil)", null);
        Ortak.VekilBul = delegate(Uri u) { return null; };
        Kontrol(Ortak.VekilDegerlendir(hedef, WebExceptionStatus.ConnectFailure, 0) == null && !Ortak.DogrudanBaglan,
                "vekil tanimli degilse denenecek baska yol yok", null);
    }

    // --- yarim / bozuk dosya
    static void YarimDosya()
    {
        string tam = Path.Combine(kok, "tam.pdf");
        File.WriteAllText(tam, "%PDF-1.7\n1 0 obj <<>> endobj\ntrailer <<>>\n%%EOF\n");
        string neden;
        Kontrol(Ortak.DosyaButunMu(tam, out neden), "butun PDF gecer", neden);

        string yarim = Path.Combine(kok, "yarim.pdf");
        var b = new StringBuilder("%PDF-1.7\n");
        for (int i = 0; i < 400; i++) b.Append("0123456789 stream verisi ").Append(i).Append('\n');
        File.WriteAllText(yarim, b.ToString());   // %%EOF YOK: aktarim yarida kesildi
        Kontrol(!Ortak.DosyaButunMu(yarim, out neden) && neden.StartsWith("PDF YARIM"), "yarim PDF yakalanir", neden);

        string bos = Path.Combine(kok, "bos.pdf");
        File.WriteAllText(bos, "");
        Kontrol(!Ortak.DosyaButunMu(bos, out neden) && neden.Contains("BOS"), "0 baytlik dosya yakalanir", neden);

        string sahte = Path.Combine(kok, "sahte.pdf");
        File.WriteAllText(sahte, "<html>hata sayfasi</html>%%EOF");
        Kontrol(!Ortak.DosyaButunMu(sahte, out neden) && neden.Contains("%PDF"), "PDF olmayan icerik yakalanir", neden);

        string xps = Path.Combine(kok, "is.xps");
        File.WriteAllBytes(xps, new byte[] { (byte)'P', (byte)'K', 3, 4, 0 });
        string xpsBozuk = Path.Combine(kok, "bozuk.xps");
        File.WriteAllText(xpsBozuk, "bozuk");
        Kontrol(Ortak.DosyaButunMu(xps, out neden) && !Ortak.DosyaButunMu(xpsBozuk, out neden), "XPS (ZIP) denetimi", neden);

        string diger = Path.Combine(kok, "not.txt");
        File.WriteAllText(diger, "duz metin");
        Kontrol(Ortak.DosyaButunMu(diger, out neden), "bilinmeyen tur engellenmez (eski davranis)", neden);
    }

    // --- tekrar filtresi
    static void Tekrar()
    {
        var t0 = new DateTime(2026, 10, 1, 9, 0, 0);
        var saat = t0;
        var f = new TekrarFiltresi();
        f.Simdi = delegate { return saat; };

        var c1 = f.Hata("BAGLANTI KOPTU");
        Kontrol(c1.Count == 1 && c1[0] == "BAGLANTI KOPTU", "ilk hata ANINDA yazilir", null);
        int bastirilan = 0;
        for (int i = 0; i < 20; i++) { saat = saat.AddSeconds(10); bastirilan += f.Hata("BAGLANTI KOPTU").Count; }
        Kontrol(bastirilan == 0, "ayni hata 20 kez: gunluk dolmaz (3 dk 20 sn)", bastirilan + " satir");

        saat = t0.AddMinutes(6);
        var c2 = f.Hata("BAGLANTI KOPTU");
        Kontrol(c2.Count == 1 && c2[0].StartsWith("(devam ediyor)") && c2[0].Contains("22 kez"),
                "uzun suren hata periyodik ozetle hatirlatilir", c2.Count > 0 ? c2[0] : "(yok)");

        saat = saat.AddSeconds(5);
        var c3 = f.Hata("SERTIFIKA UYUSMUYOR");
        Kontrol(c3.Count == 2 && c3[0].Contains("onceki hata") && c3[1] == "SERTIFIKA UYUSMUYOR",
                "FARKLI hata hemen yazilir, oncekinin toplami da", string.Join(" || ", c3.ToArray()));

        saat = saat.AddSeconds(30);
        string d = f.Duzeldi("sunucuya ulasildi");
        Kontrol(d != null && d.StartsWith("DUZELDI") && d.Contains("SERTIFIKA"), "duzelince kapanis satiri", d);
        Kontrol(f.Duzeldi("tekrar") == null, "hata yokken 'duzeldi' yazilmaz", null);
    }

    // --- takilma izleyici
    static void Takilma()
    {
        var saat = new DateTime(2026, 10, 1, 9, 0, 0);
        var t = new TakilmaIzleyici();
        t.Simdi = delegate { return saat; };
        string yol = Path.Combine(kok, "kilitli.pdf");
        Kontrol(t.HazirDegil(yol, "dosya kilitli") == null, "ilk gorulmede bildirilmez (gecici olabilir)", null);
        saat = saat.AddSeconds(90);
        Kontrol(t.HazirDegil(yol, "dosya kilitli") == null, "esik dolmadan bildirilmez", null);
        saat = saat.AddSeconds(40);
        string m = t.HazirDegil(yol, "dosya kilitli: baska surec kullaniyor");
        Kontrol(m != null && m.StartsWith("TAKILDI") && m.Contains("kilitli.pdf") && m.Contains("baska surec"),
                "esik asilinca dosya + sure + sebep bildirilir", m);
        saat = saat.AddMinutes(5);
        Kontrol(t.HazirDegil(yol, "dosya kilitli") == null, "ayni takilma bir daha bildirilmez", null);
        string c = t.Hazir(yol);
        Kontrol(c != null && c.StartsWith("TAKILMA COZULDU"), "cozulunce bildirilir", c);
        Kontrol(t.Hazir(Path.Combine(kok, "hic-takilmadi.pdf")) == null, "takilmayan dosya icin gurultu yok", null);
    }

    // --- hata ayiklama anahtari: ac, 24 saat sonra kendiliginden kapanir; sifre maskeleme
    static void HataAyiklama()
    {
        var saat = new DateTime(2026, 10, 8, 9, 0, 0);
        Ortak.HaSimdi = delegate { return saat; };
        Ortak.HataAyiklamaDosyasi = Path.Combine(kok, "hata-ayiklama.txt");
        Kontrol(!Ortak.HataAyiklamaAcik(), "varsayilan: kapali", null);
        Kontrol(Ortak.HataAyiklamaAyarla(true, TimeSpan.FromHours(24)) == null && Ortak.HataAyiklamaAcik(), "acilinca hemen gecerli", null);
        Kontrol(Ortak.HataAyiklamaBitisi() == saat.AddHours(24), "bitis zamani 24 saat sonra", Ortak.HataAyiklamaBitisi().ToString());
        saat = saat.AddHours(23).AddMinutes(59);
        Kontrol(Ortak.HataAyiklamaAcik(), "23 sa 59 dk sonra hala acik", null);
        saat = saat.AddMinutes(2);
        Kontrol(!Ortak.HataAyiklamaAcik(), "24 saat dolunca KENDILIGINDEN kapanir (gunluk sismesin)", null);
        Ortak.HataAyiklamaAyarla(true, TimeSpan.FromHours(24));
        Ortak.HataAyiklamaAyarla(false, TimeSpan.Zero);
        Kontrol(!Ortak.HataAyiklamaAcik() && !File.Exists(Ortak.HataAyiklamaDosyasi), "kapatilinca dosya silinir", null);
        File.WriteAllText(Ortak.HataAyiklamaDosyasi, "bozuk icerik");
        Ortak.HataAyiklamaAyarla(false, TimeSpan.Zero); File.WriteAllText(Ortak.HataAyiklamaDosyasi, "bozuk icerik");
        saat = saat.AddMinutes(1);
        Kontrol(!Ortak.HataAyiklamaAcik(), "bozuk anahtar dosyasi 'kapali' sayilir", null);

        Kontrol(Ortak.GizliMaskele("ClientKey=gizli123") == "ClientKey=*** (8 karakter)"
             && Ortak.GizliMaskele("Password=x") == "Password=*** (1 karakter)"
             && Ortak.GizliMaskele("Server=203.0.113.10") == "Server=203.0.113.10"
             && Ortak.GizliMaskele("ClientKey=") == "ClientKey=(bos)",
                "tani paketinde sifreler maskelenir, diger ayarlar aynen kalir", Ortak.GizliMaskele("ClientKey=gizli123"));
    }

    // --- bilgisayar adi degisti / ayni kimlik iki makinede
    static void AdDegisimi()
    {
        Kuyruk.Kok = Path.Combine(kok, "queue");
        Kuyruk.AnlikSureMs = 0;
        string eskiQ = Path.Combine(Kuyruk.Kok, "MUHASEBE-01");
        Directory.CreateDirectory(eskiQ);
        File.WriteAllText(Path.Combine(eskiQ, "20261001_090000_001_ayse~Fatura.pdf.gz"), "x");
        File.WriteAllText(Path.Combine(eskiQ, "20261001_090100_002_mehmet~Rapor.pdf.gz"), "x");
        int n = Kuyruk.MakineAdiDegisti("MUHASEBE-01", "MUH-PC-YENI");
        Kontrol(n == 2 && Directory.GetFiles(Path.Combine(Kuyruk.Kok, "MUH-PC-YENI"), "*.gz").Length == 2,
                "ad degisince TUM kullanicilarin bekleyen isleri yeni ada tasinir", n + " is");

        var t0 = new DateTime(2026, 10, 1, 9, 0, 0);
        string eski;
        // 1) gercek ad degisikligi: yeni ad gelir, eski ad bir daha GELMEZ
        var d1 = new KimlikDefteri(Path.Combine(kok, "kimlik1.csv"));
        Kontrol(d1.Gorus("iid-A", "PC-ESKI", t0, out eski) == "yeni", "ilk gorulme kaydedilir", null);
        Kontrol(d1.Gorus("iid-A", "PC-ESKI", t0.AddMinutes(1), out eski) == "ayni", "ayni ad: degisiklik yok", null);
        Kontrol(d1.Gorus("iid-A", "PC-YENI", t0.AddMinutes(2), out eski) == "gozleniyor",
                "yeni ad HEMEN karar verilmez (hizli yeniden baslatmada yanlis 'cakisma' olmasin)", null);
        Kontrol(d1.Gorus("iid-A", "PC-YENI", t0.AddMinutes(3), out eski) == "gozleniyor", "bekleme suresince gozlenir", null);
        string r = d1.Gorus("iid-A", "PC-YENI", t0.AddMinutes(5, 30), out eski);
        Kontrol(r == "ad-degisti" && eski == "PC-ESKI", "eski ad gelmedi -> AD DEGISTI", r + " / " + eski);
        // Defter dosyaya yazildi: panel yeniden baslasa da yeni ad kayitli
        var d1b = new KimlikDefteri(Path.Combine(kok, "kimlik1.csv"));
        Kontrol(d1b.Gorus("iid-A", "PC-YENI", t0.AddMinutes(20), out eski) == "ayni", "defter yeniden baslatmadan sonra da gecerli", null);

        // 2) cakisma: iki canli makine ayni kimlikle SIRAYLA gelir
        var d2 = new KimlikDefteri(Path.Combine(kok, "kimlik2.csv"));
        d2.Gorus("iid-K", "SUBE-A", t0, out eski);
        Kontrol(d2.Gorus("iid-K", "SUBE-B", t0.AddSeconds(30), out eski) == "gozleniyor", "ikinci ad gozlenir", null);
        r = d2.Gorus("iid-K", "SUBE-A", t0.AddSeconds(60), out eski);
        Kontrol(r == "cakisma" && eski == "SUBE-B", "eski ad yeniden geldi -> CAKISMA (isler tasinmaz)", r + " / " + eski);
        Kontrol(d2.Gorus("iid-K", "SUBE-A", t0.AddSeconds(90), out eski) == "ayni", "kayitli ad degismedi", null);
    }

    static void VpnSenaryolari()
    {
        // VPN kapaliyken sunucu bekleme suresine girdi; VPN acilinca hemen denenmeli
        var s = new SunucuSagligi();
        const string u = "https://10.77.0.5:8443";
        for (int i = 0; i < 6; i++) s.Basarisiz(u, "ConnectFailure");
        Kontrol(!s.Denenebilir(u), "VPN kapali: sunucu bekleme suresinde", null);
        Kontrol(s.HataVar(u), "son denemesi basarisiz sunucu isaretlenir (kisa port denemesine girer)", null);
        s.Sifirla();
        Kontrol(s.Denenebilir(u), "ag degisince bekleme suresi sifirlanir (eskiden 10 dk beklerdi)", null);

        // RDP farkli porttan: yalnizca mstsc'nin baglantisi sayilir
        var b = new[] {
            new TcpKayit { Ip = "10.77.0.61", Port = 3389, Pid = 1 },     // baska program, 3389
            new TcpKayit { Ip = "10.77.0.90", Port = 3390, Pid = 500 },   // mstsc, farkli port
            new TcpKayit { Ip = "142.250.1.1", Port = 443, Pid = 2 },    // tarayici: RDP degil
            new TcpKayit { Ip = "127.0.0.1", Port = 13389, Pid = 500 },  // mstsc, yerel tunel
            new TcpKayit { Ip = "10.77.0.90", Port = 3390, Pid = 500 } }; // ayni sunucu
        var l = RdpAlgilama.Sec(b, new[] { 500 });
        Kontrol(l.Contains("10.77.0.90"), "farkli porttan giden RDP algilandi (eskiden 'RDP oturumu yok')", string.Join(",", l.ToArray()));
        Kontrol(l.Contains("10.77.0.61"), "3389 baglantisi eskisi gibi algilanir", null);
        Kontrol(!l.Contains("142.250.1.1"), "RDP olmayan baglanti sunucu sanilmaz", null);
        Kontrol(!l.Contains("127.0.0.1"), "yerel tunel sunucu sanilmaz", null);
        Kontrol(l.Count == 2, "ayni sunucu bir kez listelenir", l.Count.ToString());

        Kontrol(RdpAlgilama.VpnMi("Ethernet", "Ethernet 4", "Fortinet SSL VPN Virtual Ethernet Adapter"), "FortiClient VPN taninir", null);
        Kontrol(RdpAlgilama.VpnMi("Tunnel", "Tailscale", "Tailscale Tunnel"), "Tailscale taninir", null);

        // Sunucu arama: Tailscale makineleri (kendi adresimiz haric)
        string ts = "{\"Self\":{\"HostName\":\"PC\",\"TailscaleIPs\":[\"100.64.0.1\",\"fd7a::1\"]},"
                  + "\"Peer\":{\"k1\":{\"HostName\":\"SRV\",\"TailscaleIPs\": [ \"100.64.0.2\", \"fd7a::2\"]},"
                  + "\"k2\":{\"HostName\":\"NAS\",\"TailscaleIPs\":[\"100.64.0.3\"]}}}";
        var es = RdpAlgilama.TailscaleEslesleri(ts, new[] { "100.64.0.1" });
        Kontrol(es.Count == 2 && es.Contains("100.64.0.2") && es.Contains("100.64.0.3"), "Tailscale makineleri listelendi, kendi adresimiz haric", string.Join(",", es.ToArray()));
        Kontrol(RdpAlgilama.TailscaleEslesleri("", new string[0]).Count == 0, "Tailscale yoksa aday yok", null);
        Kontrol(RdpAlgilama.AltAgAdaylari("10.8.0.5", "255.255.255.0").Count == 253 && !RdpAlgilama.AltAgAdaylari("10.8.0.5", "255.255.255.0").Contains("10.8.0.5"),
                "VPN agi taranir, kendi adresi haric", null);
        Kontrol(RdpAlgilama.AltAgAdaylari("100.64.0.1", "255.255.255.255").Count == 0, "noktadan noktaya VPN'de tarama yok", null);
        // Sahadan: Port=13389 + tarama 8443'u buldu
        var bilinenler = new List<string> { "https://100.64.0.50:8443" };
        Func<string, bool> saglam = x => false, sorunlu = x => true;
        var ad = RdpAlgilama.YanlisPortlariEle(new List<string> { "https://100.64.0.50:13389", "https://100.64.0.50:8443", "https://10.77.0.5:8443" }, bilinenler, saglam);
        Kontrol(ad.Count == 2 && ad[0] == "https://100.64.0.50:8443" && ad[1] == "https://10.77.0.5:8443",
                "calisan port bilinince yanlis port denenmez, baska sunucuya dokunulmaz", string.Join(",", ad.ToArray()));
        Kontrol(RdpAlgilama.CalisanAdres("https://100.64.0.50:13389", bilinenler, saglam) == "https://100.64.0.50:8443", "guncelleme calisan porta gider", null);
        Kontrol(RdpAlgilama.YanlisPortlariEle(new List<string> { "https://100.64.0.50:13389" }, bilinenler, sorunlu)[0] == "https://100.64.0.50:13389",
                "bilinen adres hatadaysa ayardaki port yine denenir (sunucu port degistirmis olabilir)", null);
        Kontrol(SunucuSagligi.BaglantiKapandiMi(WebExceptionStatus.ReceiveFailure) && !SunucuSagligi.BaglantiKapandiMi(WebExceptionStatus.ConnectFailure),
                "port acik ama baglanti kesiliyor durumu ayirt edilir", null);
        // Sahadan: mstsc adresi 100.64.0.50:13389, 13389 Print360 port ayarina girmis
        var rp = new HashSet<int> { 3389, 13389 };
        Kontrol(RdpAlgilama.MruPortu("100.64.0.50:13389") == 13389 && RdpAlgilama.MruPortu("SRV01") == 0 && RdpAlgilama.MruPortu("fe80::1") == 0,
                "RDP gecmisinden port okunur", null);
        Kontrol(RdpAlgilama.GecerliPort("13389", rp) == "" && RdpAlgilama.GecerliPort("3389", rp) == "" && RdpAlgilama.GecerliPort(" 9443 ", rp) == "9443",
                "RDP portu Print360 portu sayilmaz, gercek port korunur", null);
        Kontrol(RdpAlgilama.SunucuGecerli("100.64.0.50:13389", rp) == "100.64.0.50" && RdpAlgilama.SunucuGecerli("srv:9443", rp) == "srv:9443",
                "sunucu adresindeki RDP portu atilir, panel portu korunur", null);
        var notlar = new List<string>();
        var onar = RdpAlgilama.AyarOnar(new[] { "; yorum", "Server=100.64.0.50:13389", "Port=13389", "Printer=Xerox" }, rp, notlar);
        Kontrol(onar != null && onar[1] == "Server=100.64.0.50" && onar[2] == "Port=" && onar[3] == "Printer=Xerox" && onar[0] == "; yorum" && notlar.Count == 2,
                "ayar dosyasi onarilir, diger satirlara dokunulmaz", onar == null ? "null" : string.Join(" | ", onar.ToArray()));
        Kontrol(RdpAlgilama.AyarOnar(new[] { "Server=srv:9443", "Port=9443", "Server2=x" }, rp, new List<string>()) == null, "dogru ayarlar degistirilmez", null);
        Kontrol(RdpAlgilama.AyarOnar(new[] { "Server=", "Port=" }, rp, new List<string>()) == null, "bos ayarlar degistirilmez", null);
        Kontrol(Kuyruk.Sanitize("..") == "_" && Kuyruk.Sanitize(".") == "_" && Kuyruk.Sanitize(" . ") == "_", "'..' kuyruk klasorunun disina cikamaz", Kuyruk.Sanitize(".."));
        Kontrol(Kuyruk.Sanitize("CON") == "_CON" && Kuyruk.Sanitize("nul.txt") == "_nul.txt" && Kuyruk.Sanitize("COM1") == "_COM1", "Windows'a ayrilmis adlar klasor olamaz", null);
        Kontrol(Kuyruk.Sanitize("PC-01") == "PC-01" && Kuyruk.Sanitize("COMPAQ") == "COMPAQ" && Kuyruk.Sanitize("a\\b") == "a_b", "normal adlar eskisi gibi kalir", null);
        Kontrol(Kuyruk.MakineAdiGecerli("PC-01") && !Kuyruk.MakineAdiGecerli("") && !Kuyruk.MakineAdiGecerli(new string('a', 65)) && !Kuyruk.MakineAdiGecerli("a\nb"),
                "gecersiz makine adi reddedilir", null);
        // Sahadan: "Hedef yazici sirasi: Xerox Phaser 3020 (Kopya 1)   [1. SIRA - benim yazicim] > ..."
        const string XR = "Xerox Phaser 3020 (Kopya 1)";
        Kontrol(Ortak.YaziciAdiTemizle(XR + "   [1. SIRA - benim yazicim]") == XR && Ortak.YaziciAdiTemizle(XR + "   [3. yedek]") == XR
                && Ortak.YaziciAdiTemizle(XR + "   (Windows varsayilani)") == XR, "etiketler yazici adindan atilir", null);
        Kontrol(Ortak.YaziciAdiTemizle(XR) == XR && Ortak.YaziciAdiTemizle("HP [Kat 2]") == "HP [Kat 2]" && Ortak.YaziciAdiTemizle("Canon (varsayilan) A4") == "Canon (varsayilan) A4",
                "gercek yazici adina dokunulmaz (parantez ve koseli parantez dahil)", null);
        string mk, pt;
        Kontrol(RdpAlgilama.AdresAyir("srv", out mk, out pt) && mk == "srv" && pt == "", "adres: yalnizca makine", null);
        Kontrol(RdpAlgilama.AdresAyir(" 100.64.0.50:9443 ", out mk, out pt) && mk == "100.64.0.50" && pt == "9443", "adres: makine ve port", null);
        Kontrol(RdpAlgilama.AdresAyir("https://srv:9443/panel", out mk, out pt) && mk == "srv" && pt == "9443", "adres: tarayicidan yapistirilmis", null);
        Kontrol(!RdpAlgilama.AdresAyir("  ", out mk, out pt) && !RdpAlgilama.AdresAyir("a b", out mk, out pt), "adres: bos ya da gecersiz reddedilir", null);
        var yz = RdpAlgilama.AyarlariYaz(new[] { "; yorum", "Server=eski", "SunucuArama=0" }, new Dictionary<string, string> { { "Server", "srv:9443" }, { "Port", "" } });
        Kontrol(yz.Count == 4 && yz[0] == "; yorum" && yz[1] == "Server=srv:9443" && yz[2] == "SunucuArama=0" && yz[3] == "Port=",
                "baglanti ayari yazilir, diger satirlar korunur", string.Join(" | ", yz.ToArray()));
        var hp = RdpAlgilama.HizliPortlar("9443");
        Kontrol(hp[0] == 9443 && hp.Contains(8443) && hp.Contains(8360) && hp.FindAll(x => x == 9443).Count == 1,
                "ayardaki port once denenir, varsayilanlar da taranir, tekrar yok", null);
        Kontrol(RdpAlgilama.HizliPortlar("gecersiz")[0] == 8443, "gecersiz port ayari taramayi bozmaz", null);
        Kontrol(RdpAlgilama.WebDisiPort(3389) && RdpAlgilama.WebDisiPort(445) && !RdpAlgilama.WebDisiPort(9443), "RDP/SMB gibi portlara HTTP sorulmaz", null);
        Kontrol(RdpAlgilama.Print360Yaniti("2026.11.02.1318", ""), "yeni sunucu basligindan taninir", null);
        Kontrol(RdpAlgilama.Print360Yaniti(null, "2026.9.1.1254"), "eski sunucu surum metninden taninir", null);
        Kontrol(!RdpAlgilama.Print360Yaniti(null, "<html>baska bir site</html>"), "baska servis sunucu sanilmaz", null);
        Kontrol(RdpAlgilama.VpnMi("Ppp", "Sirket", "WAN Miniport"), "Windows VPN taninir", null);
        Kontrol(!RdpAlgilama.VpnMi("Ethernet", "Ethernet", "Realtek PCIe GbE Family Controller"), "normal ag karti VPN sanilmaz", null);
    }

    static void IsAdiCozumleme()
    {
        // Is turu belge adindan okunmaz
        Kontrol(IsAdi.Tur("20261102_100000_001_ayse~Rapor__SECTION_1.pdf") == "",
                "belge adinda __SEC: secim penceresi ACILMAZ, dogrudan basilir", null);
        Kontrol(IsAdi.Tur("20261102_100000_001_ayse~Fatura__PDF.pdf") == "",
                "belge adinda __PDF: PDF olarak acilmaz, basilir", null);
        Kontrol(IsAdi.Tur("20261102_100000_001_ayse__SEC~Rapor.pdf") == "SEC", "gercek SEC isi taninir", null);
        Kontrol(IsAdi.Tur("20261102_100000_001_ayse__PDF~Rapor.pdf") == "PDF", "gercek PDF isi taninir", null);
        Kontrol(IsAdi.Tur("20261102_100000_001_ayse__PDF.pdf") == "PDF", "belge adi yokken de tur taninir", null);
        string st = IsAdi.TurDegistir("20261102_100000_001_ayse__SEC~a__SEC b.pdf", "__SEC", "__SECTO__4142");
        Kontrol(st == "20261102_100000_001_ayse__SECTO__4142~a__SEC b.pdf", "yazici gomulurken belge adina dokunulmaz", st);
        Kontrol(IsAdi.Tur(st) == "SECTO" && IsAdi.SectoHex(st) == "4142", "gomulu yazici geri okunur", IsAdi.SectoHex(st));
        Kontrol(IsAdi.SectoHex("20261102_100000_001_ayse__SECTO__4142.pdf") == "4142", "belge adi yokken de yazici okunur", null);
        Kontrol(IsAdi.TurDegistir("x_ayse__PDF~a__PDF.pdf", "__PDF", "") == "x_ayse~a__PDF.pdf", "PDF eki yalnizca turden silinir", null);

        // Yol siniri
        string not;
        string kisa = "20261102_100000_001_ayse__SECTO__4142~Rapor.pdf";
        Kontrol(IsAdi.Sigdir(kisa, 185, out not) == kisa && not == null, "sigan ada dokunulmaz", not);
        string hex = new string('a', 130), belge = new string('b', 60);
        string uzun = "20261102_100000_001_ayse__SECTO__" + hex + "~" + belge + ".pdf";
        string s1 = IsAdi.Sigdir(uzun, 185, out not);
        Kontrol(s1.Length <= 185 && IsAdi.SectoHex(s1) == hex && s1.EndsWith(".pdf"),
                "once belge adi kisaltilir, secilen yazici KORUNUR", s1.Length + " | " + s1);
        string dev = "20261102_100000_001_ayse__SECTO__" + new string('a', 300) + "~" + belge + ".pdf";
        string s2 = IsAdi.Sigdir(dev, 185, out not);
        Kontrol(s2.Length <= 185 && IsAdi.Tur(s2) == "SEC" && s2.EndsWith(".pdf"),
                "yazici adi hic sigmiyorsa is kaybolmaz: yazici istemcide sorulur", s2.Length + " | " + s2);
        string s3 = IsAdi.Sigdir("20261102_100000_001_ayse~" + new string('b', 400) + ".pdf", 185, out not);
        Kontrol(s3.Length <= 185 && s3.Contains("~bbb"), "tursuz isin belge adi kisaltilir", s3.Length.ToString());
    }

    static void BelgeOlayiEslestirme()
    {
        // Kullanici 10:00:00'da "A Fatura", 10:00:04'te "B Rapor" yazdirdi. A'nin spool
        // dosyasi islenirken iki olay da gunlukte (en yeni once gelir).
        var t = new DateTime(2026, 10, 29, 10, 0, 0);
        var olaylar = new List<KeyValuePair<long, DateTime>> {
            new KeyValuePair<long, DateTime>(502, t.AddSeconds(4)),    // B Rapor
            new KeyValuePair<long, DateTime>(501, t.AddSeconds(0)) };  // A Fatura
        var s = new BelgeOlayiSecici();
        int a = s.Sec(olaylar, t.AddMilliseconds(300));
        Kontrol(a == 1, "A isine A'nin olayi verildi (eskiden en yeni olay = B'nin adi aliniyordu)", a.ToString());
        int b = s.Sec(olaylar, t.AddSeconds(4).AddMilliseconds(200));
        Kontrol(b == 0, "B isine B'nin olayi verildi", b.ToString());
        Kontrol(s.Sec(olaylar, t.AddSeconds(4)) == -1, "bir olay ikinci bir ise VERILMEZ", null);
        Kontrol(new BelgeOlayiSecici().Sec(olaylar, t.AddMinutes(10)) == -1,
                "yazilma anindan cok uzak olay eslestirilmez (baska is)", null);
        // B'nin olayi henuz dusmemis olsa bile A'nin adi B'ye gecmez
        var s2 = new BelgeOlayiSecici();
        s2.Sec(olaylar.GetRange(1, 1), t);
        Kontrol(s2.Sec(olaylar.GetRange(1, 1), t.AddSeconds(4)) == -1, "kullanilmis olay sonraki ise kopyalanmaz", null);
    }
}

static class DateTimeUzanti
{
    public static DateTime AddMinutes(this DateTime t, int dk, int sn) { return t.AddMinutes(dk).AddSeconds(sn); }
}
