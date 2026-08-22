// ============================================================
//  Print360 - RDP Yazdirma ve Yonetim Cozumu
//  Gelistirici : Omer CARNACAR  <omer.carnacar@outlook.com.tr>
//  Lisans      : UCRETSIZ SURUM - para ile satilamaz (bkz. LICENSE)
//  Telif       : (c) 2026 Omer CARNACAR
// ============================================================
// Print360 Client Agent
// Kullanicinin yerel bilgisayarinda calisir.
// C:\Print360\jobs klasorune dusen PDF/XPS islerini SumatraPDF ile
// varsayilan (veya Print360.ini'de tanimli) yaziciya sessizce basar.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;

static class ClientAgent
{
    static string baseDir = @"C:\Print360";
    static string jobsDir = @"C:\Print360\jobs";
    static string doneDir = @"C:\Print360\done";
    static string failedDir = @"C:\Print360\failed";
    static string logFile = @"C:\Print360\logs\client.log";
    static string iniFile = @"C:\Print360\Print360.ini";
    static string printedCsv = @"C:\Print360\stats\printed.csv";
    static string vcOutbox = @"C:\Print360\vc-outbox";

    // Gelistirici bilgisi (arayuzde tiklanabilir e-posta olarak gosterilir)
    const string GELISTIRICI = "Omer CARNACAR";
    const string EPOSTA = "omer.carnacar@outlook.com.tr";
    const string LINKEDIN = "https://www.linkedin.com/in/omercarnacar/";

    // LinkedIn profilini varsayilan tarayicida ac
    static void LinkedInAc()
    {
        try { Process.Start(new ProcessStartInfo(LINKEDIN) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            Log("LinkedIn acilamadi: " + ex.Message);
            try
            {
                System.Windows.Forms.Clipboard.SetText(LINKEDIN);
                System.Windows.Forms.MessageBox.Show(
                    "Tarayici acilamadi.\r\nAdres panoya kopyalandi:\r\n\r\n" + LINKEDIN,
                    "Print360", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
            }
            catch { }
        }
    }

    // E-posta adresine tiklaninca varsayilan posta programini ac (konu hazir gelir)
    static void MailAc()
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "mailto:" + EPOSTA + "?subject=" + Uri.EscapeDataString("Print360 v" + Surum.V + " - " + Environment.MachineName))
            { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("Posta programi acilamadi: " + ex.Message);
            try
            {   // Posta istemcisi yoksa adresi panoya kopyala ve bildir
                System.Windows.Forms.Clipboard.SetText(EPOSTA);
                System.Windows.Forms.MessageBox.Show(
                    "Posta programi acilamadi.\r\nAdres panoya kopyalandi:\r\n\r\n" + EPOSTA,
                    "Print360", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
            }
            catch { }
        }
    }

    // --- Arayuzun okudugu canli durum (dongular gunceller) ---
    static bool durumOk;                 // sunucuya ulasilabiliyor mu
    static string durumMetin = "Baglanti bekleniyor...";
    static string durumKanal = "";       // "RDP Virtual Channel" | "HTTPS https://..."
    static DateTime durumZaman;          // son basarili temas
    static string sonYaziciRapor = "";   // HeartbeatLoop'ta uretilen WMI raporu (cache)
    static bool rdpAcik;                 // su an aktif RDP oturumu var mi (RdpIzleyici gunceller)
    static int sunucuBagli;              // su an ulasilabilen sunucu sayisi (tepsi ipucunda gosterilir)
    static DateTime sonIsHatasi = DateTime.MinValue;   // is cekme hatasi gunlugunu kisitlar
    static string sonTekrarEdenIs;                      // sunucunun tekrar verdigi is (dongu korumasi)
    static DateTime sonTekrarUyari = DateTime.MinValue;
    static bool sonBildirilen;           // en son balon bildirimi yapilan durum
    static bool bildirimBasladi;         // ilk bildirim yapildi mi

    // Baglanti durumu degisince kullaniciyi tepsiden bilgilendir (bagli <-> kopuk).
    // Ayni durum tekrar ederse balon gosterilmez; tepsi ipucu her zaman guncellenir.
    static DateTime sonBaglantiBildirimi = DateTime.MinValue;

    // kisaBilgi: balonda gosterilecek KISA metin (uzun aciklama yalniz log/arayuzde)
    static void DurumBildir(bool ok, string baslik, string mesaj, string kisaBilgi = null)
    {
        if (string.IsNullOrEmpty(kisaBilgi)) kisaBilgi = mesaj;
        try
        {
            durumOk = ok;
            if (tepsi == null) return;   // arayuz kapaliysa (Arayuz=0) sessiz gec
            // Tepsi ipucu ve balon arayuz is parcacigina aittir (bkz. Bildir)
            var s = senkron;
            Action ipucuYaz = delegate { TepsiIpucuGuncelle(); };
            if (s != null && s.IsHandleCreated && s.InvokeRequired) s.BeginInvoke(ipucuYaz);
            else ipucuYaz();

            if (bildirimBasladi && ok == sonBildirilen) return;   // durum degismedi
            bool ilkDurum = !bildirimBasladi;
            sonBildirilen = ok; bildirimBasladi = true;
            Log((ok ? "BAGLANTI KURULDU: " : "BAGLANTI KESILDI: ") + mesaj);

            // GURULTU AZALTMA: kullanici surekli bildirimle rahatsiz edilmesin.
            //  - BAGLANDI  -> KISA bilgi balonu (3 sn)
            //  - KOPTU     -> BALON YOK; yalnizca log + tepsi ipucu + arayuz rengi
            //    (baglanti dalgalandiginda arka arkaya uyari cikmasi engellenir)
            //  - Ayrica ayni bilgi 10 dk icinde tekrar gosterilmez.
            if (!ok) return;
            if (!ilkDurum && (DateTime.Now - sonBaglantiBildirimi).TotalMinutes < 10) return;
            sonBaglantiBildirimi = DateTime.Now;
            Bildir("Baglandi", kisaBilgi, true);
        }
        catch { }
    }

    // VC modu: Print360.ini'de VCMode=1 ise onay/sayac/heartbeat HTTPS yerine
    // RDP sanal kanalindan gider (IP/port/HTTPS ayari gerekmez).
    // Mekanizma: mesaj vc-outbox'a .msg olarak birakilir; mstsc icindeki
    // Print360.VC.dll onu kanala yazar; sunucu VChannel.DinlemeyeBasla ile okur.
    // VCMode:  auto (VARSAYILAN) | 1 (zorla VC) | 0 (zorla HTTPS)
    //
    // auto = TAM KANAL MANTIGI, ama tahmine dayanmaz:
    //   mstsc icindeki Print360.VC.dll kanal acilinca
    //   C:\Print360\vc-outbox\.vc-aktif isaret dosyasini olusturur,
    //   kanal kapaninca siler. Isaret varsa her sey RDP kanalindan gider
    //   (IP/port/HTTPS gerekmez); yoksa otomatik HTTPS'e dusulur.
    //   Boylece eklenti kurulu olmayan makinede sistem sessizce olmez.
    static bool VcAcik(Dictionary<string, string> cfg)
    {
        string m = cfg.ContainsKey("VCMode") ? cfg["VCMode"].Trim().ToLowerInvariant() : "auto";
        if (m == "0") return false;
        if (m == "1") return true;          // zorla (tanilama icin)
        return VcKanalCalisiyor();          // auto
    }

    // Kanal gercekten ayakta mi? (isaret dosyasi + outbox tikanmamis)
    static bool VcKanalCalisiyor()
    {
        try
        {
            if (!File.Exists(Path.Combine(vcOutbox, ".vc-aktif"))) return false;
            // Eklenti mesajlari tuketmiyorsa (birikme varsa) kanali saglikli sayma
            return Directory.GetFiles(vcOutbox, "*.msg").Length <= 3;
        }
        catch { return false; }
    }

    // Ters protokol (istemci->sunucu):  [turUzunlugu:4 LE][tur][veriUzunlugu:4 LE][veri]
    static void VcYaz(string tur, string veri)
    {
        try
        {
            Directory.CreateDirectory(vcOutbox);
            byte[] t = Encoding.UTF8.GetBytes(tur);
            byte[] v = Encoding.UTF8.GetBytes(veri ?? "");
            using (var ms = new MemoryStream())
            {
                ms.Write(BitConverter.GetBytes(t.Length), 0, 4);
                ms.Write(t, 0, t.Length);
                ms.Write(BitConverter.GetBytes(v.Length), 0, 4);
                ms.Write(v, 0, v.Length);
                // Atomik birak: eklenti yalnizca tam .msg gorur (.tmp -> rename)
                string ad = Guid.NewGuid().ToString("N");
                string tmp = Path.Combine(vcOutbox, ad + ".tmp");
                string son = Path.Combine(vcOutbox, ad + ".msg");
                File.WriteAllBytes(tmp, ms.ToArray());
                File.Move(tmp, son);
            }
        }
        catch (Exception ex) { Log("VC outbox yazilamadi (" + tur + "): " + ex.Message); }
    }

    [STAThread]
    static void Main()
    {
        bool created;
        using (var mx = new Mutex(true, "Print360ClientAgent", out created))
        {
            // Zaten calisiyorsa: calisan ornege "durum penceresini ac" sinyali gonder
            // (kullanici kisayola/exe'ye tekrar tikladiginda arayuz acilsin).
            if (!created) { GosterSinyaliGonder(); return; }
            // HTTPS: TLS 1.2 + self-signed sertifika kabulu.
            // Print360.ini'de CertHash tanimliysa yalnizca o parmak izine sahip sertifika kabul edilir (pinning).
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            System.Net.ServicePointManager.ServerCertificateValidationCallback = (snd, cert, chain, err) =>
            {
                try
                {
                    var cfg = ReadIni();
                    string pin = cfg.ContainsKey("CertHash") ? cfg["CertHash"].Replace(" ", "").Replace(":", "").Trim() : "";
                    if (pin.Length > 0)
                        return cert.GetCertHashString().Equals(pin, StringComparison.OrdinalIgnoreCase);
                    return true; // pin yoksa self-signed kabul (kanal yine sifreli)
                }
                catch { return false; }
            };
            Directory.CreateDirectory(jobsDir);
            Directory.CreateDirectory(doneDir);
            Directory.CreateDirectory(failedDir);
            Directory.CreateDirectory(Path.Combine(baseDir, "logs"));
            Directory.CreateDirectory(Path.Combine(baseDir, "stats"));
            Log("Ajan basladi (v" + Surum.Etiket + "). Izlenen klasor: " + jobsDir);
            VarsayilanYaziciyiKurDefaults();   // istemcide her zaman bir varsayilan bulunsun
            new Thread(RdpIzleyici) { IsBackground = true }.Start();   // RDP acilir acilmaz algila
            new Thread(HeartbeatLoop) { IsBackground = true }.Start();
            new Thread(JobPollLoop) { IsBackground = true }.Start();
            new Thread(UpdateLoop) { IsBackground = true }.Start();

            // Arayuz (tepsi simgesi + durum penceresi). Print360.ini'de Arayuz=0 ise
            // eski sessiz davranis korunur (is dongusu ana thread'de calisir).
            var cfg0 = ReadIni();
            bool arayuz = !cfg0.ContainsKey("Arayuz") || cfg0["Arayuz"].Trim() != "0";
            if (arayuz)
            {
                new Thread(IsDongusu) { IsBackground = true }.Start();
                ArayuzuCalistir();   // mesaj dongusu - burada bloklar
            }
            else IsDongusu();
        }
    }

    // Jobs klasorunu izleyip basan ana is dongusu.
    static void IsDongusu()
    {
        while (true)
        {
            try
            {
                foreach (var f in Directory.GetFiles(jobsDir)
                         .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => f))
                {
                    if (!IsStable(f)) continue;
                    PrintJob(f);
                }
                Prune();
            }
            catch (Exception ex) { Log("HATA: " + ex.Message); }
            Thread.Sleep(2000);
        }
    }

