// ============================================================
//  Print360 - RDP Yazdirma ve Yonetim Cozumu
//  Gelistirici : Omer CARNACAR  <omer.carnacar@outlook.com.tr>
//  LinkedIn    : https://www.linkedin.com/in/omercarnacar/
//  Lisans      : UCRETSIZ SURUM - para ile satilamaz (bkz. LICENSE)
//  Telif       : (c) 2026 Omer CARNACAR
// ============================================================
//  NATIVE YAPILANDIRICI  (PowerShell GEREKTIRMEZ)
//
//  Kurulum isini Setup.exe'nin icinden bu program yapar. Eskiden
//  Install-Server.ps1 / Install-Client.ps1 calisiyordu; PowerShell
//  yurutme ilkesi, eksik surum, gizli konsol ve hata yakalama sorunlari
//  cikariyordu. Artik her sey .NET + Windows API ile yapilir.
//
//  Kullanim (Setup.exe icinden cagrilir, sessiz calisir):
//    Print360.Setup.exe --sunucu  [secenekler]
//    Print360.Setup.exe --istemci [secenekler]
//
//  Cikis kodu: 0 = basarili, >0 = hata (Setup kullaniciya gosterir)
//  Gunluk    : C:\Print360\logs\kurulum.log
// ============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

static class Kurulum
{
    const string BASE = @"C:\Print360";
    static readonly List<string> _uyarilar = new List<string>();
    static StreamWriter _log;

    // ---------------- Giris ----------------
    static int Main(string[] args)
    {
        var p = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string mod = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            string k = args[i].Substring(2);
            if (k.Equals("sunucu", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("istemci", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("kaldir-sunucu", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("kaldir-istemci", StringComparison.OrdinalIgnoreCase)) { mod = k.ToLowerInvariant(); continue; }
            // "--anahtar deger" veya "--anahtar=deger". Deger yoksa BOS kabul edilir
            // (eskiden "1" yazilirdi -> bos --server parametresi "Server=1" olurdu).
            string v;
            int esit = k.IndexOf('=');
            if (esit > 0) { v = k.Substring(esit + 1); k = k.Substring(0, esit); }
            else v = (i + 1 < args.Length && !args[i + 1].StartsWith("--")) ? args[++i] : "";
            p[k] = v;
        }

        try
        {
            Directory.CreateDirectory(Path.Combine(BASE, "logs"));
            _log = new StreamWriter(Path.Combine(BASE, "logs", "kurulum.log"), true, Encoding.UTF8) { AutoFlush = true };
        }
        catch { }

        string surum = "?";
        try { surum = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(); } catch { }
        Yaz("");
        Yaz("=========================================================");
        Yaz("  Print360 KURULUM  s" + surum + "   (mod: " + (mod ?? "yok") + ")");
        Yaz("  " + DateTime.Now);
        Yaz("=========================================================");

        try
        {
            if (mod == "sunucu") SunucuKur(p);
            else if (mod == "istemci") IstemciKur(p);
            else if (mod == "kaldir-sunucu") SunucuKaldir();
            else if (mod == "kaldir-istemci") IstemciKaldir();
            else { Yaz("HATA: --sunucu / --istemci / --kaldir-sunucu / --kaldir-istemci belirtilmeli."); return 2; }
        }
        catch (Exception ex)
        {
            Yaz("KURULUM HATASI: " + ex.Message);
            Yaz(ex.ToString());
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (_uyarilar.Count > 0)
        {
            Yaz("");
            Yaz("Kurulum tamamlandi, ancak " + _uyarilar.Count + " uyari var:");
            foreach (var u in _uyarilar) Yaz("  - " + u);
            // Uyarilar Setup'a stdout ile bildirilir (kritik degil, kurulum basarili sayilir)
            foreach (var u in _uyarilar) Console.WriteLine("UYARI: " + u);
        }
        else Yaz("Kurulum sorunsuz tamamlandi.");

        // KANIT DOSYASI: Kurulum sihirbazi, yapilandirmanin GERCEKTEN calistigini
        // bu dosyadan anlar. Sihirbaz her calismada benzersiz bir isaret uretip
        // --marker ile gecer; buraya yazilmazsa sihirbaz kullaniciya "yapilandirma
        // calismadi" uyarisi gosterir. Boylece "kurulum bitti dedi ama hicbir sey
        // olmadi" durumu sessiz kalmiyor.
        try
        {
            string isaret = Al(p, "marker");
            if (isaret.Length > 0)
                File.WriteAllText(Path.Combine(BASE, "logs", "son-kurulum.txt"),
                    isaret + "|" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Encoding.ASCII);
        }
        catch { }

        try { if (_log != null) _log.Flush(); } catch { }
        // Takilmis bir arka plan cagrisi (spooler/COM) surecin kapanmasini
        // geciktirmesin - Setup burada bekler kalirdi.
        Environment.Exit(0);
        return 0;
    }

    // Her satira saat yaz: kurulum takilirsa gunlukte SON satir nerede
    // durdugunu tam olarak gosterir.
    static void Yaz(string s)
    {
        string t = DateTime.Now.ToString("HH:mm:ss") + "  " + s;
        try { if (_log != null) _log.WriteLine(t); } catch { }
        Console.WriteLine(s);
    }
    static void Uyari(string s) { _uyarilar.Add(s); Yaz("UYARI: " + s); }

    // Bir adimi ZAMAN SINIRLI calistir. Windows'un bazi cagrilari (ozellikle
    // Print Spooler RPC: OpenPrinter/AddPrinter ve sertifika uretimi) zaman
    // asimi PARAMETRESI ALMAZ ve spooler mesgulse SURESIZ bloklar - kurulum
    // "sona geldi durdu" haline gelir. Adimi arka plan is parcaciginda
    // calistirip sinir asilirsa devam ederiz; is parcacigi IsBackground
    // oldugu icin surecin cikmasini engellemez.
    static bool ZamanSinirli(string ad, int sn, Action is_)
    {
        Exception hata = null;
        var t = new System.Threading.Thread(() => { try { is_(); } catch (Exception ex) { hata = ex; } });
        t.IsBackground = true;
        t.Start();
        if (!t.Join(sn * 1000))
        {
            Uyari(ad + ": " + sn + " saniyede tamamlanmadi, atlandi (Print Spooler mesgul olabilir).");
            return false;
        }
        if (hata != null) { Uyari(ad + ": " + hata.Message); return false; }
        return true;
    }
    static string Al(Dictionary<string, string> p, string k, string vars = "")
    {
        string v; return p.TryGetValue(k, out v) && v != null && v.Trim().Length > 0 ? v.Trim() : vars;
    }

    // ============================================================
    //                        SUNUCU
    // ============================================================
    static void SunucuKur(Dictionary<string, string> p)
    {
        string binDir = Al(p, "bin", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin"));
        string httpPort = Al(p, "httpport", "8360");
        string httpsPort = Al(p, "httpsport", "8443");
        bool vc = Al(p, "vc", "1") != "0";
        string yaziciModu = Al(p, "yazicimodu", "dogrudan").ToLowerInvariant();
        bool sqlKur = Al(p, "sql", "0") == "1";

        // --- 0) ESKI SURUMU KOKTEN SIL (eski binary kalmasin) ---
        EskiSurumuTemizle(true);

        // --- 1) Klasorler + izinler ---
        foreach (var d in new[] { BASE, "spool", "logs", "stats", @"stats\clients", "archive", "queue", "update" })
            Directory.CreateDirectory(d == BASE ? BASE : Path.Combine(BASE, d));
        IzinVer(BASE);
        Yaz("[1/8] Klasorler hazir: " + BASE);

        // --- 2) PrintService olay gunlugu (belge adi + sayfa sayisi buradan okunur) ---
        if (Calistir("wevtutil.exe", "sl Microsoft-Windows-PrintService/Operational /e:true") != 0)
            Uyari("PrintService olay gunlugu acilamadi (belge adi/sayfa sayisi bos gorunebilir).");
        Yaz("[2/8] PrintService olay gunlugu etkin.");

        // --- 3) Bilesenler ---
        SurecDurdur("Print360.ServerAgent", "Print360.Dashboard", "Print360.Panel");
        foreach (var f in new[] { "Print360.ServerAgent.exe", "Print360.Dashboard.exe", "Print360.Panel.exe",
                                  "Print360.ico", "System.Data.SQLite.dll" })
            KopyalaVarsa(Path.Combine(binDir, f), Path.Combine(BASE, f));
        // SQLite native interop: x64\ ve x86\ alt klasorlerinde olmali
        foreach (var mim in new[] { "x64", "x86" })
            KopyalaVarsa(Path.Combine(binDir, mim, "SQLite.Interop.dll"),
                         Path.Combine(BASE, mim, "SQLite.Interop.dll"));
        KopyalaVarsa(Path.Combine(binDir, "Print360.ClientAgent.exe"), Path.Combine(BASE, "update", "Print360.ClientAgent.exe"));
        Yaz("[3/8] Bilesenler yerlestirildi.");

        // --- 4) db.ini ---
        string sqlSrv = Al(p, "sqlserver", Environment.MachineName);
        string sqlUsr = Al(p, "sqluser", "sa");
        string sqlPwd = Al(p, "sqlpwd");   // varsayilan sifre YOK - kullanici girer
        AyarYaz(Path.Combine(BASE, "db.ini"),
            "Server=" + sqlSrv + "\r\nDatabase=Print360\r\nUser=" + sqlUsr + "\r\nPassword=" + sqlPwd +
            "\r\nHttpPort=" + httpPort + "\r\nHttpsPort=" + httpsPort +
            "\r\nVirtualChannel=" + (vc ? "1" : "0") +
            "\r\nVarsayilanYazici=" + yaziciModu + "\r\n", Encoding.ASCII);
        Yaz("[4/8] Ayarlar yazildi (RDP kanali: " + (vc ? "ACIK" : "kapali") + ", yazdirma modu: " + yaziciModu + ").");

        // --- 5) MSSQL (ISTEGE BAGLI - kurulmazsa sistem dosya modunda tam calisir) ---
        if (sqlKur)
        {
            try
            {
                SqlHazirla(sqlSrv, sqlUsr, sqlPwd, Al(p, "paneladmin", "admin"), Al(p, "paneladminpwd"));
                Yaz("[5/8] MSSQL hazir: Print360 @ " + sqlSrv);
            }
            catch (Exception ex) { Uyari("MSSQL kurulamadi (" + ex.Message + "). Sistem dosya modunda calisir."); }
        }
        else Yaz("[5/8] MSSQL atlandi - sistem dosya modunda calisir (istege bagli, sonradan eklenebilir).");

        // --- 6) Panel sifresi ---
        string panelPwd = Al(p, "panelpwd");
        if (panelPwd.Length > 0)
        {
            AyarYaz(Path.Combine(BASE, "panel.pwd"), Sha256(panelPwd), Encoding.ASCII);
        }

        // --- 7) Yazicilar (Windows API - PowerShell yok) ---
        // ONEMLI: Windows SERVER'da "Microsoft Print to PDF" ozelligi VARSAYILAN
        // OLARAK KAPALIDIR. Surucu yoksa AddPrinter basarisiz olur, sanal yazicilar
        // olusmaz ve kullanici yazdiramaz (hicbir sey spool'a dusmez). Once denetle,
        // yoksa ozelligi ac.
        if (!SuructVar(PDF_SURUCU))
        {
            Yaz("  'Microsoft Print to PDF' surucusu yok - Windows ozelligi aciliyor...");
            Calistir("dism.exe", "/online /enable-feature /featurename:Printing-PrintToPDFServices-Features /all /norestart /quiet", 180);
            // Spooler surucu listesini tazelesin
            Calistir("net.exe", "stop spooler", 60);
            Calistir("net.exe", "start spooler", 60);
            System.Threading.Thread.Sleep(2000);
            if (SuructVar(PDF_SURUCU)) Yaz("  Surucu etkinlestirildi: " + PDF_SURUCU);
            else Uyari("'Microsoft Print to PDF' surucusu ETKINLESTIRILEMEDI. Sanal yazicilar " +
                       "olusturulamaz ve YAZDIRMA CALISMAZ. Sunucuda 'Sunucu Yoneticisi > Roller ve " +
                       "Ozellikler'den 'Yazdirma ve Belge Hizmetleri' / 'Microsoft Print to PDF' ozelligini acin.");
        }
        else Yaz("  Yazici surucusu hazir: " + PDF_SURUCU);

        // Onceki kurulumdan kalan Print360 yazicilarini KALDIR: kullanici yazdirma
        // ekraninda birden fazla Print360 yazicisi gorup kafasi karismasin.
        ZamanSinirli("Eski Print360 yazicilarini kaldirma", 30, () => YazicilariSil());

        // TEK YAZICI: "Print360 - <kullanici>". Davranis kurulumda secilen moda
        // gore PORT ile belirlenir (ajan uc spool dosyasini da izler):
        //   dogrudan -> <u>.pdf          : istemcinin varsayilan yazicisina sessiz baski
        //   sec      -> <u>.sec.pdf      : yazici secim penceresi
        //   pdf      -> <u>.pdfview.pdf  : istemcide PDF olarak acilir
        // Tum secenekleri isteyen yonetici: --tumyazicilar 1
        bool tumu = Al(p, "tumyazicilar", "0") == "1";
        string ek = yaziciModu == "sec" ? ".sec.pdf" : yaziciModu == "pdf" ? ".pdfview.pdf" : ".pdf";

        string kullanicilar = Al(p, "users", Environment.UserName);
        int yOk = 0, yHata = 0;
        foreach (var u in kullanicilar.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string ku = u.Trim();
            if (ku.Length == 0) continue;
            var hedefler = new System.Collections.Generic.List<string[]>();
            if (tumu)
            {
                hedefler.Add(new[] { "Print360 - " + ku, Path.Combine(BASE, "spool", ku + ".pdf") });
                hedefler.Add(new[] { "Print360 Yazici Sec - " + ku, Path.Combine(BASE, "spool", ku + ".sec.pdf") });
                hedefler.Add(new[] { "Print360 PDF - " + ku, Path.Combine(BASE, "spool", ku + ".pdfview.pdf") });
            }
            else
                hedefler.Add(new[] { "Print360 - " + ku, Path.Combine(BASE, "spool", ku + ek) });

            foreach (var y in hedefler)
            {
                // Her yazici en fazla 25 sn: spooler donarsa kurulum kilitlenmesin
                bool ok = false;
                var yy = y;
                ZamanSinirli("Yazici '" + yy[0] + "'", 25, () => { ok = YaziciOlustur(yy[0], yy[1]); });
                if (ok) yOk++; else yHata++;
            }
        }
        Yaz("[6/8] Yazicilar: " + yOk + " hazir" + (yHata > 0 ? ", " + yHata + " basarisiz" : ""));
        if (yHata > 0) Uyari(yHata + " sanal yazici olusturulamadi. 'Microsoft Print to PDF' ozelligi acik mi?");

        // --- 8) Ag: urlacl + guvenlik duvari + HTTPS sertifikasi ---
        AgHazirla(httpPort, httpsPort);
        Yaz("[7/8] Ag ve HTTPS hazir (HTTP " + httpPort + " / HTTPS " + httpsPort + ").");

        // --- 9) Otomatik baslatma + kisayol + ajanlari baslat ---
        OtomatikBaslatma();
        Kisayol(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Print360 Panel.lnk"),
                Path.Combine(BASE, "Print360.Panel.exe"), "Print360 yonetim paneli");
        Baslat(Path.Combine(BASE, "Print360.Dashboard.exe"));
        Baslat(Path.Combine(BASE, "Print360.ServerAgent.exe"));
        System.Threading.Thread.Sleep(1500);
        if (Process.GetProcessesByName("Print360.ServerAgent").Length == 0)
            Uyari("Yazdirma ajani baslatilamadi. Elle: " + Path.Combine(BASE, "Print360.ServerAgent.exe"));
        Yaz("[8/8] Otomatik baslatma kuruldu, ajanlar calisiyor.");
    }

    // ============================================================
    //                        ISTEMCI
    // ============================================================
    static void IstemciKur(Dictionary<string, string> p)
    {
        string binDir = Al(p, "bin", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin"));

        // --- 0) ESKI SURUMU KOKTEN SIL (eski binary kalmasin) ---
        EskiSurumuTemizle(false);

        foreach (var d in new[] { BASE, "jobs", "done", "failed", "logs", "stats" })
            Directory.CreateDirectory(d == BASE ? BASE : Path.Combine(BASE, d));
        IzinVer(BASE);
        Yaz("[1/5] Klasorler hazir.");

        SurecDurdur("Print360.ClientAgent");
        string exe = Path.Combine(BASE, "Print360.ClientAgent.exe");
        KopyalaVarsa(Path.Combine(binDir, "Print360.ClientAgent.exe"), exe);
        KopyalaVarsa(Path.Combine(binDir, "Print360.ico"), Path.Combine(BASE, "Print360.ico"));
        Yaz("[2/5] Ajan yerlestirildi.");

        // RDP Virtual Channel eklentisi (kanal mantigi)
        string vcSrc = Path.Combine(binDir, "Print360.VC.dll");
        if (File.Exists(vcSrc))
        {
            string vcDll = Path.Combine(BASE, "Print360.VC.dll");
            KopyalaVarsa(vcSrc, vcDll);
            int kayitOk = 0;
            foreach (var kok in new[] { @"SOFTWARE\Microsoft\Terminal Server Client\Default\AddIns\Print360",
                                        @"SOFTWARE\WOW6432Node\Microsoft\Terminal Server Client\Default\AddIns\Print360" })
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(kok))
                        if (k != null) { k.SetValue("Name", vcDll); kayitOk++; }
                }
                catch (Exception ex) { Yaz("  Kayit yazilamadi (" + kok + "): " + ex.Message); }
            if (kayitOk > 0)
                Yaz("[3/5] RDP kanal eklentisi kuruldu (sonraki RDP oturumunda etkinlesir).");
            else
                Uyari("RDP kanal eklentisi KAYDEDILEMEDI (yonetici yetkisi gerekir). " +
                      "kanal modu devre disi kalir; isler HTTPS kanalindan tasinir.");
        }
        else Uyari("Print360.VC.dll pakette yok - isler HTTPS kanalindan tasinir.");

        // Ayar dosyasi
        var sb = new StringBuilder();
        sb.AppendLine("; Print360 istemci ayarlari");
        sb.AppendLine("; Server bos = RDP baglantisindan sunucuyu OTOMATIK bul");
        sb.AppendLine("Printer=" + Al(p, "printer"));
        sb.AppendLine("Server=" + Al(p, "server"));
        sb.AppendLine("ClientKey=" + Al(p, "clientkey"));
        sb.AppendLine("Port=" + Al(p, "port"));
        sb.AppendLine("CertHash=" + Al(p, "certhash"));
        sb.AppendLine("UseHttps=1");
        sb.AppendLine("YedekMotor=1");
        sb.AppendLine("Arayuz=1");
        sb.AppendLine("; VCMode: auto = RDP kanali varsa oradan (RDP kanali), yoksa HTTPS");
        sb.AppendLine("VCMode=auto");
        sb.AppendLine("SumatraPath=" + Path.Combine(BASE, "SumatraPDF.exe"));
        AyarYaz(Path.Combine(BASE, "Print360.ini"), sb.ToString(), Encoding.UTF8);

        // Yazdirma motoru: once paketten, yoksa internetten
        string sumatra = Path.Combine(BASE, "SumatraPDF.exe");
        if (!File.Exists(sumatra))
        {
            string yerel = Path.Combine(binDir, "SumatraPDF.exe");
            if (File.Exists(yerel)) KopyalaVarsa(yerel, sumatra);
            else if (Al(p, "sumatra", "1") == "1") SumatraIndir(sumatra);
        }
        if (!File.Exists(sumatra))
            Uyari("Yazdirma motoru (SumatraPDF.exe) yok. Baski basarisiz olabilir - " + BASE + " klasorune kopyalayin.");
        Yaz("[4/5] Ayarlar ve yazdirma motoru hazir.");

        // Otomatik baslatma + kisayol + baslat
        try
        {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                if (k != null) k.SetValue("Print360ClientAgent", "\"" + exe + "\"");
        }
        catch (Exception ex) { Uyari("Otomatik baslatma kaydi yazilamadi: " + ex.Message); }
        Kisayol(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Print360 Durum.lnk"),
                exe, "Print360 istemci durumu (baglanti, yazicilar, gorevler)");
        Baslat(exe);
        Yaz("[5/5] Ajan calisiyor, oturum acilisinda otomatik baslayacak.");
    }

    // ============================================================
    //   ESKI SURUMU KOKTEN TEMIZLE (her kurulumda, dosya kopyalamadan ONCE)
    //
    //   Eski binary'ler kalinca "kurdum ama eski surum calisiyor" karisikligi
    //   olusuyordu (or. yeni Tani sayfasi gorunmuyordu). Artik tum calistirilabilir
    //   dosyalar ve gecici veriler silinir; KULLANICI VERISI KORUNUR.
    //     SILINIR : *.exe, *.dll (DevExpress kalintilari dahil), update\, spool\, queue\
    //     KORUNUR : logs\, archive\, stats\, *.ini, panel.pwd, cert-thumbprint.txt,
    //               SumatraPDF.exe (16 MB - yeniden indirmeyelim)
    // ============================================================
    static void EskiSurumuTemizle(bool sunucu)
    {
        if (!Directory.Exists(BASE)) return;
        SurecDurdur("Print360.ServerAgent", "Print360.Dashboard", "Print360.Panel", "Print360.ClientAgent");
        System.Threading.Thread.Sleep(800);   // dosya kilitleri birakilsin

        int silinen = 0;
        foreach (var f in Directory.GetFiles(BASE))
        {
            string ad = Path.GetFileName(f);
            if (ad.Equals("SumatraPDF.exe", StringComparison.OrdinalIgnoreCase)) continue;  // buyuk indirme, korunur
            if (!ad.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !ad.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(f); silinen++; }
            catch (Exception ex) { Uyari("Eski dosya silinemedi (" + ad + "): " + ex.Message + " - calisiyor olabilir."); }
        }
        // Gecici klasorler: bayat isler yeni surumle karismasin
        foreach (var k in new[] { "update", "spool", "queue" })
            silinen += KlasorBosalt(Path.Combine(BASE, k));

        Yaz("[0/" + (sunucu ? "8" : "5") + "] Eski surum temizlendi (" + silinen +
            " dosya silindi; gunlukler, arsiv ve ayarlar korundu).");
    }

    static int KlasorBosalt(string yol)
    {
        int n = 0;
        try
        {
            if (!Directory.Exists(yol)) return 0;
            foreach (var f in Directory.GetFiles(yol, "*", SearchOption.AllDirectories))
                try { File.Delete(f); n++; } catch { }
        }
        catch { }
        return n;
    }

    // ============================================================
    //                        KALDIRMA
    // ============================================================
    static void SunucuKaldir()
    {
        // SIRA ONEMLI: once otomatik baslatma kaldirilir, sonra surecler
        // durdurulur. Aksi halde zamanlanmis gorev ajani hemen geri baslatabilir.
        Calistir("schtasks.exe", "/Delete /TN \"Print360 Yazdirma Ajani\" /F");
        RunKaydiSil("Print360ServerAgent", "Print360Dashboard");
        SurecDurdur("Print360.ServerAgent", "Print360.Dashboard", "Print360.Panel");
        ZamanSinirli("Yazicilari kaldirma", 30, () => YazicilariSil());
        try { File.Delete(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Print360 Panel.lnk")); } catch { }
        AgTemizle();
        BinaryleriSil();
        SurecDogrula("Print360.ServerAgent", "Print360.Dashboard", "Print360.Panel");
        Yaz("Sunucu bilesenleri kaldirildi. Gunlukler, arsiv ve istatistikler " + BASE + " altinda KORUNDU.");
    }

    // Kaldirmadan sonra HICBIR Print360 sureci kalmamali - dogrula ve raporla
    static void SurecDogrula(params string[] adlar)
    {
        System.Threading.Thread.Sleep(1200);
        foreach (var a in adlar)
        {
            var pr = Process.GetProcessesByName(a);
            if (pr.Length == 0) continue;
            Yaz("  '" + a + "' hala calisiyor, tekrar durduruluyor...");
            foreach (var x in pr) try { x.Kill(); x.WaitForExit(3000); } catch { }
            if (Process.GetProcessesByName(a).Length > 0)
                Uyari("'" + a + "' DURDURULAMADI. Bilgisayari yeniden baslatin.");
            else Yaz("  '" + a + "' durduruldu.");
        }
    }

    // Ag kayitlari: urlacl, guvenlik duvari kurallari, HTTPS sertifika baglamasi
    static void AgTemizle()
    {
        foreach (var pr in new[] { "8360", "8443" })
        {
            Calistir("netsh.exe", "advfirewall firewall delete rule name=\"Print360 " + pr + "\"", 20);
            Calistir("netsh.exe", "http delete urlacl url=http://+:" + pr + "/", 20);
            Calistir("netsh.exe", "http delete urlacl url=https://+:" + pr + "/", 20);
        }
        Calistir("netsh.exe", "http delete sslcert ipport=0.0.0.0:8443", 20);
    }

    // Calistirilabilir dosyalari sil (veri korunur)
    static void BinaryleriSil()
    {
        int n = 0;
        try
        {
            foreach (var f in Directory.GetFiles(BASE))
            {
                string ad = Path.GetFileName(f);
                if (!ad.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !ad.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(f); n++; } catch { }
            }
        }
        catch { }
        if (n > 0) Yaz("  " + n + " program dosyasi silindi.");
    }

    static void IstemciKaldir()
    {
        RunKaydiSil("Print360ClientAgent");
        SurecDurdur("Print360.ClientAgent");
        foreach (var kok in new[] { @"SOFTWARE\Microsoft\Terminal Server Client\Default\AddIns",
                                    @"SOFTWARE\WOW6432Node\Microsoft\Terminal Server Client\Default\AddIns" })
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(kok, true))
                    if (k != null) k.DeleteSubKeyTree("Print360", false);
            }
            catch { }
        try { File.Delete(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Print360 Durum.lnk")); } catch { }
        BinaryleriSil();
        SurecDogrula("Print360.ClientAgent");
        Yaz("Istemci bilesenleri kaldirildi. Gunlukler ve istatistikler " + BASE + " altinda KORUNDU.");
    }

    static void RunKaydiSil(params string[] adlar)
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                       @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                if (k != null) foreach (var a in adlar) try { k.DeleteValue(a, false); } catch { }
        }
        catch { }
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool EnumPrinters(uint Flags, string Name, uint Level, IntPtr pPrinterEnum,
                                    uint cbBuf, out uint pcbNeeded, out uint pcReturned);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool DeletePrinter(IntPtr hPrinter);

    // "Print360" ile baslayan tum sanal yazicilari sil
    static void YazicilariSil()
    {
        try
        {
            const uint PRINTER_ENUM_LOCAL = 2;
            uint gerek, adet;
            EnumPrinters(PRINTER_ENUM_LOCAL, null, 2, IntPtr.Zero, 0, out gerek, out adet);
            if (gerek == 0) return;
            IntPtr buf = Marshal.AllocHGlobal((int)gerek);
            try
            {
                if (!EnumPrinters(PRINTER_ENUM_LOCAL, null, 2, buf, gerek, out gerek, out adet)) return;
                int boyut = Marshal.SizeOf(typeof(PRINTER_INFO_2));
                for (int i = 0; i < adet; i++)
                {
                    var pi = (PRINTER_INFO_2)Marshal.PtrToStructure(
                        new IntPtr(buf.ToInt64() + i * boyut), typeof(PRINTER_INFO_2));
                    if (pi.pPrinterName == null ||
                        !pi.pPrinterName.StartsWith("Print360", StringComparison.OrdinalIgnoreCase)) continue;
                    IntPtr h;
                    var pd = new PRINTER_DEFAULTS { pDatatype = null, pDevMode = IntPtr.Zero, DesiredAccess = 0x000F000C };
                    if (OpenPrinter(pi.pPrinterName, out h, ref pd))
                    {
                        if (DeletePrinter(h)) Yaz("  Yazici silindi: " + pi.pPrinterName);
                        ClosePrinter(h);
                    }
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (Exception ex) { Yaz("Yazici silme: " + ex.Message); }
    }

    // ============================================================
    //             YAZICI OLUSTURMA (winspool.drv)
    // ============================================================
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PRINTER_DEFAULTS { public string pDatatype; public IntPtr pDevMode; public int DesiredAccess; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PRINTER_INFO_2
    {
        public string pServerName, pPrinterName, pShareName, pPortName, pDriverName, pComment, pLocation;
        public IntPtr pDevMode;
        public string pSepFile, pPrintProcessor, pDatatype, pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, cJobs, AveragePPM;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, ref PRINTER_DEFAULTS pDefault);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool ClosePrinter(IntPtr hPrinter);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr AddPrinter(string pName, uint Level, ref PRINTER_INFO_2 pPrinter);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "XcvDataW")]
    static extern bool XcvData(IntPtr hXcv, string pszDataName, IntPtr pInputData, uint cbInputData,
                               IntPtr pOutputData, uint cbOutputData, out uint pcbOutputNeeded, out uint pdwStatus);

    const string PDF_SURUCU = "Microsoft Print to PDF";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DRIVER_INFO_1 { public string pName; }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool EnumPrinterDrivers(string pName, string pEnvironment, uint Level,
                                          IntPtr pDriverInfo, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

    // Belirtilen yazici surucusu kurulu mu? (Microsoft Print to PDF Server'da kapali olabilir)
    static bool SuructVar(string surucuAdi)
    {
        try
        {
            uint gerek, adet;
            EnumPrinterDrivers(null, null, 1, IntPtr.Zero, 0, out gerek, out adet);
            if (gerek == 0) return false;
            IntPtr buf = Marshal.AllocHGlobal((int)gerek);
            try
            {
                if (!EnumPrinterDrivers(null, null, 1, buf, gerek, out gerek, out adet)) return false;
                int boyut = Marshal.SizeOf(typeof(DRIVER_INFO_1));
                for (int i = 0; i < adet; i++)
                {
                    var di = (DRIVER_INFO_1)Marshal.PtrToStructure(
                        new IntPtr(buf.ToInt64() + i * boyut), typeof(DRIVER_INFO_1));
                    if (string.Equals(di.pName, surucuAdi, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (Exception ex) { Yaz("  Surucu listesi okunamadi: " + ex.Message); }
        return false;
    }

    const int SERVER_ACCESS_ADMINISTER = 0x00000001;
    const uint PRINTER_ATTRIBUTE_LOCAL = 0x00000040;
    const int ERROR_PRINTER_ALREADY_EXISTS = 1802;
    const int ERROR_ALREADY_EXISTS = 183;

    // "Local Port" monitorunde dosya yolu portu olustur (PowerShell Add-PrinterPort karsiligi)
    static bool PortOlustur(string port)
    {
        IntPtr h;
        var pd = new PRINTER_DEFAULTS { pDatatype = null, pDevMode = IntPtr.Zero, DesiredAccess = SERVER_ACCESS_ADMINISTER };
        if (!OpenPrinter(",XcvMonitor Local Port", out h, ref pd))
        {
            Yaz("  PortOlustur: XcvMonitor acilamadi (" + Marshal.GetLastWin32Error() + ")");
            return false;
        }
        IntPtr buf = IntPtr.Zero;
        try
        {
            buf = Marshal.StringToHGlobalUni(port);              // null sonlandirilmis Unicode
            uint cb = (uint)((port.Length + 1) * 2);
            uint gerek, durum;
            bool ok = XcvData(h, "AddPort", buf, cb, IntPtr.Zero, 0, out gerek, out durum);
            // durum: 0 = basarili, 183 = zaten var (ikisi de kabul)
            if (!ok || (durum != 0 && durum != ERROR_ALREADY_EXISTS))
            {
                Yaz("  PortOlustur('" + port + "'): durum=" + durum);
                return durum == ERROR_ALREADY_EXISTS;
            }
            return true;
        }
        finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); ClosePrinter(h); }
    }

    static bool YaziciVar(string ad)
    {
        IntPtr h;
        var pd = new PRINTER_DEFAULTS { pDatatype = null, pDevMode = IntPtr.Zero, DesiredAccess = 0x00000008 /*PRINTER_ACCESS_USE*/ };
        if (OpenPrinter(ad, out h, ref pd)) { ClosePrinter(h); return true; }
        return false;
    }

    // Sanal yaziciyi olustur: cikti dogrudan spool dosyasina yazilir (dialog acilmaz)
    static bool YaziciOlustur(string ad, string port)
    {
        try
        {
            if (YaziciVar(ad)) { Yaz("  Yazici zaten var: " + ad); return true; }
            if (!PortOlustur(port)) Yaz("  Not: port olusturulamadi, yine de denenecek: " + port);

            var pi = new PRINTER_INFO_2
            {
                pPrinterName = ad,
                pPortName = port,
                pDriverName = PDF_SURUCU,
                pPrintProcessor = "winprint",
                pDatatype = "RAW",
                pComment = "Print360 - RDP yazdirma",
                pLocation = "",
                pShareName = "",
                pSepFile = "",
                pParameters = "",
                pServerName = null,
                pDevMode = IntPtr.Zero,
                pSecurityDescriptor = IntPtr.Zero,
                Attributes = PRINTER_ATTRIBUTE_LOCAL
            };
            IntPtr h = AddPrinter(null, 2, ref pi);
            if (h != IntPtr.Zero) { ClosePrinter(h); Yaz("  Yazici olusturuldu: " + ad); return true; }
            int err = Marshal.GetLastWin32Error();
            if (err == ERROR_PRINTER_ALREADY_EXISTS) return true;
            Yaz("  Yazici olusturulamadi: " + ad + " (hata " + err + ")");
            return false;
        }
        catch (Exception ex) { Yaz("  Yazici hatasi (" + ad + "): " + ex.Message); return false; }
    }

    // ============================================================
    //                 AG / HTTPS / SERTIFIKA
    // ============================================================
    static void AgHazirla(string httpPort, string httpsPort)
    {
        // Panel yonetici olmadan da portu dinleyebilsin (WD = herkes, dil bagimsiz)
        // NOT: Ayrim zaten varsa netsh 183 (ERROR_ALREADY_EXISTS) doner. Bu bir HATA
        // DEGILDIR - istenen sonuc zaten saglanmis demektir. Once silip yeniden
        // ekliyoruz; boylece gunluge yanilticii hata satiri dusmuyor.
        foreach (var pr in new[] { httpPort, httpsPort })
        {
            string sema = (pr == httpsPort) ? "https" : "http";
            Calistir("netsh.exe", "http delete urlacl url=" + sema + "://+:" + pr + "/", 20);
            int rc = Calistir("netsh.exe", "http add urlacl url=" + sema + "://+:" + pr + "/ sddl=\"D:(A;;GX;;;WD)\"");
            if (rc != 0) Yaz("  Port ayrimi (" + sema + " " + pr + ") eklenemedi; mevcut ayrim kullanilacak.");
        }
        // Guvenlik duvari
        foreach (var pr in new[] { httpPort, httpsPort })
        {
            Calistir("netsh.exe", "advfirewall firewall delete rule name=\"Print360 " + pr + "\"");
            Calistir("netsh.exe", "advfirewall firewall add rule name=\"Print360 " + pr +
                                  "\" dir=in action=allow protocol=TCP localport=" + pr);
        }
        // Self-signed sertifika + HTTPS baglama (RSA-2048 uretimi uzun surebilir)
        try
        {
            string parmak = null;
            ZamanSinirli("HTTPS sertifikasi", 90, () => { parmak = SertifikaHazirla(); });
            if (parmak != null)
            {
                Calistir("netsh.exe", "http delete sslcert ipport=0.0.0.0:" + httpsPort);
                int rc = Calistir("netsh.exe", "http add sslcert ipport=0.0.0.0:" + httpsPort +
                                  " certhash=" + parmak + " appid={7A3F2B10-360A-4B3B-9E01-000000008443}");
                if (rc != 0) Uyari("HTTPS sertifikasi porta baglanamadi (panel yalnizca HTTP calisabilir).");
                File.WriteAllText(Path.Combine(BASE, "cert-thumbprint.txt"), parmak, Encoding.ASCII);
            }
        }
        catch (Exception ex) { Uyari("HTTPS sertifikasi hazirlanamadi: " + ex.Message); }
    }

    // Var olan Print360 sertifikasini bul, yoksa uret (10 yil) ve guvenilen koke ekle
    static string SertifikaHazirla()
    {
        var my = new System.Security.Cryptography.X509Certificates.X509Store(
            System.Security.Cryptography.X509Certificates.StoreName.My,
            System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
        my.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadWrite);
        try
        {
            foreach (var c in my.Certificates)
                if (c.Subject == "CN=Print360" && c.NotAfter > DateTime.Now) return c.Thumbprint;
        }
        finally { my.Close(); }

        // CertEnroll COM ile uret (PowerShell New-SelfSignedCertificate karsiligi)
        try
        {
            Type tDn = Type.GetTypeFromProgID("X509Enrollment.CX500DistinguishedName");
            Type tKey = Type.GetTypeFromProgID("X509Enrollment.CX509PrivateKey");
            Type tCert = Type.GetTypeFromProgID("X509Enrollment.CX509CertificateRequestCertificate");
            Type tEnroll = Type.GetTypeFromProgID("X509Enrollment.CX509Enrollment");
            if (tDn == null || tKey == null || tCert == null || tEnroll == null) return null;

            dynamic dn = Activator.CreateInstance(tDn);
            dn.Encode("CN=Print360", 0);
            dynamic key = Activator.CreateInstance(tKey);
            key.ProviderName = "Microsoft RSA SChannel Cryptographic Provider";
            key.KeySpec = 1;              // XCN_AT_KEYEXCHANGE
            key.Length = 2048;
            key.MachineContext = true;
            key.ExportPolicy = 1;
            key.Create();
            dynamic cert = Activator.CreateInstance(tCert);
            cert.InitializeFromPrivateKey(2 /*ContextMachine*/, key, "");
            cert.Subject = dn;
            cert.Issuer = dn;
            cert.NotBefore = DateTime.Now.AddDays(-1);
            cert.NotAfter = DateTime.Now.AddYears(10);
            cert.Encode();
            dynamic enroll = Activator.CreateInstance(tEnroll);
            enroll.InitializeFromRequest(cert);
            string csr = enroll.CreateRequest(1 /*Base64*/);
            enroll.InstallResponse(2 /*AllowUntrustedRoot*/, csr, 1 /*Base64*/, "");

            // Yeni sertifikayi bul + guvenilen koke ekle (tarayici uyarmasin)
            my.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
            System.Security.Cryptography.X509Certificates.X509Certificate2 yeni = null;
            foreach (var c in my.Certificates) if (c.Subject == "CN=Print360") yeni = c;
            my.Close();
            if (yeni == null) return null;
            try
            {
                var kok = new System.Security.Cryptography.X509Certificates.X509Store(
                    System.Security.Cryptography.X509Certificates.StoreName.Root,
                    System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
                kok.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadWrite);
                kok.Add(yeni); kok.Close();
            }
            catch { }
            return yeni.Thumbprint;
        }
        catch (Exception ex) { Yaz("  Sertifika uretilemedi: " + ex.Message); return null; }
    }

    // ============================================================
    //          OTOMATIK BASLATMA (kanal mantigi)
    // ============================================================
    static void OtomatikBaslatma()
    {
        string ajan = Path.Combine(BASE, "Print360.ServerAgent.exe");
        string dash = Path.Combine(BASE, "Print360.Dashboard.exe");
        try
        {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                if (k != null)
                {
                    k.SetValue("Print360ServerAgent", "\"" + ajan + "\"");
                    k.SetValue("Print360Dashboard", "\"" + dash + "\"");
                }
        }
        catch (Exception ex) { Uyari("Otomatik baslatma kaydi yazilamadi: " + ex.Message); }

        // Zamanlanmis gorev: oturum acilisi + RDP YENIDEN BAGLANMASI + cokme kurtarma
        string xml = @"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.3"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Print360 yazdirma ajani</Description></RegistrationInfo>
  <Triggers>
    <LogonTrigger><Enabled>true</Enabled></LogonTrigger>
    <SessionStateChangeTrigger><Enabled>true</Enabled><StateChange>RemoteConnect</StateChange></SessionStateChangeTrigger>
  </Triggers>
  <Principals><Principal id=""Author""><GroupId>S-1-5-32-545</GroupId><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle><DisallowStartOnRemoteAppSession>false</DisallowStartOnRemoteAppSession>
    <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine><WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority>
    <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>
  </Settings>
  <Actions Context=""Author""><Exec><Command>" + ajan + @"</Command></Exec></Actions>
</Task>";
        string xmlYol = Path.Combine(Path.GetTempPath(), "Print360Ajan.xml");
        File.WriteAllText(xmlYol, xml, new UnicodeEncoding(false, true));
        Calistir("schtasks.exe", "/Delete /TN \"Print360 Yazdirma Ajani\" /F");
        if (Calistir("schtasks.exe", "/Create /TN \"Print360 Yazdirma Ajani\" /XML \"" + xmlYol + "\" /F") != 0)
            Uyari("Zamanlanmis gorev kurulamadi (ajan yine de oturum acilisinda baslar).");
        try { File.Delete(xmlYol); } catch { }
    }

    // ============================================================
    //                      YARDIMCILAR
    // ============================================================
    static void SqlHazirla(string srv, string usr, string pwd, string adminU, string adminP)
    {
        string cs = "Server=" + srv + ";Database=master;User ID=" + usr + ";Password=" + pwd + ";Connect Timeout=10";
        using (var cn = new System.Data.SqlClient.SqlConnection(cs))
        {
            cn.Open();
            using (var cmd = cn.CreateCommand())
            { cmd.CommandText = "IF DB_ID('Print360') IS NULL CREATE DATABASE Print360"; cmd.ExecuteNonQuery(); }
        }
        if (adminP.Length == 0) return;
        string cs2 = "Server=" + srv + ";Database=Print360;User ID=" + usr + ";Password=" + pwd + ";Connect Timeout=10";
        using (var cn = new System.Data.SqlClient.SqlConnection(cs2))
        {
            cn.Open();
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = "IF OBJECT_ID('dbo.PanelUsers','U') IS NULL CREATE TABLE dbo.PanelUsers(" +
                    "Kullanici NVARCHAR(100) PRIMARY KEY, SifreHash CHAR(64) NOT NULL, Rol NVARCHAR(20) DEFAULT 'admin');";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "IF EXISTS(SELECT 1 FROM PanelUsers WHERE Kullanici=@u) " +
                    "UPDATE PanelUsers SET SifreHash=@h WHERE Kullanici=@u " +
                    "ELSE INSERT INTO PanelUsers(Kullanici,SifreHash,Rol) VALUES(@u,@h,'admin')";
                cmd.Parameters.AddWithValue("@u", adminU);
                cmd.Parameters.AddWithValue("@h", Sha256(adminP));
                cmd.ExecuteNonQuery();
            }
        }
    }

    static string Sha256(string s)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            var sb = new StringBuilder();
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
    }

    static void SumatraIndir(string hedef)
    {
        try
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            string zip = Path.Combine(Path.GetTempPath(), "p360sumatra.zip");
            string kls = Path.Combine(Path.GetTempPath(), "p360sumatra");
            using (var wc = new System.Net.WebClient())
                wc.DownloadFile("https://www.sumatrapdfreader.org/dl/rel/3.5.2/SumatraPDF-3.5.2-64.zip", zip);
            if (Directory.Exists(kls)) Directory.Delete(kls, true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, kls);
            foreach (var f in Directory.GetFiles(kls, "*.exe", SearchOption.AllDirectories))
            { File.Copy(f, hedef, true); break; }
            try { File.Delete(zip); Directory.Delete(kls, true); } catch { }
            Yaz("  Yazdirma motoru indirildi: SumatraPDF");
        }
        catch (Exception ex) { Uyari("Yazdirma motoru indirilemedi (" + ex.Message + "). Internet baglaninca tekrar kurun."); }
    }

    static void IzinVer(string yol)
    {
        // Users grubu (SID ile - dil bagimsiz) yazabilsin.
        // /T KULLANILMAZ: arsiv/kuyruk klasorlerinde binlerce dosya olabilir ve
        // agaci dolasmak kurulumu dakikalarca yavaslatirdi. (OI)(CI) mirasi
        // zaten alt ogelere uygulanir - klasore bir kez yazmak yeterli.
        // NOT: Buyuk bir C:\Print360 agacinda icacls binlerce dosyada gezinip 30 sn'yi
        // asabiliyor ve her kurulumda "zaman asimi" uyarisi dusuyordu. Izin yalnizca
        // KLASORUN KENDISINE veriliyor (/T yok); alt ogeler kalitimla aliyor.
        Calistir("icacls.exe", "\"" + yol + "\" /grant \"*S-1-5-32-545:(OI)(CI)M\" /C /Q", 60);
    }
    static void SadeceYoneticiOkunur(string dosya)
    {
        Calistir("icacls.exe", "\"" + dosya + "\" /inheritance:r /grant \"*S-1-5-32-544:F\" \"*S-1-5-18:F\" \"*S-1-5-32-545:R\" /C /Q");
    }

    // Ayar dosyasini yaz. Onceki kurulumdan kalan kisitli izinler yazmayi engellerse
    // izinleri sifirlayip TEKRAR dener; yine olmazsa kurulumu comertmeden uyari verir.
    static void AyarYaz(string dosya, string icerik, Encoding enc)
    {
        try { File.WriteAllText(dosya, icerik, enc); }
        catch (UnauthorizedAccessException)
        {
            Yaz("  Ayar dosyasi kilitli, izinler sifirlaniyor: " + dosya);
            Calistir("icacls.exe", "\"" + dosya + "\" /reset /C /Q");
            Calistir("icacls.exe", "\"" + dosya + "\" /grant \"*S-1-5-32-544:F\" /C /Q");
            try { File.WriteAllText(dosya, icerik, enc); }
            catch (Exception ex2)
            { Uyari("Ayar dosyasi yazilamadi (" + Path.GetFileName(dosya) + "): " + ex2.Message); return; }
        }
        catch (Exception ex) { Uyari("Ayar dosyasi yazilamadi (" + Path.GetFileName(dosya) + "): " + ex.Message); return; }
        SadeceYoneticiOkunur(dosya);
    }

    static void KopyalaVarsa(string kaynak, string hedef)
    {
        try
        {
            if (!File.Exists(kaynak)) { Yaz("  (atlandi, yok): " + kaynak); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(hedef));
            File.Copy(kaynak, hedef, true);
        }
        catch (Exception ex) { Uyari("Kopyalanamadi " + Path.GetFileName(kaynak) + ": " + ex.Message); }
    }

    static void SurecDurdur(params string[] adlar)
    {
        foreach (var a in adlar)
            foreach (var pr in Process.GetProcessesByName(a))
                try { pr.Kill(); pr.WaitForExit(3000); } catch { }
    }

    static void Baslat(string exe)
    {
        try { if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); }
        catch (Exception ex) { Uyari("Baslatilamadi " + Path.GetFileName(exe) + ": " + ex.Message); }
    }

    static void Kisayol(string lnk, string hedef, string aciklama)
    {
        try
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return;
            dynamic sh = Activator.CreateInstance(t);
            dynamic k = sh.CreateShortcut(lnk);
            k.TargetPath = hedef;
            k.Description = aciklama;
            string ico = Path.Combine(BASE, "Print360.ico");
            if (File.Exists(ico)) k.IconLocation = ico + ",0";
            k.Save();
        }
        catch (Exception ex) { Uyari("Kisayol olusturulamadi: " + ex.Message); }
    }

    // Yardimci program calistir (netsh, icacls, schtasks, wevtutil) - konsol gorunmez.
    // Hicbir adim kurulumu kilitleyemez: asenkron okuma + zorunlu zaman asimi.
    static int Calistir(string exe, string args, int zamanAsimiSn = 45)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var pr = new Process())
            {
                pr.StartInfo = psi;
                // ONEMLI: Ciktiyi ASENKRON oku. ReadToEnd() ile stdout okurken cocuk
                // surec stderr borusunu doldurursa IKI TARAF DA BIRBIRINI BEKLER ve
                // kurulum sonsuza kadar kilitlenir (icacls binlerce satir yazabilir).
                var cikti = new StringBuilder();
                pr.OutputDataReceived += (s, e) => { if (e.Data != null) lock (cikti) cikti.AppendLine(e.Data); };
                pr.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (cikti) cikti.AppendLine(e.Data); };
                pr.Start();
                pr.BeginOutputReadLine();
                pr.BeginErrorReadLine();

                if (!pr.WaitForExit(zamanAsimiSn * 1000))
                {
                    try { pr.Kill(); } catch { }
                    Uyari("Adim zaman asimina ugradi ve durduruldu: " + exe + " " + args);
                    return -2;
                }
                if (pr.ExitCode != 0 && _log != null)
                {
                    string o; lock (cikti) o = cikti.ToString();
                    if (o.Length > 500) o = o.Substring(0, 500) + "...";
                    _log.WriteLine("  [" + exe + " " + args + "] cikis=" + pr.ExitCode + " " + o.Trim());
                }
                return pr.ExitCode;
            }
        }
        catch (Exception ex) { Yaz("  Calistirilamadi " + exe + ": " + ex.Message); return -1; }
    }
}