    static bool IsStable(string path)
    {
        try
        {
            long s1 = new FileInfo(path).Length;
            if (s1 == 0) return false;
            Thread.Sleep(500);
            if (new FileInfo(path).Length != s1) return false;
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            return true;
        }
        catch { return false; }
    }

    static void PrintJob(string file)
    {
        var cfg = ReadIni();
        string fname = Path.GetFileName(file);

        // Is turu: __PDF -> goruntule/kaydet, __SEC -> yazici sec
        if (fname.IndexOf("__PDF", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            PdfGoruntule(file);
            return;
        }

        string printer = cfg.ContainsKey("Printer") ? cfg["Printer"].Trim() : "";

        // Yazici SUNUCUDA secildi: dosya adina gomulu (__SECTO__<hex>) -> dogrudan ona bas
        int stIdx = fname.IndexOf("__SECTO__", StringComparison.OrdinalIgnoreCase);
        if (stIdx >= 0)
        {
            string hex = fname.Substring(stIdx + 9);
            // Ad, '~' ile baslayan orijinal belge adini da icerebilir -> once orada kes
            int kes = hex.IndexOfAny(new[] { '~', '.' });
            if (kes >= 0) hex = hex.Substring(0, kes);
            string sunucudaSecilen = HexCoz(hex);
            if (sunucudaSecilen.Length > 0) { printer = sunucudaSecilen; Log("Yazici sunucuda secildi: " + printer); }
        }
        else if (fname.IndexOf("__SEC", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Log("Yazici secim penceresi aciliyor: " + fname);
            string secilen = YaziciSec();
            if (secilen == null)
            {
                string dc = Path.Combine(doneDir, fname);
                if (File.Exists(dc)) File.Delete(dc);
                File.Move(file, dc);
                RecordPrinted(fname, Environment.MachineName + " \\ (secilmedi)", "IPTAL");
                Log("Kullanici yazdirmayi iptal etti: " + fname);
                return;
            }
            printer = secilen;
        }

        // --- Yazici cozumleme: atanan yoksa varsayilana, o da yoksa ilk gercek yaziciya duser ---
        string hedef = GecerliYazici(printer);
        if (hedef == null)
        {
            Basarisiz(file, fname, printer.Length > 0 ? printer : "-", "bu bilgisayarda kullanilabilir yazici yok");
            return;
        }
        if (printer.Length > 0 && !hedef.Equals(printer, StringComparison.OrdinalIgnoreCase))
            Log("UYARI: '" + printer + "' yazicisi bulunamadi; '" + hedef + "' kullanilacak");

        // --- Cift motorlu baski + 3 deneme ---
        string sumatra = cfg.ContainsKey("SumatraPath") ? cfg["SumatraPath"] : Path.Combine(baseDir, "SumatraPDF.exe");
        bool yedekAcik = !cfg.ContainsKey("YedekMotor") || cfg["YedekMotor"].Trim() != "0";
        string sonHata = "";
        bool basarili = false;
        Log("Yazdiriliyor: " + fname + " -> " + hedef);
        for (int deneme = 1; deneme <= 3 && !basarili; deneme++)
        {
            // 1. motor: SumatraPDF (sessiz, guvenilir)
            if (File.Exists(sumatra))
            {
                try
                {
                    using (var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = sumatra,
                        Arguments = "-print-to \"" + hedef + "\" -silent -exit-when-done \"" + file + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }))
                    {
                        if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } sonHata = "SumatraPDF zaman asimi"; }
                        else if (p.ExitCode == 0) basarili = true;
                        else sonHata = "SumatraPDF cikis kodu " + p.ExitCode;
                    }
                }
                catch (Exception ex) { sonHata = "SumatraPDF: " + ex.Message; }
            }
            else sonHata = "SumatraPDF bulunamadi (" + sumatra + ")";

            // 2. motor (yedek): Windows 'printto' kanali (Adobe/kurulu PDF uygulamasi)
            if (!basarili && yedekAcik)
            {
                try
                {
                    var psi = new ProcessStartInfo(file)
                    {
                        Verb = "printto",
                        Arguments = "\"" + hedef + "\"",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    var pp = Process.Start(psi);
                    if (pp != null) { try { pp.WaitForExit(120000); } catch { } }
                    basarili = true; // shell kabul ettiyse is Windows kuyruguna verildi
                    Log("Yedek motor (printto) kullanildi: " + fname);
                }
                catch (Exception ex) { sonHata += " | printto: " + ex.Message; }
            }
            if (!basarili)
            {
                Log("Deneme " + deneme + "/3 basarisiz: " + sonHata);
                Thread.Sleep(2000 * deneme);
            }
        }
        if (!basarili) { Basarisiz(file, fname, YerelEtiket(hedef), sonHata); return; }

        string dst = Path.Combine(doneDir, fname);
        if (File.Exists(dst)) File.Delete(dst);
        File.Move(file, dst);
        RecordPrinted(fname, YerelEtiket(hedef));
        Log("Tamamlandi: " + fname + " -> " + hedef);
        // Kullaniciya BILDIR: is bitti, cikti yaziciya gitti.
        // KISA bildirim: belge adi + yazici (tek satir, 3 sn)
        Bildir("Yazdirildi", GorunenBelgeAdi(fname) + "  ->  " + hedef, true);
    }

    // Tepsiden balon bildirim (arayuz kapaliysa sessizce gecer).
    // Yazdirma bitince kullanici sonucu ANINDA gorur; log'a bakmasi gerekmez.
    //
    // ONEMLI: Bu metot ARKA PLAN is parcaciklarindan cagrilir (baski dongusu,
    // kalp atisi). NotifyIcon arayuz is parcacigina aittir; dogrudan cagirmak
    // capraz-thread erisimidir ve balon SESSIZCE gorunmez. Bu yuzden istek
    // 'senkron' penceresi uzerinden arayuz is parcacigina aktarilir.
    static void Bildir(string baslik, string mesaj, bool basarili)
    {
        try
        {
            if (tepsi == null) return;
            Action goster = delegate
            {
                try
                {
                    tepsi.ShowBalloonTip(basarili ? 3000 : 8000, "Print360 - " + baslik, mesaj,
                        basarili ? System.Windows.Forms.ToolTipIcon.Info
                                 : System.Windows.Forms.ToolTipIcon.Error);
                }
                catch { }
            };
            var s = senkron;
            if (s != null && s.IsHandleCreated && s.InvokeRequired) s.BeginInvoke(goster);
            else goster();
        }
        catch { }
    }

    // Kayitlarda yazicinin hangi LOKAL PC'ye ait oldugunu belli et:
    // "MAKINE \ Yazici Adi"  (RDP sunucusundaki 'Print360 - x' sanal yazicisiyla karismaz)
    static string YerelEtiket(string yazici)
    {
        return Environment.MachineName + " \\ " + yazici;
    }

    // Istenen yaziciyi kurulu yazicilarla eslestir (buyuk/kucuk harf duyarsiz);
    // yoksa varsayilan, o da yoksa ilk gercek (sanal olmayan) yazici.
    // ---- KISIYE OZEL YAZICI SECIMI ----
    // Ayni bilgisayarda birden fazla kullanici olabilir ve makinede 5-10 yazici
    // bulunabilir. Her Windows kullanicisi KENDI hedef yazicisini secer; secim
    // burada saklanir. Boylece is "kafasina gore" rastgele bir yaziciya gitmez.
    static string KisiselYaziciDosyasi
    {
        get
        {
            string ad = Environment.UserName;
            foreach (char c in Path.GetInvalidFileNameChars()) ad = ad.Replace(c, '_');
            return Path.Combine(baseDir, "stats", "yazici-" + ad + ".txt");
        }
    }

    // Kullanicinin ONCELIK SIRALI hedef yazici listesi (dosyada her satir bir yazici).
    // 1. sira yoksa/kapaliysa 2. sira denenir, o da yoksa 3. ... Boylece is asla
    // "kafasina gore" baska bir yaziciya gitmez; sira KULLANICININ belirledigidir.
    public static List<string> KisiselYaziciListesi()
    {
        var liste = new List<string>();
        try
        {
            string f = KisiselYaziciDosyasi;
            if (File.Exists(f))
                foreach (var ln in File.ReadAllLines(f, Encoding.UTF8))
                {
                    // Eski surumlerden kalan BOM'u da temizle
                    string s = ln.Trim().TrimStart('﻿').Trim();
                    if (s.Length == 0) continue;
                    bool var_ = false;
                    foreach (var x in liste) if (string.Equals(x, s, StringComparison.OrdinalIgnoreCase)) { var_ = true; break; }
                    if (!var_) liste.Add(s);
                }
        }
        catch { }
        return liste;
    }

    public static void KisiselYaziciListesiYaz(List<string> liste)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(baseDir, "stats"));
            // BOM'suz UTF-8: BOM, ilk satirdaki yazici adinin basina gorunmez bir
            // karakter ekleyip eslesmeyi bozabilir.
            File.WriteAllLines(KisiselYaziciDosyasi, (liste ?? new List<string>()).ToArray(),
                               new UTF8Encoding(false));
            Log("Hedef yazici sirasi (" + Environment.UserName + "): " +
                (liste == null || liste.Count == 0 ? "(Windows varsayilani)" : string.Join(" > ", liste.ToArray())));
        }
        catch (Exception ex) { Log("Yazici sirasi kaydedilemedi: " + ex.Message); }
    }

    // Yaziciyi 1. siraya al (varsa listeden cikarilip basa eklenir)
    public static void KisiselYaziciBirinciYap(string yazici)
    {
        if (string.IsNullOrEmpty(yazici)) return;
        var l = KisiselYaziciListesi();
        l.RemoveAll(x => string.Equals(x, yazici, StringComparison.OrdinalIgnoreCase));
        l.Insert(0, yazici);
        KisiselYaziciListesiYaz(l);
    }

    // Yedek olarak sona ekle
    public static void KisiselYaziciYedekEkle(string yazici)
    {
        if (string.IsNullOrEmpty(yazici)) return;
        var l = KisiselYaziciListesi();
        foreach (var x in l) if (string.Equals(x, yazici, StringComparison.OrdinalIgnoreCase)) return;
        l.Add(yazici);
        KisiselYaziciListesiYaz(l);
    }

    public static void KisiselYaziciCikar(string yazici)
    {
        var l = KisiselYaziciListesi();
        l.RemoveAll(x => string.Equals(x, yazici, StringComparison.OrdinalIgnoreCase));
        KisiselYaziciListesiYaz(l);
    }

    // Windows'un VARSAYILAN yazicisi (sanal olanlar sayilmaz)
    public static string WindowsVarsayilanYazici()
    {
        try
        {
            var vs = new System.Drawing.Printing.PrinterSettings();
            if (vs.IsValid && !SanalYazici(vs.PrinterName)) return vs.PrinterName;
        }
        catch { }
        return "";
    }

    // ILK CALISTIRMADA VARSAYILANI KUR:
    // Kullanici hic secim yapmadiysa liste bos kalir ve arayuzde "secilmedi"
    // gorunurdu. Bunun yerine Windows'un varsayilan yazicisi otomatik olarak
    // 1. siraya konur; kullanici isterse degistirir. Boylece istemcide her
    // zaman net bir varsayilan yazici bulunur.
    static void VarsayilanYaziciyiKurDefaults()
    {
        try
        {
            if (KisiselYaziciListesi().Count > 0) return;      // kullanici zaten secmis
            string v = WindowsVarsayilanYazici();
            if (v.Length == 0)
            {
                // Varsayilan sanal/yoksa: tek gercek yazici varsa onu al
                var gercek = new List<string>();
                foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                    if (!SanalYazici(p)) gercek.Add(p);
                if (gercek.Count == 1) v = gercek[0];
            }
            if (v.Length == 0) return;
            KisiselYaziciListesiYaz(new List<string> { v });
            Log("Varsayilan hedef yazici otomatik ayarlandi (Windows varsayilanindan): " + v);
        }
        catch (Exception ex) { Log("Varsayilan yazici kurulamadi: " + ex.Message); }
    }

    // Bir yazici SU AN kullanilabilir mi? (kurulu + cevrimdisi/hatali degil)
    static bool YaziciKullanilabilir(string ad, List<string> kurulu)
    {
        bool kuruluMu = false;
        foreach (var p in kurulu)
            if (p.Trim().Equals(ad.Trim(), StringComparison.OrdinalIgnoreCase)) { kuruluMu = true; break; }
        if (!kuruluMu) return false;
        // Saglik raporundan durum bak (heartbeat dongusu doldurur)
        try
        {
            foreach (var ln in (sonYaziciRapor ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = CsvAyir(ln);
                if (f.Length < 4) continue;
                if (!f[0].Trim().Equals(ad.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (f[1] == "Cevrimdisi" || f[2] == "Kagit bitti" || f[2] == "Cevrimdisi") return false;
                return true;
            }
        }
        catch { }
        return true;   // rapor yoksa kurulu olmasi yeterli
    }

    // Hedef yaziciyi belirle. ONCELIK SIRASI:
    //   1. Isle birlikte gelen ad (sunucuda secilmisse)
    //   2. KULLANICININ kendi SIRALI listesi: 1. sira -> yoksa 2. sira -> ...
    //   3. Print360.ini'deki Printer= (makine geneli)
    //   4. Windows VARSAYILAN yazicisi
    //   5. Makinede tek gercek yazici varsa o
    // Hicbiri yoksa null -> is "failed" olur ve kullanici uyarilir.
    // RASTGELE / YANLIS bir yaziciya ASLA gonderilmez.
    static string GecerliYazici(string istenen)
    {
        try
        {
            var kurulu = new List<string>();
            foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters) kurulu.Add(p);

            // 1) Isle gelen ad
            if (!string.IsNullOrEmpty(istenen))
                foreach (var p in kurulu)
                    if (p.Trim().Equals(istenen.Trim(), StringComparison.OrdinalIgnoreCase)) return p;

            // 2) Kullanicinin SIRALI listesi - once kullanilabilir olani ara
            var sira = KisiselYaziciListesi();
            foreach (var aday in sira)
                if (YaziciKullanilabilir(aday, kurulu))
                    foreach (var p in kurulu)
                        if (p.Trim().Equals(aday.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.Equals(aday, sira[0], StringComparison.OrdinalIgnoreCase))
                                Log("1. siradaki yazici kullanilamiyor; yedege gecildi: " + p);
                            return p;
                        }
            // Kullanilabilirlik saglanamadiysa listede KURULU olan ilkini yine de dene
            foreach (var aday in sira)
                foreach (var p in kurulu)
                    if (p.Trim().Equals(aday.Trim(), StringComparison.OrdinalIgnoreCase))
                    { Log("Siradaki yazicilar sorunlu gorunuyor; yine de deneniyor: " + p); return p; }

            // 3) ini'deki makine geneli ayar
            try
            {
                var cfg = ReadIni();
                string ini = cfg.ContainsKey("Printer") ? cfg["Printer"].Trim() : "";
                if (ini.Length > 0)
                    foreach (var p in kurulu)
                        if (p.Trim().Equals(ini, StringComparison.OrdinalIgnoreCase)) return p;
            }
            catch { }

            // 4) Windows VARSAYILAN yazicisi (sanal degilse)
            try
            {
                var vs = new System.Drawing.Printing.PrinterSettings();
                if (vs.IsValid && !SanalYazici(vs.PrinterName)) return vs.PrinterName;
            }
            catch { }

            // 5) Tek gercek yazici varsa belirsizlik yok
            var gercek = new List<string>();
            foreach (var p in kurulu) if (!SanalYazici(p)) gercek.Add(p);
            if (gercek.Count == 1) return gercek[0];

            Log("HEDEF YAZICI SECILMEMIS: bu bilgisayarda " + gercek.Count + " yazici var. " +
                "Print360 penceresi > Yazicilar sekmesinden kendi yazicinizi secin.");
            return null;
        }
        catch { return string.IsNullOrEmpty(istenen) ? null : istenen; }
    }

    // Tum denemeler tukendi: isi failed klasorune al, sunucuya HATA bildir (panelde uyari olur)
    static void Basarisiz(string file, string fname, string yazici, string neden)
    {
        try
        {
            Directory.CreateDirectory(failedDir);
            string dst = Path.Combine(failedDir, fname);
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(file, dst);
        }
        catch (Exception ex) { Log("failed tasima hatasi: " + ex.Message); }
        RecordPrinted(fname, yazici, "HATA: " + neden);
        Log("BASILAMADI (" + neden + "): " + fname + "  [dosya: " + failedDir + "]");
        Bildir("YAZDIRILAMADI", GorunenBelgeAdi(fname) + "\r\nYazici: " + yazici
                              + "\r\nSebep: " + neden, false);
    }

    // PDF modu: isi masaustune kaydet ve varsayilan PDF goruntuleyiciyle ac
    static void PdfGoruntule(string file)
    {
        string fname = Path.GetFileName(file);
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Print360 Belgeler");
            Directory.CreateDirectory(dir);
            string dst = Path.Combine(dir, fname.Replace("__PDF", ""));
            File.Copy(file, dst, true);
            string dc = Path.Combine(doneDir, fname);
            if (File.Exists(dc)) File.Delete(dc);
            File.Move(file, dc);
            try { Process.Start(dst); } catch (Exception ex) { Log("PDF acilamadi (dosya masaustunde): " + ex.Message); }
            RecordPrinted(fname, "PDF olarak acildi");
            Log("PDF modunda teslim edildi: " + dst);
        }
        catch (Exception ex) { Log("PDF goruntule hatasi: " + ex.Message); }
    }

    // Kullaniciya gosterilecek BELGE ADI. Sunucu, orijinal belge adini dosya
    // adina '~' ayiricisiyla gomer:
    //   20260727_170635_543_admin~Fatura2026.pdf  ->  "Fatura2026"
    // Gomulu ad yoksa (olay gunlugu bos) dosya adi oldugu gibi gosterilir.
    static string GorunenBelgeAdi(string dosyaAdi)
    {
        try
        {
            int i = dosyaAdi.LastIndexOf('~');
            if (i < 0) return dosyaAdi;
            string s = dosyaAdi.Substring(i + 1);
            if (s.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            return s.Length > 0 ? s : dosyaAdi;
        }
        catch { return dosyaAdi; }
    }

    // Sunucunun dosya adina gomdugu hex yazici adini geri coz (UTF-8).
    static string HexCoz(string hex)
    {
        try
        {
            if (hex.Length == 0 || hex.Length % 2 != 0) return "";
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return Encoding.UTF8.GetString(b);
        }
        catch { return ""; }
    }

    // Yazici secim modu: kullaniciya BU PC'nin yazici listesini goster (STA).
    // RDP tam ekran acikken bile gorunmesi icin en-ustte (TopMost) sahip pencereyle acilir.
    static string YaziciSec()
    {
        string secilen = null;
        var t = new Thread(() =>
        {
            try
            {
                using (var sahip = new System.Windows.Forms.Form
                {
                    TopMost = true,
                    ShowInTaskbar = true,
                    Text = "Print360 - [" + Environment.MachineName + "] yerel yazici secimi",
                    StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                    Size = new System.Drawing.Size(1, 1),
                    FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
                    Opacity = 0
                })
                using (var dlg = new System.Windows.Forms.PrintDialog())
                {
                    sahip.Show();
                    sahip.Activate();
                    System.Windows.Forms.Application.DoEvents();
                    dlg.UseEXDialog = true;
                    dlg.AllowSomePages = false;
                    if (dlg.ShowDialog(sahip) == System.Windows.Forms.DialogResult.OK)
                        secilen = dlg.PrinterSettings.PrinterName;
                }
            }
            catch (Exception ex) { Log("Yazici secim penceresi hatasi: " + ex.Message); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        if (!t.Join(180000)) return null; // 3 dk icinde secim yapilmazsa iptal
        return secilen;
    }

    // Basilan isi kaydet: (1) yerel dosyaya (\\tsclient ile geri cekilir),
    // (2) sunucu tanimliysa dogrudan sunucuya POST (merkezi sayac, anlik)
    static void RecordPrinted(string fileName, string printer, string durum = "OK")
    {
        var fields = new[] { DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                             Environment.MachineName, fileName, printer, durum };
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(fields[i].Replace("\"", "\"\"")).Append('"');
        }
        string line = sb.ToString();
        try { File.AppendAllText(printedCsv, line + "\r\n"); }
        catch (Exception ex) { Log("Istatistik yazilamadi: " + ex.Message); }
        PushToServer(line);
    }

    // Otomatik guncelleme: sunucudaki surumu periyodik kontrol et; yeni surum varsa
    // yeni ajani indir, kendini degistir ve yeniden baslat (self-update).
    static void UpdateLoop()
    {
        Thread.Sleep(15000); // ilk aciliste 15 sn bekle (diger baslatmalar otursun)
        while (true)
        {
            try { GuncellemeKontrol(); }
            catch (Exception ex) { Log("Guncelleme kontrolu: " + ex.Message); }
            Thread.Sleep(30 * 60 * 1000); // 30 dakikada bir
        }
    }

    static void GuncellemeKontrol()
    {
        var cfg = ReadIni();
        string baseUrl = BaseUrl(cfg);
        if (baseUrl == null) return;
        string sunucuSurum;
        using (var wc = new P360WebClient()) sunucuSurum = wc.DownloadString(baseUrl + "/api/clientversion").Trim();
        Version sv, mv;
        if (!Version.TryParse(sunucuSurum, out sv) || !Version.TryParse(Surum.V, out mv)) return;
        if (sv <= mv) return; // guncel

        Log("Yeni surum bulundu: " + sunucuSurum + " (mevcut " + Surum.V + "). Indiriliyor...");
        string cur = Process.GetCurrentProcess().MainModule.FileName;   // C:\Print360\Print360.ClientAgent.exe
        string yeni = cur + ".new";
        using (var wc = new P360WebClient()) wc.DownloadFile(baseUrl + "/api/clientexe", yeni);
        if (!File.Exists(yeni) || new FileInfo(yeni).Length < 4096) { try { File.Delete(yeni); } catch { } return; }

        // Guncelleyici: ajan kapansin -> yeni exe'yi eskinin uzerine yaz -> yeniden baslat
        string cmd = Path.Combine(baseDir, "update.cmd");
        File.WriteAllText(cmd,
            "@echo off\r\n" +
            "ping -n 3 127.0.0.1 >nul\r\n" +
            "taskkill /f /im Print360.ClientAgent.exe >nul 2>&1\r\n" +
            "ping -n 2 127.0.0.1 >nul\r\n" +
            "move /y \"" + yeni + "\" \"" + cur + "\" >nul\r\n" +
            "start \"\" \"" + cur + "\"\r\n" +
            "del \"%~f0\"\r\n");
        Log("Guncelleme " + sunucuSurum + " hazir; ajan yeniden baslatiliyor.");
        Process.Start(new ProcessStartInfo
        {
            FileName = cmd,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = true
        });
        Environment.Exit(0);   // Mutex serbest kalir; guncelleyici yeni surumu baslatir
    }

    // Sunucu taban adresi: varsayilan HTTPS:8443; UseHttps=0 ile HTTP:8360'a donulur.
    static string sonRdpSunucu;   // RDP kapaninca son bilinen sunucu (bekleyen isler tamamlansin)
    static string sonOtoLog;

    // Kullanicinin RDP ile bagli oldugu sunucunun IP'sini bul (aktif 3389 baglantisi).
    // Boylece Server= bos/"auto" olsa bile client, sunucuyu ayar girmeden bulur.
    // AYNI ANDA ACIK TUM RDP oturumlarinin sunucu IP'leri.
    // Kullanici birden fazla sunucuya baglanabilir; is HANGISINDEN gelirse
    // gelsin alinmalidir. Tek bir sunucu secmek yanlis sunucuya bakmaya yol acardi.
    static List<string> RdpSunucularBul()
    {
        var liste = new List<string>();
        try
        {
            foreach (var c in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections())
                if (c.RemoteEndPoint.Port == 3389 && c.State == TcpState.Established)
                {
                    string ip = c.RemoteEndPoint.Address.ToString();
                    bool var_ = false;
                    foreach (var x in liste) if (x == ip) { var_ = true; break; }
                    if (!var_) liste.Add(ip);
                }
        }
        catch (Exception ex)
        { if (sonOtoLog != ex.Message) { Log("Oto sunucu bulma: " + ex.Message); sonOtoLog = ex.Message; } }
        return liste;
    }

    // Geriye uyumluluk: tek sunucu isteyen yerler icin ilkini dondurur
    static string RdpAktifSunucu()
    {
        var l = RdpSunucularBul();
        return l.Count > 0 ? l[0] : null;
    }

    // Son kullanilan (calistigi dogrulanmis) sunucular - RDP kapansa da
    // bekleyen isler tamamlanabilsin diye hatirlanir.
    static readonly List<string> sonCalisanSunucular = new List<string>();

    static void SunucuHatirla(string baseUrl)
    {
        lock (sonCalisanSunucular)
        {
            foreach (var x in sonCalisanSunucular)
                if (string.Equals(x, baseUrl, StringComparison.OrdinalIgnoreCase)) return;
            sonCalisanSunucular.Add(baseUrl);
        }
    }

    // Denenecek TUM sunucu adresleri.
    //   Server= ayari varsa yalnizca o; yoksa acik TUM RDP oturumlari
    //   (+ daha once calistigi dogrulanmis adresler).
    static List<string> SunucuAdresleri(Dictionary<string, string> cfg)
    {
        var sonuc = new List<string>();
        string server = cfg.ContainsKey("Server") ? cfg["Server"].Trim() : "";
        bool https = !cfg.ContainsKey("UseHttps") || cfg["UseHttps"].Trim() != "0";
        string port = cfg.ContainsKey("Port") ? cfg["Port"].Trim() : "";

        Func<string, string> yap = delegate (string konak)
        {
            string k = konak;
            if (!k.Contains(":"))
            {
                string p = port.Length > 0 ? port : (https ? "8443" : "8360");
                k += ":" + p;
            }
            return (https ? "https://" : "http://") + k;
        };

        // Server= elle verilmisse: VIRGULLE birden fazla sunucu yazilabilir
        //   ornek:  Server=SRV01,SRV02,SRV03
        if (server.Length > 0 && !server.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var s in server.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string u = yap(s.Trim());
                bool v_ = false;
                foreach (var x in sonuc) if (string.Equals(x, u, StringComparison.OrdinalIgnoreCase)) { v_ = true; break; }
                if (!v_) sonuc.Add(u);
            }
            return sonuc;
        }

        foreach (var ip in RdpSunucularBul())
        {
            string u = yap(ip);
            bool var_ = false;
            foreach (var x in sonuc) if (string.Equals(x, u, StringComparison.OrdinalIgnoreCase)) { var_ = true; break; }
            if (!var_) sonuc.Add(u);
        }
        lock (sonCalisanSunucular)
            foreach (var u in sonCalisanSunucular)
            {
                bool var_ = false;
                foreach (var x in sonuc) if (string.Equals(x, u, StringComparison.OrdinalIgnoreCase)) { var_ = true; break; }
                if (!var_) sonuc.Add(u);
            }
        return sonuc;
    }

    static string RdpSunucusuBul()
    {
        string ip = RdpAktifSunucu();
        if (ip != null)
        {
            if (ip != sonRdpSunucu)
            {
                Log("RDP sunucusu otomatik bulundu: " + ip + " (ayar gerekmedi)");
                sonRdpSunucu = ip;
            }
            return ip;
        }
        return sonRdpSunucu;   // RDP o an kapaliysa son bilinen sunucuyu kullan
    }

    // RDP oturumu acilir/kapanirsa ANINDA tepki ver (5 sn'de bir bakar):
    // kalp atisi dongusunu uyandirir, boylece 60 sn beklenmez.
    static readonly ManualResetEvent hbTetik = new ManualResetEvent(false);

    static void RdpIzleyici()
    {
        string onceki = null;
        while (true)
        {
            try
            {
                string s = RdpAktifSunucu();
                if (s != onceki)
                {
                    if (s != null)
                    {
                        sonRdpSunucu = s;
                        Log("RDP oturumu ACILDI: " + s + " - sunucuya hemen baglaniliyor.");
                        rdpAcik = true;
                    }
                    else
                    {
                        Log("RDP oturumu KAPANDI.");
                        rdpAcik = false;
                    }
                    onceki = s;
                    hbTetik.Set();   // kalp atisi dongusu hemen calissin
                }
            }
            catch { }
            Thread.Sleep(5000);
        }
    }

    static string BaseUrl(Dictionary<string, string> cfg)
    {
        string server = cfg.ContainsKey("Server") ? cfg["Server"].Trim() : "";
        // Server bos veya "auto" ise: aktif RDP baglantisindan sunucuyu otomatik bul
        if (server.Length == 0 || server.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            server = RdpSunucusuBul();
            if (string.IsNullOrEmpty(server)) return null;  // henuz RDP acilmamis
        }
        bool https = !cfg.ContainsKey("UseHttps") || cfg["UseHttps"].Trim() != "0";
        // Server=host:port biciminde port verilmisse ona dokunma; yoksa Port ayarini
        // ya da varsayilani (HTTPS 8443 / HTTP 8360) kullan.
        if (!server.Contains(":"))
        {
            string port = cfg.ContainsKey("Port") ? cfg["Port"].Trim() : "";
            if (port.Length == 0) port = https ? "8443" : "8360";
            server += ":" + port;
        }
        return (https ? "https://" : "http://") + server;
    }

    // HTTPS is kanali: sunucudaki kuyrugu 3 sn'de bir yoklar, GZip'li isi indirip acar,
    // jobs klasorune birakir (yazdirma dongusu basar) ve sunucuya ACK gonderir.
    // tsclient yonlendirmesi kapali olsa bile yazdirma bu kanaldan calisir.
    // GUVENLIK AGI: VC modunda da HTTPS kuyrugu ARADA BIR yoklanir.
    // Sebep: kanalin acik sayilmasi ".vc-aktif" isaret dosyasinin VARLIGINA bakar.
    // RDP oturumu anormal koptugunda (mstsc oldurulur, ag gider) bu dosya geride
    // kalabiliyor; istemci kanali sonsuza kadar "acik" sanip HTTPS'i hic yoklamiyor
    // ve isler sunucu kuyrugunda birikip kaliyordu. Artik VC acik gorunse bile her
    // ~30 saniyede bir kuyruk yoklanir; kanal gercekten calisiyorsa kuyruk zaten
    // bostur ve bu istek bedelsizdir.
    const int VC_GUVENLIK_AGI = 10;   // 10 x 3 sn = ~30 sn

    static void JobPollLoop()
    {
        int tur = 0;
        while (true)
        {
            try
            {
                var cfg = ReadIni();
                tur++;
                // VC modu: is normalde dogrudan RDP kanalindan (Print360.VC.dll -> jobs)
                // gelir; kuyruk yoklamasi gereksizdir. Yine de yukaridaki sebeple
                // periyodik bir guvenlik yoklamasi yapilir.
                bool vc = VcAcik(cfg);
                if (!vc || tur % VC_GUVENLIK_AGI == 0)
                {
                    if (vc) tur = 0;
                    // COKLU RDP: acik TUM sunucular yoklanir; is hangisinden
                    // gelirse gelsin alinir (tek sunucu secmek yanlis olurdu).
                    string key = cfg.ContainsKey("ClientKey") ? cfg["ClientKey"].Trim() : "";
                    foreach (var baseUrl in SunucuAdresleri(cfg))
                    {
                        try
                        {
                            bool alindi = false;
                            // Kuyruk bosalana kadar - ama tur basina SERT sinirla.
                            // Sunucu bir isi kuyruktan dusuremezse while sonsuza
                            // kadar donerdi; sinir bunu kesin olarak engeller.
                            int sayac = 0;
                            while (sayac++ < 50 && FetchOneJob(baseUrl, key)) alindi = true;
                            if (alindi) SunucuHatirla(baseUrl);
                        }
                        catch { }   // bir sunucu erisilemezse digerleri denenmeye devam etsin
                    }
                }
            }
            catch (Exception ex) { Log("Is cekme dongusu: " + ex.Message); }
            Thread.Sleep(3000);
        }
    }

    // Keep-alive acik oldugu icin sunucu bosta kalan baglantiyi kapatmis olabilir;
    // .NET bunu ilk istekte anlar ve "baglanti kapatildi" hatasi verir. Bu, gecici
    // ve kendini duzelten bir durumdur: ikinci deneme taze baglanti acar. Bu yuzden
    // TEK SEFERLIK yeniden deneme yapiyoruz. Zaman asimi gibi gercek hatalarda
    // tekrar denemiyoruz - sadece dongunun bir sonraki turunu bekliyoruz.

    // Yuzde-kodlu adi coz. Eski sunucular kodsuz gonderir; "%" yoksa oldugu
    // gibi birakilir, boylece surum karisimi da calisir.
    static string CozKodlu(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('%') < 0) return s;
        try { return Uri.UnescapeDataString(s); } catch { return s; }
    }

    static bool FetchOneJob(string baseUrl, string key)
    {
        try { return FetchOneJobTek(baseUrl, key); }
        catch (WebException ex)
        {
            // NOT: istisna filtresi (catch ... when) bu derleyicide yok; durumu
            // govde icinde denetleyip ilgisiz hatalari yeniden firlatiyoruz.
            if (ex.Status != WebExceptionStatus.KeepAliveFailure &&
                ex.Status != WebExceptionStatus.ConnectionClosed) throw;
            Thread.Sleep(300);
            return FetchOneJobTek(baseUrl, key);   // taze baglantiyla bir kez daha
        }
    }

    static bool FetchOneJobTek(string baseUrl, string key)
    {
        // ADIM ADIM ZAMANLAMA: sorun sunucuda mi istemcide mi, gunlukten anlasilsin.
        //   1) yanit basligi beklenirken takiliyorsa  -> SUNUCU gec cevap veriyor
        //   2) indirme sirasinda takiliyorsa          -> AG / aktarim yavas
        //   3) onay (ACK) sirasinda takiliyorsa       -> SUNUCU mesgul
        // Her asamanin suresi ve aktarilan boyut kaydedilir.
        string asama = "baglanti kuruluyor";
        var kron = System.Diagnostics.Stopwatch.StartNew();
        long basligaKadar = 0, indirmeBitis = 0;
        long ham = 0, acilmis = 0;

        string qs = "?machine=" + Uri.EscapeDataString(Environment.MachineName) + "&key=" + Uri.EscapeDataString(key);
        var req = (HttpWebRequest)WebRequest.Create(baseUrl + "/api/jobs" + qs);
        req.Method = "GET";
        // ILK baglanti TLS dogrulamasi yuzunden 20+ saniye surebiliyor (bkz.
        // P360WebClient aciklamasi); eski 20 sn'lik sinir bu yuzden yetmiyordu.
        req.Timeout = 60000;
        req.ReadWriteTimeout = 120000;
        try
        {
            asama = "sunucudan yanit bekleniyor";
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                basligaKadar = kron.ElapsedMilliseconds;
                if (resp.StatusCode == HttpStatusCode.NoContent) return false;
                ham = resp.ContentLength;
                // Sunucu adlari YUZDE-KODLU gonderir (HTTP basliklari ASCII tasir;
                // Turkce harfler aksi halde bozuluyordu ve onay eslesmiyordu).
                // Kimlik AYNEN geri gonderilir - kodlu hali zaten URL-guvenli.
                string id = resp.Headers["X-Job-Id"] ?? "";
                string fnameHam = resp.Headers["X-File-Name"] ?? (id + ".pdf");
                string fname = CozKodlu(fnameHam);
                foreach (char c in Path.GetInvalidFileNameChars()) fname = fname.Replace(c, '_');
                string hedef = Path.Combine(jobsDir, fname);
                bool zatenVar = File.Exists(hedef) || File.Exists(Path.Combine(doneDir, fname));
                if (!zatenVar)
                {
                    asama = "is indiriliyor";
                    string tmp = hedef + ".tmp";
                    using (var gz = new System.IO.Compression.GZipStream(resp.GetResponseStream(),
                               System.IO.Compression.CompressionMode.Decompress))
                    using (var fs = File.Create(tmp))
                    {
                        var buf = new byte[81920]; int n;
                        while ((n = gz.Read(buf, 0, buf.Length)) > 0) { fs.Write(buf, 0, n); acilmis += n; }
                    }
                    File.Move(tmp, hedef);
                    indirmeBitis = kron.ElapsedMilliseconds;
                    Log(string.Format(
                        "Is alindi [HTTPS]: {0}  |  sunucu yaniti {1} ms  |  indirme {2} ms  |  {3} KB sikistirilmis -> {4} KB",
                        fname, basligaKadar, indirmeBitis - basligaKadar,
                        ham > 0 ? (ham / 1024) : 0, acilmis / 1024));
                }
                else indirmeBitis = kron.ElapsedMilliseconds;

                // ACK: kuyruktan dusur (dosya zaten alinmissa da onayla)
                bool onayOk = false;
                if (id.Length > 0)
                {
                    asama = "onay (ACK) gonderiliyor";
                    try
                    {
                        using (var wc = new P360WebClient())
                            wc.UploadString(baseUrl + "/api/jobs/done" + qs + "&id=" + id, "POST", "");
                        onayOk = true;
                        if (!zatenVar)
                            Log("Onay gonderildi: " + fname + "  (" + (kron.ElapsedMilliseconds - indirmeBitis) + " ms)");
                    }
                    catch (WebException wex)
                    {
                        // Sunucu 500 dondurduyse dosyayi silememis demektir. Bunu
                        // "OK" sanip donguye devam edersek ayni isi sonsuza kadar
                        // onaylariz (sahada saniyede 8 kez yasandi).
                        Log("ONAY REDDEDILDI: " + fname + " | " + wex.Message);
                    }
                }

                // SONSUZ DONGU KORUMASI: sunucu ayni isi arka arkaya veriyorsa
                // (silinemiyor), onay basarili gorunse bile dur. Bir sonraki
                // yoklama turunda tekrar denenir; bu arada gunluk dolmaz.
                if (zatenVar)
                {
                    if (sonTekrarEdenIs == fname)
                    {
                        if ((DateTime.Now - sonTekrarUyari).TotalMinutes >= 1)
                        {
                            sonTekrarUyari = DateTime.Now;
                            Log("UYARI: Sunucu ayni isi tekrar veriyor (kuyruktan dusuremiyor): " + fname
                              + " | onay " + (onayOk ? "kabul edildi" : "REDDEDILDI")
                              + " | sunucudaki C:/Print360/queue klasorunu ve izinlerini kontrol edin");
                        }
                        return false;
                    }
                    sonTekrarEdenIs = fname;
                    return false;
                }
                sonTekrarEdenIs = null;
                return onayOk;
            }
        }
        catch (WebException ex)
        {
            if (ex.Status != WebExceptionStatus.KeepAliveFailure &&
                ex.Status != WebExceptionStatus.ConnectionClosed)
            {
                // Hangi ASAMADA takildigini yaziyoruz: "sunucudan yanit bekleniyor"
                // goruluyorsa sorun SUNUCUDA, "is indiriliyor" goruluyorsa AG'da.
                // Ilk hata hemen yazilir; tekrarlari 2 dakikada bir (gunluk sismesin).
                bool ilk = (sonIsHatasi == DateTime.MinValue);
                if (ilk || (DateTime.Now - sonIsHatasi).TotalMinutes >= 2)
                {
                    sonIsHatasi = DateTime.Now;
                    Log(string.Format("IS ALINAMADI [{0}] asama: {1} | gecen {2} ms | durum: {3} | {4}",
                        baseUrl, asama, kron.ElapsedMilliseconds, ex.Status, ex.Message));
                }
                return false;
            }
            throw;   // gecici baglanti hatasi: cagiran tek sefer yeniden dener
        }
    }

    static void HeartbeatLoop()
    {
        Thread.Sleep(1500);   // tepsi simgesi kurulsun (ilk baglanti bildirimi balon olarak cikabilsin)
        bool lastOk = false, first = true;
        while (true)
        {
            var cfg = ReadIni();
            // Yazici saglik taramasi baglantidan BAGIMSIZ yapilir: sunucu yokken de
            // arayuzdeki yazici listesi dolu ve guncel kalir.
            try { sonYaziciRapor = YaziciSaglikRaporu(); }
            catch (Exception ex) { Log("Yazici taramasi: " + ex.Message); }
            // VC modu: heartbeat + yazici sagligi RDP kanalindan (HTTPS/IP/port gerekmez)
            if (VcAcik(cfg))
            {
                string prn2 = ""; try { prn2 = new System.Drawing.Printing.PrinterSettings().PrinterName; } catch { }
                string os2 = ""; try { os2 = Environment.OSVersion.VersionString; } catch { }
                VcYaz("HB", "machine=" + Environment.MachineName + ";user=" + Environment.UserName
                          + ";printer=" + prn2 + ";os=" + os2 + ";ver=" + Surum.V);
                try
                {
                    string r = sonYaziciRapor;
                    if (r.Length > 0) VcYaz("YAZICI", Environment.MachineName + "\n" + r);
                }
                catch (Exception ex) { Log("Yazici sagligi (VC): " + ex.Message); }
                // VC modunda gercek baglanti gostergesi: mstsc icindeki eklenti
                // vc-outbox'taki .msg dosyalarini tuketiyorsa kanal calisiyordur.
                // Dosyalar birikiyorsa (RDP kapali / eklenti yok) baglanti yok demektir.
                int bekleyen = 0;
                try { bekleyen = Directory.GetFiles(vcOutbox, "*.msg").Length; } catch { }
                bool vcCalisiyor = bekleyen <= 2;   // 1-2 mesaj gecici gecikme sayilir
                durumKanal = "RDP Virtual Channel (VC modu)";
                if (vcCalisiyor)
                {
                    durumZaman = DateTime.Now;
                    durumMetin = "Baglanti: RDP kanali uzerinden (HTTPS gerekmiyor)";
                    DurumBildir(true, "Print360 baglandi",
                        "RDP sanal kanali uzerinden calisiyor (ayar gerekmiyor).",
                        "RDP kanali uzerinden baglandi.");
                }
                else
                {
                    durumMetin = "RDP kanali yanit vermiyor (" + bekleyen + " mesaj bekliyor) - RDP oturumu acik mi?";
                    DurumBildir(false, "Print360 baglanti yok",
                        "RDP sanal kanali yanit vermiyor. RDP oturumu kapali olabilir.");
                }
                if (first) { Log("VC modu: onay/sayac/heartbeat RDP kanalindan gidiyor (HTTPS kapali)."); first = false; }
                hbTetik.Reset(); hbTetik.WaitOne(60000);   // RDP degisiminde erken uyanir
                continue;
            }
            // COKLU RDP: TUM acik sunuculara kalp atisi gonderilir. Kullanici ayni
            // anda 5 sunucuya baglanabilir; hepsi bu istemciyi "cevrimici" gormeli
            // ve is gonderebilmelidir.
            var sunucular = SunucuAdresleri(cfg);
            if (sunucular.Count > 0)
            {
                string prn = "";
                try { prn = new System.Drawing.Printing.PrinterSettings().PrinterName; } catch { }
                string os = "";
                try { os = Environment.OSVersion.VersionString; } catch { }
                string key = cfg.ContainsKey("ClientKey") ? cfg["ClientKey"].Trim() : "";

                var basarili = new List<string>();
                string sonHataMesaji = "";
                foreach (var baseUrl in sunucular)
                {
                    string url = baseUrl + "/api/heartbeat?machine=" + Uri.EscapeDataString(Environment.MachineName)
                               + "&printer=" + Uri.EscapeDataString(prn)
                               + "&user=" + Uri.EscapeDataString(Environment.UserName)
                               + "&os=" + Uri.EscapeDataString(os)
                               + "&key=" + Uri.EscapeDataString(key);
                    try
                    {
                        using (var wc = new P360WebClient()) wc.UploadString(url, "POST", "");
                        basarili.Add(baseUrl);
                        SunucuHatirla(baseUrl);
                        try
                        {
                            string rapor = sonYaziciRapor;
                            if (rapor.Length > 0)
                                using (var wc = new P360WebClient())
                                {
                                    wc.Encoding = Encoding.UTF8;
                                    wc.UploadString(baseUrl + "/api/printers?machine=" + Uri.EscapeDataString(Environment.MachineName)
                                                  + "&key=" + Uri.EscapeDataString(key), "POST", rapor);
                                }
                        }
                        catch { }
                    }
                    catch (Exception ex) { sonHataMesaji = ex.Message; }
                }

                if (basarili.Count > 0)
                {
                    if (!lastOk || first)
                        Log("BAGLANTI: " + basarili.Count + "/" + sunucular.Count + " sunucuya baglandi ("
                            + string.Join(", ", basarili.ToArray()) + ")");
                    lastOk = true;
                    durumZaman = DateTime.Now;
                    sunucuBagli = basarili.Count;   // tepsi ipucu bunu gosterir
                    durumKanal = basarili.Count == 1 ? basarili[0]
                               : basarili.Count + " sunucu: " + string.Join(" , ", basarili.ToArray());
                    durumMetin = "Baglanti: " + basarili.Count + " sunucuya baglandi"
                               + (sunucular.Count > basarili.Count
                                  ? " (" + (sunucular.Count - basarili.Count) + " sunucuda Print360 yok)" : "");
                    // Balonda KISA bilgi: "Sunucuya baglandi" (+ birden fazlaysa sayi)
                    DurumBildir(true, "Print360 baglandi",
                        basarili.Count + " sunucuya baglandi: " + string.Join(", ", basarili.ToArray()),
                        basarili.Count == 1 ? "Sunucuya baglandi." : basarili.Count + " sunucuya baglandi.");
                }
                else
                {
                    if (lastOk || first)
                        Log("BAGLANTI: Hicbir sunucuya ulasilamadi (" + sunucular.Count + " adres denendi): " + sonHataMesaji);
                    lastOk = false;
                    sunucuBagli = 0;
                    durumKanal = sunucular.Count + " adres denendi: " + string.Join(" , ", sunucular.ToArray());
                    if (rdpAcik)
                    {
                        durumMetin = "UYARI: Acik RDP sunucularinin hicbirinde Print360 yanit vermiyor.";
                        DurumBildir(false, "Print360 sunucusu bulunamadi",
                            "RDP baglantisi var ama hicbir sunucuda Print360 yanit vermiyor.\r\n"
                            + "Sunucuya Print360-Server-Setup.exe kurulu mu?");
                    }
                    else
                    {
                        durumMetin = "Sunucuya ulasilamiyor: " + sonHataMesaji;
                        DurumBildir(false, "Print360 baglanti yok", "Sunucuya ulasilamiyor.");
                    }
                }
                first = false;
            }
            else
            {
                durumKanal = "HTTPS (sunucu otomatik araniyor)";
                durumMetin = "RDP baglantisi bekleniyor - sunucu henuz bulunamadi";
                DurumBildir(false, "Print360 baglanti yok",
                    "Sunucu bulunamadi. RDP baglantisi acildiginda otomatik baglanacak.");
            }
            hbTetik.Reset(); hbTetik.WaitOne(60000);   // RDP acilinca/kapaninca erken uyanir
        }
    }

    // Yerel yazicilarin sagligi (WMI Win32_Printer + Win32_PrintJob).
    // Satir formati: "yazici","durum","hata","kuyruk"
    static string YaziciSaglikRaporu()
    {
        var sb = new StringBuilder();
        try
        {
            // Kuyruk uzunluklari (tek sorgu; Name = "Yazici, IsNo")
            var kuyruk = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var js = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_PrintJob"))
                    foreach (System.Management.ManagementObject j in js.Get())
                    {
                        string n = Convert.ToString(j["Name"]);
                        int vi = n.LastIndexOf(',');
                        string yz = vi > 0 ? n.Substring(0, vi).Trim() : n;
                        int c; kuyruk.TryGetValue(yz, out c); kuyruk[yz] = c + 1;
                    }
            }
            catch { }

            using (var ps = new System.Management.ManagementObjectSearcher(
                       "SELECT Name, PrinterStatus, DetectedErrorState, WorkOffline FROM Win32_Printer"))
                foreach (System.Management.ManagementObject p in ps.Get())
                {
                    string ad = Convert.ToString(p["Name"]);
                    if (SanalYazici(ad)) continue;
                    int st = 0; try { st = Convert.ToInt32(p["PrinterStatus"]); } catch { }
                    int err = 0; try { err = Convert.ToInt32(p["DetectedErrorState"]); } catch { }
                    bool off = false; try { off = p["WorkOffline"] != null && (bool)p["WorkOffline"]; } catch { }
                    int q; kuyruk.TryGetValue(ad, out q);
                    sb.Append('"').Append(ad.Replace("\"", "\"\"")).Append("\",\"")
                      .Append(YaziciDurum(st, off)).Append("\",\"")
                      .Append(YaziciHata(err)).Append("\",\"").Append(q).Append("\"\r\n");
                }
        }
        catch (Exception ex) { Log("Yazici taramasi: " + ex.Message); }
        return sb.ToString();
    }

    static bool SanalYazici(string ad)
    {
        string[] sanal = { "Microsoft Print to PDF", "Microsoft XPS", "OneNote", "Fax", "Print360", "PDF24", "Foxit" };
        foreach (var s in sanal)
            if (ad.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    static string YaziciDurum(int st, bool off)
    {
        if (off) return "Cevrimdisi";
        switch (st)
        {
            case 3: return "Hazir";
            case 4: return "Yazdiriyor";
            case 5: return "Isiniyor";
            case 6: return "Durduruldu";
            case 7: return "Cevrimdisi";
            default: return "Bilinmiyor";
        }
    }

    static string YaziciHata(int err)
    {
        switch (err)
        {
            case 0: case 1: case 2: return "";
            case 3: return "Kagit az";
            case 4: return "Kagit bitti";
            case 5: return "Toner az";
            case 6: return "Toner bitti";
            case 7: return "Kapak acik";
            case 8: return "Kagit sikismasi";
            case 9: return "Cevrimdisi";
            case 10: return "Servis gerekli";
            case 11: return "Cikti tepsisi dolu";
            default: return "Hata #" + err;
        }
    }

    static void PushToServer(string csvLine)
    {
        var cfg = ReadIni();
        // VC modu: sayac/onay HTTPS yerine RDP kanalindan (ayar gerekmez)
        if (VcAcik(cfg)) { VcYaz("SAYAC", csvLine); return; }
        string baseUrl = BaseUrl(cfg);
        if (baseUrl == null) return;
        string key = cfg.ContainsKey("ClientKey") ? cfg["ClientKey"].Trim() : "";
        string url = baseUrl + "/api/printed?machine=" + Uri.EscapeDataString(Environment.MachineName)
                   + "&key=" + Uri.EscapeDataString(key);
        try
        {
            using (var wc = new P360WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.UploadString(url, "POST", csvLine);
            }
        }
        catch (Exception ex)
        {
            // Sunucuya ulasilamazsa sorun degil: kayit yerelde durur, RDP kanalindan geri cekilir
            Log("Sunucuya iletilemedi (" + url + "): " + ex.Message);
        }
    }

    // done klasorunde en fazla 200 dosya tut
    static void Prune()
    {
        var files = new DirectoryInfo(doneDir).GetFiles().OrderByDescending(f => f.CreationTime).Skip(200);
        foreach (var f in files) { try { f.Delete(); } catch { } }
    }

    static Dictionary<string, string> ReadIni()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(iniFile)) return d;
        foreach (var line in File.ReadAllLines(iniFile))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith(";") || t.StartsWith("#")) continue;
            int i = t.IndexOf('=');
            if (i > 0) d[t.Substring(0, i).Trim()] = t.Substring(i + 1).Trim();
        }
        return d;
    }


    // BAGLANTI MALIYETI OLCULDU: TCP el sikismasi ~25 ms, ama ILK HTTPS istegi
    // ~17-27 SANIYE suruyor. Sebep TLS el sikismasindaki sertifika zinciri
    // dogrulamasi: kendinden imzali sertifikanin iptal listesine ulasilamiyor ve
    // Windows zaman asimini bekliyor. Sonraki istekler ~70 ms - cunku baglanti
    // ve dogrulama sonucu yeniden kullaniliyor.
    //
    // Bu yuzden KEEP-ALIVE ACIK olmalidir: bedel bir kez odenir. Baglantiyi her
    // istekte kapatmak, her yoklamada 20+ saniyelik el sikismasi demek olur ve
    // istekler zaman asimina ugrar. (Bir sure oyle denendi, isler inmedi.)
    //
    // Keep-alive'in bilinen riski, sunucunun bosta kalan baglantiyi kapatmasi ve
    // .NET'in onu hala canli sanmasidir; bu FetchOneJob icinde tek seferlik
    // yeniden deneme ile karsilanir.
    class P360WebClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri adres)
        {
            var r = base.GetWebRequest(adres);
            var h = r as HttpWebRequest;
            if (h != null) { h.Timeout = 60000; h.ReadWriteTimeout = 120000; }
            return r;
        }
    }

    // Gunluk dosyasi 5 MB'i gecince .1 uzantisiyla devreder; iki kusak tutulur.
    // Boylece yogun sunucularda gunluk suresiz buyuyup diski doldurmaz.
    const long LOG_SINIR = 5 * 1024 * 1024;

    static void LogDevret(string dosya)
    {
        try
        {
            var fi = new FileInfo(dosya);
            if (!fi.Exists || fi.Length < LOG_SINIR) return;
            string eski = dosya + ".1";
            if (File.Exists(eski)) File.Delete(eski);
            File.Move(dosya, eski);
        }
        catch { }
    }

    static void Log(string msg)
    {
        try { LogDevret(logFile); File.AppendAllText(logFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + "\r\n"); }
        catch { }
    }

    // ==================== ARAYUZ ====================
    // Tepsi simgesi + durum penceresi: baglanti durumu, yazici listesi, gorevler.
    static System.Windows.Forms.NotifyIcon tepsi;
    static DurumPenceresi pencere;
    static System.Windows.Forms.Form senkron;   // UI thread'ine Invoke koprusu

    static System.Drawing.Icon SimgeYukle()
    {
        try
        {
            string ico = Path.Combine(baseDir, "Print360.ico");
            if (File.Exists(ico)) return new System.Drawing.Icon(ico);
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    // Tepsi simgesinin ipucu: fareyle uzerine gelindiginde CANLI durum gorunur.
    //   1. satir : bagli mi, kac sunucuya
    //   2. satir : hangi yaziciya basacak
    //   3. satir : bekleyen is varsa sayisi, yoksa son temas saati
    // NotifyIcon.Text .NET'te EN FAZLA 63 karakter kabul eder (asilirsa istisna
    // atar). Bu yuzden sabit satirlar once yazilir, yazici adi kalan yere gore
    // kisaltilir. UI is parcacigindan cagrilmalidir.
    const int IPUCU_SINIR = 63;

    static void TepsiIpucuGuncelle()
    {
        var t = tepsi;
        if (t == null) return;
        try
        {
            string s1 = "Print360 - " + (durumOk ? "BAGLI" : "BAGLANTI YOK");
            if (durumOk && sunucuBagli > 1) s1 += " (" + sunucuBagli + " sunucu)";

            int bekleyen = 0;
            try { bekleyen = Directory.GetFiles(jobsDir).Length; } catch { }

            string s3;
            // Kisa tutuluyor: her karakter yazici adindan calinir (bkz. IPUCU_SINIR)
            if (bekleyen > 0) s3 = "Bekleyen: " + bekleyen + " is";
            else if (durumOk && durumZaman > DateTime.MinValue) s3 = "Son temas: " + durumZaman.ToString("HH:mm");
            else if (rdpAcik) s3 = "Sunucu araniyor";
            else s3 = "RDP bekleniyor";

            string yazici = "";
            try { var l = KisiselYaziciListesi(); if (l.Count > 0) yazici = l[0]; } catch { }
            if (yazici.Length == 0) yazici = "(secilmedi)";

            // Yazici satirina kalan yer: sinir - digerleri - iki satir sonu - etiket
            int yer = IPUCU_SINIR - s1.Length - s3.Length - 2 - 8;   // "Yazici: " = 8
            if (yer < 6) { IpucuYaz(t, s1 + "\n" + s3); return; }    // sigmiyorsa yazici satirini atla
            if (yazici.Length > yer) yazici = yazici.Substring(0, yer - 2) + "..";
            IpucuYaz(t, s1 + "\nYazici: " + yazici + "\n" + s3);
        }
        catch { }
    }

    static void IpucuYaz(System.Windows.Forms.NotifyIcon t, string s)
    {
        if (s.Length > IPUCU_SINIR) s = s.Substring(0, IPUCU_SINIR);
        try { t.Text = s; } catch { }
    }

    static void ArayuzuCalistir()
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        tepsi = new System.Windows.Forms.NotifyIcon();
        tepsi.Icon = SimgeYukle();
        tepsi.Text = "Print360 istemci";
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Durum penceresi", null, delegate { PencereAc(); });
        menu.Items.Add("Yazdirma klasoru", null, delegate {
            try { Process.Start("explorer.exe", jobsDir); } catch { } });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Cikis", null, delegate {
            tepsi.Visible = false; Environment.Exit(0); });
        tepsi.ContextMenuStrip = menu;
        tepsi.DoubleClick += delegate { PencereAc(); };
        tepsi.Visible = true;
        // Baslangic balonu yok: ilk baglanti sonucunu HeartbeatLoop -> DurumBildir bildirir.

        // UI thread'ine is aktarmak icin gizli senkron penceresi (handle bu thread'e ait)
        senkron = new System.Windows.Forms.Form();
        senkron.ShowInTaskbar = false;
        senkron.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
        senkron.Size = new System.Drawing.Size(1, 1);
        senkron.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        senkron.Location = new System.Drawing.Point(-32000, -32000);
        var zorla = senkron.Handle;   // handle olussun (Invoke icin sart)

        // Tepsi ipucu duzenli tazelensin: kullanici fareyle uzerine geldiginde
        // bekleyen is sayisi ve son temas saati guncel gorunsun. Zamanlayici bu
        // (UI) is parcaciginda calisir; tepsi.Text dogrudan yazilabilir.
        var ipucuSaati = new System.Windows.Forms.Timer();
        ipucuSaati.Interval = 4000;
        ipucuSaati.Tick += delegate { TepsiIpucuGuncelle(); };
        ipucuSaati.Start();
        TepsiIpucuGuncelle();

        // Ikinci kez calistirilirsa (kisayol) mevcut ornek pencereyi acsin
        new Thread(GosterSinyaliniDinle) { IsBackground = true }.Start();

        System.Windows.Forms.Application.Run(new System.Windows.Forms.ApplicationContext());
    }

    const string GOSTER_OLAY = "Print360ClientShowUI";

    static void GosterSinyaliniDinle()
    {
        try
        {
            using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset, GOSTER_OLAY))
                while (true)
                {
                    ev.WaitOne();
                    // Pencere UI thread'inde olusturulmali -> senkron kopruden Invoke
                    try
                    {
                        var s = senkron;
                        if (s != null && s.IsHandleCreated) s.BeginInvoke((Action)PencereAc);
                    }
                    catch { }
                }
        }
        catch { }
    }

    // Calisan ornek varsa ona "pencereyi ac" sinyali gonder (true = gonderildi).
    static bool GosterSinyaliGonder()
    {
        try
        {
            EventWaitHandle ev;
            if (EventWaitHandle.TryOpenExisting(GOSTER_OLAY, out ev))
            {
                using (ev) { ev.Set(); return true; }
            }
        }
        catch { }
        return false;
    }

    static void PencereAc()
    {
        try
        {
            if (pencere == null || pencere.IsDisposed) pencere = new DurumPenceresi();
            pencere.Show();
            if (pencere.WindowState == System.Windows.Forms.FormWindowState.Minimized)
                pencere.WindowState = System.Windows.Forms.FormWindowState.Normal;
            pencere.BringToFront();
            pencere.Activate();
        }
        catch (Exception ex) { Log("Durum penceresi acilamadi: " + ex.Message); }
    }

    // printed.csv satirini alanlara ayir ("a","b",...)
    static string[] CsvAyir(string line)
    {
        var f = new List<string>();
        var cur = new StringBuilder();
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else if (c == '"') q = false;
                else cur.Append(c);
            }
            else
            {
                if (c == '"') q = true;
                else if (c == ',') { f.Add(cur.ToString()); cur.Length = 0; }
                else cur.Append(c);
            }
        }
        f.Add(cur.ToString());
        return f.ToArray();
    }

    class DurumPenceresi : System.Windows.Forms.Form
    {
        System.Windows.Forms.Panel ustPanel;
        System.Windows.Forms.Label lblDurum, lblKanal;
        System.Windows.Forms.ListView lvGorev, lvYazici;
        System.Windows.Forms.Label lblHedef;   // "Hedef siraniz: 1) ... 2) ..."
        System.Windows.Forms.TextBox txtLog;
        System.Windows.Forms.Timer zamanlayici;
        static readonly System.Drawing.Color Mavi = System.Drawing.Color.FromArgb(0x3B, 0x82, 0xF6);
        static readonly System.Drawing.Color Indigo = System.Drawing.Color.FromArgb(0x63, 0x66, 0xF1);
        // Bagli durumu: yesil gradyan (emerald-600 -> emerald-800)
        static readonly System.Drawing.Color Yesil = System.Drawing.Color.FromArgb(0x15, 0x9A, 0x54);
        static readonly System.Drawing.Color YesilKoyu = System.Drawing.Color.FromArgb(0x0B, 0x6B, 0x3A);

        public DurumPenceresi()
        {
            Text = "Print360 - Istemci Durumu";
            ClientSize = new System.Drawing.Size(780, 560);
            MinimumSize = new System.Drawing.Size(660, 460);
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            BackColor = System.Drawing.Color.White;
            Font = new System.Drawing.Font("Segoe UI", 9f);
            try { Icon = SimgeYukle(); } catch { }

            // --- Ust durum paneli (gradyan) ---
            ustPanel = new System.Windows.Forms.Panel();
            ustPanel.Dock = System.Windows.Forms.DockStyle.Top;
            ustPanel.Height = 88;
            ustPanel.Paint += delegate (object s, System.Windows.Forms.PaintEventArgs e)
            {
                // BAGLI = yesil, baglanti yok = kirmizi: durum bir bakista anlasilsin
                var c1 = durumOk ? Yesil : System.Drawing.Color.FromArgb(0xDC, 0x26, 0x26);
                var c2 = durumOk ? YesilKoyu : System.Drawing.Color.FromArgb(0x99, 0x1B, 0x1B);
                using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(
                    ustPanel.ClientRectangle, c1, c2, 0f))
                    e.Graphics.FillRectangle(b, ustPanel.ClientRectangle);
            };
            lblDurum = new System.Windows.Forms.Label();
            lblDurum.ForeColor = System.Drawing.Color.White;
            lblDurum.Font = new System.Drawing.Font("Segoe UI", 12f, System.Drawing.FontStyle.Bold);
            lblDurum.SetBounds(18, 16, 740, 26);
            lblDurum.BackColor = System.Drawing.Color.Transparent;
            lblKanal = new System.Windows.Forms.Label();
            lblKanal.ForeColor = System.Drawing.Color.FromArgb(225, 235, 255);
            lblKanal.SetBounds(18, 46, 740, 34);
            lblKanal.BackColor = System.Drawing.Color.Transparent;
            ustPanel.Controls.Add(lblDurum);
            ustPanel.Controls.Add(lblKanal);

            // --- Sekmeler ---
            var tab = new System.Windows.Forms.TabControl();
            tab.Dock = System.Windows.Forms.DockStyle.Fill;
            tab.Padding = new System.Drawing.Point(12, 6);

            lvGorev = YeniListe(new string[] { "Zaman", "Belge", "Yazici", "Durum" },
                                new int[] { 130, 300, 200, 110 });
            var tpG = new System.Windows.Forms.TabPage("Gorevler");
            tpG.BackColor = System.Drawing.Color.White;
            tpG.Controls.Add(lvGorev);

            lvYazici = YeniListe(new string[] { "Yazici", "Durum", "Hata", "Kuyruk" },
                                 new int[] { 330, 140, 160, 70 });
            var tpY = new System.Windows.Forms.TabPage("Yazicilar");
            tpY.BackColor = System.Drawing.Color.White;

            // --- KISIYE OZEL YAZICI SECIMI ---
            // Makinede birden fazla yazici olabilir; is "kafasina gore" rastgele
            // bir yaziciya gitmesin diye her Windows kullanicisi KENDI hedefini secer.
            var secPanel = new System.Windows.Forms.Panel();
            secPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            secPanel.Height = 64;
            secPanel.BackColor = System.Drawing.Color.FromArgb(0xF3, 0xF6, 0xFB);

            lblHedef = new System.Windows.Forms.Label();
            lblHedef.SetBounds(12, 8, 740, 18);
            lblHedef.Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold);

            // Secili listedeki yazici adini al ("(varsayilan)" etiketi olmadan)
            Func<string> seciliAd = delegate
            {
                if (lvYazici.SelectedItems.Count == 0) return null;
                string ad = lvYazici.SelectedItems[0].Text;
                int p = ad.IndexOf("   (");
                return p > 0 ? ad.Substring(0, p) : ad;
            };
            Action uyar = delegate
            {
                System.Windows.Forms.MessageBox.Show("Once listeden bir yazici secin.", "Print360",
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
            };

            var btnBirinci = new System.Windows.Forms.Button();
            btnBirinci.Text = "1. SIRA yap (varsayilanim)";
            btnBirinci.SetBounds(12, 30, 210, 26);
            btnBirinci.Click += delegate
            {
                string ad = seciliAd(); if (ad == null) { uyar(); return; }
                KisiselYaziciBirinciYap(ad); Yenile();
            };

            var btnYedek = new System.Windows.Forms.Button();
            btnYedek.Text = "Yedek olarak ekle";
            btnYedek.SetBounds(230, 30, 150, 26);
            btnYedek.Click += delegate
            {
                string ad = seciliAd(); if (ad == null) { uyar(); return; }
                KisiselYaziciYedekEkle(ad); Yenile();
            };

            var btnCikar = new System.Windows.Forms.Button();
            btnCikar.Text = "Siradan cikar";
            btnCikar.SetBounds(388, 30, 120, 26);
            btnCikar.Click += delegate
            {
                string ad = seciliAd(); if (ad == null) { uyar(); return; }
                KisiselYaziciCikar(ad); Yenile();
            };

            // Windows'un varsayilan yazicisina geri don (tek tikla sifirlama)
            var btnVars = new System.Windows.Forms.Button();
            btnVars.Text = "Windows varsayilanina don";
            btnVars.SetBounds(516, 30, 190, 26);
            btnVars.Click += delegate
            {
                string v = WindowsVarsayilanYazici();
                if (v.Length == 0)
                {
                    System.Windows.Forms.MessageBox.Show(
                        "Windows varsayilan yazicisi bulunamadi (veya sanal bir yazici).",
                        "Print360", System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                    return;
                }
                KisiselYaziciListesiYaz(new List<string> { v });
                Yenile();
            };

            secPanel.Controls.Add(lblHedef);
            secPanel.Controls.Add(btnBirinci);
            secPanel.Controls.Add(btnYedek);
            secPanel.Controls.Add(btnCikar);
            secPanel.Controls.Add(btnVars);
            tpY.Controls.Add(lvYazici);
            tpY.Controls.Add(secPanel);

            txtLog = new System.Windows.Forms.TextBox();
            txtLog.Multiline = true; txtLog.ReadOnly = true;
            txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            txtLog.Dock = System.Windows.Forms.DockStyle.Fill;
            txtLog.BackColor = System.Drawing.Color.White;
            txtLog.Font = new System.Drawing.Font("Consolas", 8.5f);
            var tpL = new System.Windows.Forms.TabPage("Gunluk");
            tpL.Controls.Add(txtLog);

            tab.TabPages.Add(tpG); tab.TabPages.Add(tpY); tab.TabPages.Add(tpL);

            // --- Alt cubuk ---
            var alt = new System.Windows.Forms.Panel();
            alt.Dock = System.Windows.Forms.DockStyle.Bottom;
            alt.Height = 46;
            var btnYenile = new System.Windows.Forms.Button();
            btnYenile.Text = "Yenile"; btnYenile.SetBounds(12, 9, 92, 28);
            btnYenile.Click += delegate { Yenile(); };
            var btnKlasor = new System.Windows.Forms.Button();
            btnKlasor.Text = "Yazdirma klasoru"; btnKlasor.SetBounds(112, 9, 140, 28);
            btnKlasor.Click += delegate { try { Process.Start("explorer.exe", jobsDir); } catch { } };
            var lblSurum = new System.Windows.Forms.Label();
            lblSurum.Text = "Print360 v" + Surum.Etiket + "  -  " + Environment.MachineName
                          + "   |   Ucretsiz surum, para ile satilamaz";
            lblSurum.ForeColor = System.Drawing.Color.Gray;
            lblSurum.SetBounds(266, 6, 500, 16);

            // Gelistirici + tiklanabilir e-posta ve LinkedIn baglantilari
            var lnkMail = new System.Windows.Forms.LinkLabel();
            string mailMetin = "Gelistirici: " + GELISTIRICI + "  -  " + EPOSTA + "  -  LinkedIn";
            lnkMail.Text = mailMetin;
            lnkMail.Links.Clear();
            lnkMail.Links.Add(mailMetin.IndexOf(EPOSTA), EPOSTA.Length, "mail");
            lnkMail.Links.Add(mailMetin.LastIndexOf("LinkedIn"), "LinkedIn".Length, "linkedin");
            lnkMail.ForeColor = System.Drawing.Color.Gray;
            lnkMail.LinkColor = System.Drawing.Color.FromArgb(0x3B, 0x82, 0xF6);
            lnkMail.ActiveLinkColor = System.Drawing.Color.FromArgb(0x63, 0x66, 0xF1);
            lnkMail.SetBounds(266, 23, 505, 18);
            lnkMail.LinkClicked += delegate (object s, System.Windows.Forms.LinkLabelLinkClickedEventArgs e)
            {
                if (Convert.ToString(e.Link.LinkData) == "linkedin") LinkedInAc();
                else MailAc();
            };
            alt.Controls.Add(btnYenile); alt.Controls.Add(btnKlasor);
            alt.Controls.Add(lblSurum); alt.Controls.Add(lnkMail);

            Controls.Add(tab); Controls.Add(alt); Controls.Add(ustPanel);

            // Kapatinca uygulamadan cikma - tepsiye in
            FormClosing += delegate (object s, System.Windows.Forms.FormClosingEventArgs e)
            {
                if (e.CloseReason == System.Windows.Forms.CloseReason.UserClosing)
                { e.Cancel = true; Hide(); }
            };

            zamanlayici = new System.Windows.Forms.Timer();
            zamanlayici.Interval = 2000;
            zamanlayici.Tick += delegate { Yenile(); };
            zamanlayici.Start();
            Yenile();
        }

        static System.Windows.Forms.ListView YeniListe(string[] basliklar, int[] genislikler)
        {
            var lv = new System.Windows.Forms.ListView();
            lv.View = System.Windows.Forms.View.Details;
            lv.FullRowSelect = true;
            lv.GridLines = false;
            lv.Dock = System.Windows.Forms.DockStyle.Fill;
            lv.BorderStyle = System.Windows.Forms.BorderStyle.None;
            lv.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
            for (int i = 0; i < basliklar.Length; i++)
                lv.Columns.Add(basliklar[i], genislikler[i]);
            return lv;
        }

        void Yenile()
        {
            try
            {
                // --- Durum basligi ---
                bool vc = false;
                try { vc = VcAcik(ReadIni()); } catch { }
                string yas = durumZaman == default(DateTime)
                    ? "henuz temas yok"
                    : "son temas: " + durumZaman.ToString("HH:mm:ss");
                lblDurum.Text = (durumOk ? "BAGLI" : "BAGLI DEGIL") + "   -   " + Environment.MachineName
                              + (rdpAcik ? "   |   RDP oturumu ACIK" + (sonRdpSunucu != null ? " (" + sonRdpSunucu + ")" : "")
                                         : "   |   RDP oturumu yok");
                lblKanal.Text = (durumKanal.Length > 0 ? durumKanal : (vc ? "RDP Virtual Channel (VC modu)" : "HTTPS"))
                              + "\r\n" + durumMetin + "   (" + yas + ")";
                ustPanel.Invalidate();

                YenileGorevler();
                YenileYazicilar();
                YenileGunluk();
            }
            catch { }
        }

        void YenileGorevler()
        {
            var satirlar = new List<string[]>();
            // Bekleyen isler (jobs klasoru)
            try
            {
                foreach (var f in Directory.GetFiles(jobsDir)
                         .Where(x => !x.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)))
                {
                    var fi = new FileInfo(f);
                    satirlar.Add(new string[] { fi.CreationTime.ToString("dd.MM HH:mm:ss"),
                                                GorunenBelgeAdi(fi.Name), "-", "BEKLIYOR" });
                }
            }
            catch { }
            // Basilan/hatali isler (printed.csv, son kayitlar)
            try
            {
                if (File.Exists(printedCsv))
                {
                    var tum = File.ReadAllLines(printedCsv);
                    int bas = Math.Max(0, tum.Length - 200);
                    for (int i = tum.Length - 1; i >= bas; i--)
                    {
                        if (tum[i].Trim().Length == 0) continue;
                        var a = CsvAyir(tum[i]);
                        if (a.Length < 5) continue;
                        string zaman = a[0].Length >= 16 ? a[0].Substring(5, 11) : a[0];
                        satirlar.Add(new string[] { zaman, GorunenBelgeAdi(a[2]), a[3], a[4] });
                    }
                }
            }
            catch { }

            lvGorev.BeginUpdate();
            lvGorev.Items.Clear();
            foreach (var s in satirlar)
            {
                var it = new System.Windows.Forms.ListViewItem(s);
                string d = s[3].ToUpperInvariant();
                if (d.StartsWith("HATA")) it.ForeColor = System.Drawing.Color.Firebrick;
                else if (d.StartsWith("IPTAL")) it.ForeColor = System.Drawing.Color.DarkOrange;
                else if (d.StartsWith("BEKLIYOR")) it.ForeColor = System.Drawing.Color.RoyalBlue;
                else it.ForeColor = System.Drawing.Color.FromArgb(0x15, 0x7F, 0x3C);
                lvGorev.Items.Add(it);
            }
            lvGorev.EndUpdate();
        }

        void YenileYazicilar()
        {
            // WMI raporu (heartbeat'te uretilir) -> durum/hata/kuyruk sozlugu
            var bilgi = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var ln in (sonYaziciRapor ?? "").Split(new string[] { "\r\n", "\n" },
                                                                StringSplitOptions.RemoveEmptyEntries))
                {
                    var a = CsvAyir(ln);
                    if (a.Length >= 4) bilgi[a[0]] = new string[] { a[1], a[2], a[3] };
                }
            }
            catch { }

            string varsayilan = "";
            try { varsayilan = new System.Drawing.Printing.PrinterSettings().PrinterName; } catch { }

            // Kullanicinin oncelik sirasi
            var sira = KisiselYaziciListesi();
            if (lblHedef != null)
            {
                if (sira.Count == 0)
                {
                    string wv = WindowsVarsayilanYazici();
                    lblHedef.Text = wv.Length > 0
                        ? "VARSAYILAN YAZICINIZ: " + wv + "   (Windows varsayilani - degistirmek icin asagidan secin)"
                        : "VARSAYILAN YAZICI YOK - lutfen listeden bir yazici secip '1. SIRA yap' deyin.";
                }
                else
                {
                    var sb2 = new StringBuilder("VARSAYILAN YAZICINIZ (" + Environment.UserName + "): ");
                    sb2.Append(sira[0]);
                    if (sira.Count > 1)
                    {
                        sb2.Append("      Yedekler: ");
                        for (int i = 1; i < sira.Count; i++)
                            sb2.Append(i > 1 ? " > " : "").Append(i + 1).Append(") ").Append(sira[i]);
                    }
                    lblHedef.Text = sb2.ToString();
                }
            }

            lvYazici.BeginUpdate();
            lvYazici.Items.Clear();
            try
            {
                foreach (string ad in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                {
                    // Sanal yazicilar saglik taramasina girmez (durumu izlenmez)
                    string durum = SanalYazici(ad) ? "Sanal" : "-", hata = "", kuyruk = "";
                    if (bilgi.ContainsKey(ad))
                    { durum = bilgi[ad][0]; hata = bilgi[ad][1]; kuyruk = bilgi[ad][2]; }
                    bool vars = ad.Equals(varsayilan, StringComparison.OrdinalIgnoreCase);

                    // Kullanicinin sirasindaki yeri (1. sira = benim varsayilanim)
                    int sn = -1;
                    for (int i = 0; i < sira.Count; i++)
                        if (string.Equals(sira[i], ad, StringComparison.OrdinalIgnoreCase)) { sn = i + 1; break; }
                    string etiket = ad
                        + (sn == 1 ? "   [1. SIRA - benim yazicim]" : sn > 0 ? "   [" + sn + ". yedek]" : "")
                        + (vars && sn < 0 ? "   (Windows varsayilani)" : "");

                    var it = new System.Windows.Forms.ListViewItem(new string[] { etiket, durum, hata, kuyruk });
                    if (hata.Length > 0 || durum == "Cevrimdisi" || durum == "Durduruldu")
                        it.ForeColor = System.Drawing.Color.Firebrick;
                    else if (sn == 1)
                    {
                        it.ForeColor = System.Drawing.Color.FromArgb(0x0B, 0x6B, 0x3A);
                        it.Font = new System.Drawing.Font(lvYazici.Font, System.Drawing.FontStyle.Bold);
                    }
                    else if (vars) it.Font = new System.Drawing.Font(lvYazici.Font, System.Drawing.FontStyle.Bold);
                    lvYazici.Items.Add(it);
                }
            }
            catch (Exception ex)
            {
                lvYazici.Items.Add(new System.Windows.Forms.ListViewItem(
                    new string[] { "Yazicilar okunamadi: " + ex.Message, "", "", "" }));
            }
            lvYazici.EndUpdate();
        }

        void YenileGunluk()
        {
            try
            {
                if (!File.Exists(logFile)) return;
                string[] tum;
                using (var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                    tum = sr.ReadToEnd().Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                int bas = Math.Max(0, tum.Length - 200);
                var sb = new StringBuilder();
                for (int i = tum.Length - 1; i >= bas; i--) sb.AppendLine(tum[i]);
                string yeni = sb.ToString();
                if (txtLog.Text != yeni) txtLog.Text = yeni;
            }
            catch { }
        }
    }
}
