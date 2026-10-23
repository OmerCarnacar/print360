// ============================================================
//  Print360 - RDP Yazdirma ve Yonetim Cozumu
//  Gelistirici : Omer CARNACAR  <omer.carnacar@outlook.com.tr>
//  LinkedIn    : https://www.linkedin.com/in/omercarnacar/
//  Lisans      : GPL-3.0-or-later, acik kaynak (bkz. LICENSE, NOTICE)
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
    // Panel sunucusu (Dashboard) ayri bir zamanlanmis gorevle, SYSTEM olarak calisir.
    const string PANEL_GOREVI = "Print360 Panel Sunucusu";
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
            // Temizlik adiminda otomatik baslatmayi askiya almistik. Kurulum
            // yarida kalirsa bunu GERI ALMAK sart: aksi halde gorev devre disi
            // kalir, ajan hic baslamaz ve sistem sessizce calismaz olur.
            try { Calistir("schtasks.exe", "/Change /TN \"Print360 Yazdirma Ajani\" /ENABLE", 20); } catch { }
            try { Calistir("schtasks.exe", "/Change /TN \"" + PANEL_GOREVI + "\" /ENABLE", 20); } catch { }
            SonucYaz(surum, mod, "HATA", ex.Message);
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
        SonucYaz(surum, mod, _uyarilar.Count > 0 ? "UYARI" : "OK",
                 _uyarilar.Count > 0 ? _uyarilar.Count + " uyari: " + string.Join(" | ", _uyarilar.ToArray()) : "");

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

    // KURULUM SONUCU (uzaktan dogrulama icin). Sunucu paneli bunu /api/surum
    // ile yayinlar: "kurdum" denildiginde hangi surumun, ne zaman, hangi
    // sonucla kuruldugu saniyeler icinde gorulur. Eskiden kurulumun sessizce
    // basarisiz oldugu ya da baska sunucuya yapildigi anlasilamiyordu.
    // Bicim: surum|tarih|mod|OK/UYARI/HATA|ayrinti
    static void SonucYaz(string surum, string mod, string sonuc, string ayrinti)
    {
        try
        {
            if (mod != "sunucu" && mod != "istemci") return;
            ayrinti = (ayrinti ?? "").Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
            if (ayrinti.Length > 400) ayrinti = ayrinti.Substring(0, 400) + "...";
            File.WriteAllText(Path.Combine(Path.Combine(BASE, "logs"), "kurulum-sonuc.txt"),
                surum + "|" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "|" + mod + "|" + sonuc + "|" + ayrinti,
                Encoding.UTF8);
        }
        catch { }
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
        SistemKalintilariTemizle(true, httpPort, httpsPort);

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
        string sqlPwd = Al(p, "sqlpwd");   // varsayilan sifre YOK - kullanici girer
        // SQL secilmediyse ve parola verilmediyse MSSQL bilgisi YAZILMAZ: eskiden
        // "Server=<makine>, User=sa, Password=(bos)" yaziliyor, panel yerel SQL
        // Server'a bos parolayla 'sa' girisi deniyordu (bkz. Db.ConnStr).
        bool sqlVar = sqlKur || sqlPwd.Length > 0;
        string sqlSrv = sqlVar ? Al(p, "sqlserver", Environment.MachineName) : "";
        string sqlUsr = sqlVar ? Al(p, "sqluser", "sa") : "";
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
        ZamanSinirli("Eski yazici portlarini kaldirma", 30, () => EskiPortlariSil());

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
        KurulumKaydiYaz("sunucu", "httpport=" + httpPort, "httpsport=" + httpsPort);
        Yaz("[7/8] Ag ve HTTPS hazir (HTTP " + httpPort + " / HTTPS " + httpsPort + ").");

        // --- 9) Otomatik baslatma + kisayol + ajanlari baslat ---
        bool panelGoreviKuruldu = OtomatikBaslatma();
        Kisayol(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Print360 Panel.lnk"),
                Path.Combine(BASE, "Print360.Panel.exe"), "Print360 yonetim paneli");
        PaneliBaslat(panelGoreviKuruldu);
        Baslat(Path.Combine(BASE, "Print360.ServerAgent.exe"));
        // GUNCELLEME TUM OTURUMLARDAKI AJANLARI KAPATIR. Eskiden yalnizca kurulumu
        // yapanin oturumunda yeniden baslatiliyordu: diger kullanicilarin ciktilari
        // oturumlarini kapatip acana kadar spool'da bekliyordu ("bazi kullanicilarda
        // calismiyor"). Gorev kullanici grubuna kayitli oldugundan istege bagli
        // calistirma, oturum acmis kullanicilarin oturumlarinda ajani baslatir.
        Calistir("schtasks.exe", "/Run /TN \"Print360 Yazdirma Ajani\"", 20);
        System.Threading.Thread.Sleep(4000);
        if (Process.GetProcessesByName("Print360.ServerAgent").Length == 0)
            Uyari("Yazdirma ajani baslatilamadi. Elle: " + Path.Combine(BASE, "Print360.ServerAgent.exe"));
        AjanKapsamiRaporu();
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
        SistemKalintilariTemizle(false, null, null);
        KurulumKaydiYaz("istemci");

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

            // ONEMLI - RDP ISTEMCISI ACILMAMA RISKI
            // mstsc.exe, AddIns altinda kayitli DLL'i KENDI MIMARISINDE yuklemeye
            // calisir. 64-bit DLL'i 32-bit mstsc'ye kaydedersek yukleme basarisiz
            // olur ve UZAK MASAUSTU HIC ACILMAZ. Bu yuzden kayit, DLL'in gercek
            // mimarisine gore YALNIZCA dogru dala yazilir.
            //   x64 DLL -> SOFTWARE\Microsoft\...            (64-bit gorunum)
            //   x86 DLL -> SOFTWARE\WOW6432Node\Microsoft\... (32-bit gorunum)
            bool kayitAtla = false;
            bool altmisDort = PeX64(vcDll);
            string dogruKok = EklentiKoku(altmisDort);
            string yanlisKok = EklentiKoku(!altmisDort);
            if (!VcKaydiYapilmali(Environment.Is64BitOperatingSystem, altmisDort))
            {
                // 32-bit Windows: tek kayit dali vardir ve 32-bit mstsc 64-bit DLL'i
                // YUKLEYEMEZ. Kaydedersek eklenti yuklenemez (eski surumlerde RDP hic
                // acilmiyordu). Kaydetme, eski kaydi da kaldir; istemci HTTPS ile calisir.
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                               @"SOFTWARE\Microsoft\Terminal Server Client\Default\AddIns", true))
                        if (k != null) k.DeleteSubKeyTree("Print360", false);
                }
                catch { }
                Uyari("32-BIT WINDOWS: RDP kanal eklentisi 64-bit oldugu icin bu makinede kullanilamaz; kaydedilmedi. "
                    + "Istemci HTTPS ile calisir - sunucu adresi ve 8443 portu bu makineden erisilebilir olmali.");
                kayitAtla = true;
            }

            // Onceki surumlerin yanlis dala yazdigi kayit varsa TEMIZLE - aksi
            // halde o makinede RDP acilmamaya devam ederdi.
            if (!kayitAtla)
            try
            {
                string ust = yanlisKok.Substring(0, yanlisKok.LastIndexOf(Path.DirectorySeparatorChar));
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(ust, true))
                    if (k != null && Array.IndexOf(k.GetSubKeyNames(), "Print360") >= 0)
                    {
                        k.DeleteSubKeyTree("Print360", false);
                        Yaz("  Uyumsuz mimarideki eski kayit temizlendi (RDP acilmama sebebi).");
                    }
            }
            catch { }

            int kayitOk = 0;
            if (!kayitAtla)
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(dogruKok))
                    if (k != null) { k.SetValue("Name", vcDll); kayitOk++; }
            }
            catch (Exception ex) { Yaz("  Kayit yazilamadi (" + dogruKok + "): " + ex.Message); }
            if (kayitAtla) { }
            else if (kayitOk > 0)
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
        IstemciAjaniniBaslat(exe);
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

        // SIRA ONEMLI: once OTOMATIK BASLATMA askiya alinir, sonra surecler
        // durdurulur. Aksi halde zamanlanmis gorev (oturum acilisi / RDP
        // baglantisi tetikleyicisi) ajani biz dosyalari kopyalarken yeniden
        // baslatabiliyor; exe kilitli kaliyor, kopyalama sessizce basarisiz
        // oluyor ve kurulum "tamamlandi" dedigi halde ESKI SURUM calismaya
        // devam ediyordu.
        // Yalnizca SUNUCUDA zamanlanmis gorev vardir; istemci Run kaydi kullanir.
        // Istemcide bu cagri "gorev bulunamadi" hatasi uretip gunluge yanilticii
        // bir satir dusuruyordu.
        if (sunucu)
        {
            Calistir("schtasks.exe", "/Change /TN \"Print360 Yazdirma Ajani\" /DISABLE", 20);
            // Panel gorevi 5 dakikada bir tekrar eder; kopyalama sirasinda paneli
            // yeniden baslatip exe'yi kilitlemesin. SYSTEM olarak calisan sureci
            // Kill ile durdurmak her zaman mumkun olmayabilir: gorev uzerinden durdur.
            if (GorevVar(PANEL_GOREVI))
            {
                Calistir("schtasks.exe", "/Change /TN \"" + PANEL_GOREVI + "\" /DISABLE", 20);
                Calistir("schtasks.exe", "/End /TN \"" + PANEL_GOREVI + "\"", 20);
            }
        }

        SurecDurdur("Print360.ServerAgent", "Print360.Dashboard", "Print360.Panel", "Print360.ClientAgent");
        System.Threading.Thread.Sleep(800);   // dosya kilitleri birakilsin

        // Durmayan bir surec varsa dosyalar kilitli kalir; zorla durdur.
        foreach (var a in new[] { "Print360.ServerAgent", "Print360.Dashboard", "Print360.Panel", "Print360.ClientAgent" })
            foreach (var pr in Process.GetProcessesByName(a))
                try { pr.Kill(); pr.WaitForExit(4000); } catch { }

        // ESKI SURUM KALINTILARI: GENEL KURAL (bkz. KalintiTemizle).
        // Eskiden yalnizca kok klasordeki *.exe/*.dll siliniyordu: her surumun
        // biraktigi baska turden dosyalar (betik, eski lisans dosyasi, gecici
        // guncelleme dosyasi, artik kullanilmayan klasorler) birikiyordu. Ustelik
        // istemci kurulumu, ayni makinede kurulu SUNUCUNUN dosyalarini da siliyordu.
        int silinen = 0;
        foreach (var satir in KalintiTemizle(BASE, sunucu, DateTime.Now))
        {
            if (satir.StartsWith("SILINEMEDI")) Uyari(satir + " - calisiyor olabilir.");
            else if (satir.StartsWith("GUNCELLENECEK")) silinen++;
            else { Yaz("  " + satir); silinen++; }
        }
        // SADECE "update" bosaltilir: orada otomatik guncelleme icin indirilen
        // gecici ikililer durur, yeni surumle karismamalidir.
        //
        // "queue" ve "spool" ARTIK SILINMIYOR. Buralarda KULLANICI VERISI var:
        //   queue = istemciye henuz teslim edilmemis baski isleri
        //   spool = yakalanmis ama gonderilmemis ciktilar
        // Eski surumde bunlar her guncellemede siliniyordu; yani sunucuyu
        // guncelleyen yonetici, kullanicilarin bekleyen ciktilarini farkinda
        // olmadan yok ediyordu. Guncelleme veri kaybettirmemelidir.
        silinen += KlasorBosalt(Path.Combine(BASE, "update"));

        // ONCEKI SURUMDEN KALAN BEKLEYEN ISLER: korunur VE ayrintili raporlanir
        // (hangi makine, kac is, en eskisi ne zaman, yeni surum onlara ne yapacak).
        foreach (var satir in BekleyenRaporu(BASE, sunucu, DateTime.Now)) Yaz("  " + satir);

        Yaz("[0/" + (sunucu ? "8" : "5") + "] Eski surum temizlendi (" + silinen +
            " dosya silindi; gunlukler, arsiv ve ayarlar korundu).");
    }

    // ============================================================
    //   ESKI SURUM KALINTILARI - GENEL KURAL
    //
    //   Her yeni surum, bir onceki surumun biraktigi ama artik kullanilmayan
    //   seyleri (dosya, klasor, Run kaydi, zamanlanmis gorev, guvenlik duvari
    //   kurali, yazici portu, sertifika, kisayol) temizlemeli. Eskiden kurulum
    //   yalnizca ADINI BILDIGI eski ogeleri siliyordu; listeye girmeyen her
    //   yeni kalinti birikiyordu.
    //
    //   Artik ters calisir: BU SURUMUN sahip oldugu ogeler listelidir.
    //   Print360'a ait olup listede OLMAYAN her sey kalintidir:
    //     - ikili/betik dosyalari silinir (yeniden uretilebilir),
    //     - bilinmeyen dosya ve klasorler SILINMEZ, eski-surum-kalintilari\<tarih>
    //       altina tasinir (veri kaybi yok, kok klasor temiz),
    //     - kullanici verisi ve ayarlara HIC dokunulmaz.
    //   Diger rolun (ayni makinede kurulu sunucu/istemci) ogeleri korunur.
    // ============================================================
    internal const string KALINTI_KLASORU = "eski-surum-kalintilari";
    internal static readonly string[] SunucuIkilileri = {
        "Print360.ServerAgent.exe", "Print360.Dashboard.exe", "Print360.Panel.exe", "System.Data.SQLite.dll" };
    internal static readonly string[] IstemciIkilileri = { "Print360.ClientAgent.exe", "Print360.VC.dll" };
    // Iki rolun da tuttugu, kurulumda silinmeyen dosyalar
    internal static readonly string[] OrtakKorunan = { "SumatraPDF.exe", "Print360.ico" };
    // Kullanici verisi ve ayarlar - ASLA dokunulmaz
    internal static readonly string[] VeriDosyalari = {
        "Print360.ini", "db.ini", "mail.ini", "panel.pwd", "cert-thumbprint.txt", "rules.csv",
        "print360.db", "print360.db-journal", "print360.db-wal", "print360.db-shm",
        "kimlik.txt", "makine-adi.txt", "SumatraPDF-settings.txt",
        "kurulum-sunucu.txt", "kurulum-istemci.txt", "hata-ayiklama.txt" };
    internal static readonly string[] BilinenKlasorler = {
        "logs", "stats", "archive", "queue", "queue-suresi-dolan", "spool", "update",
        "jobs", "done", "failed", "vc-outbox", "sumatrapdfcache", "x64", "x86", KALINTI_KLASORU };
    static readonly string[] IkiliUzantilar = {
        ".exe", ".dll", ".pdb", ".config", ".new", ".old", ".bak", ".cmd", ".bat", ".ps1", ".vbs",
        ".xml", ".tmp", ".manifest", ".application" };

    static bool Icinde(string[] liste, string ad)
    {
        foreach (var x in liste) if (string.Equals(x, ad, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // Kok klasordeki kalintilari temizler; yapilan her isi bir satirla doner.
    // Satir onekleri: GUNCELLENECEK (bu rolun ikilisi, yenisi kopyalanacak),
    // SILINDI, KENARA ALINDI, SILINEMEDI.
    internal static List<string> KalintiTemizle(string kok, bool sunucu, DateTime simdi)
    {
        var r = new List<string>();
        if (!Directory.Exists(kok)) return r;
        string[] benim = sunucu ? SunucuIkilileri : IstemciIkilileri;
        string[] diger = sunucu ? IstemciIkilileri : SunucuIkilileri;
        string kalinti = Path.Combine(Path.Combine(kok, KALINTI_KLASORU), simdi.ToString("yyyyMMdd_HHmmss"));

        foreach (var f in Directory.GetFiles(kok))
        {
            string ad = Path.GetFileName(f);
            if (Icinde(VeriDosyalari, ad) || Icinde(OrtakKorunan, ad) || Icinde(diger, ad)) continue;
            if (Icinde(benim, ad))
            {
                try { File.Delete(f); r.Add("GUNCELLENECEK: " + ad); }
                catch (Exception ex) { r.Add("SILINEMEDI: " + ad + " (" + ex.Message + ")"); }
                continue;
            }
            if (Icinde(IkiliUzantilar, Path.GetExtension(ad)))
            {
                try { File.Delete(f); r.Add("SILINDI (eski surum dosyasi): " + ad); }
                catch (Exception ex) { r.Add("SILINEMEDI: " + ad + " (" + ex.Message + ")"); }
                continue;
            }
            try
            {
                Directory.CreateDirectory(kalinti);
                File.Move(f, Path.Combine(kalinti, ad));
                r.Add("KENARA ALINDI (bu surumde kullanilmiyor): " + ad + " -> " + KALINTI_KLASORU + "\\" + Path.GetFileName(kalinti));
            }
            catch (Exception ex) { r.Add("SILINEMEDI: " + ad + " tasinamadi (" + ex.Message + ")"); }
        }

        foreach (var d in Directory.GetDirectories(kok))
        {
            string ad = Path.GetFileName(d);
            if (Icinde(BilinenKlasorler, ad)) continue;
            try
            {
                Directory.CreateDirectory(kalinti);
                Directory.Move(d, Path.Combine(kalinti, ad));
                r.Add("KENARA ALINDI (bu surumde kullanilmayan klasor): " + ad + "\\ -> " + KALINTI_KLASORU + "\\" + Path.GetFileName(kalinti));
            }
            catch (Exception ex) { r.Add("SILINEMEDI: klasor " + ad + " tasinamadi (" + ex.Message + ")"); }
        }

        // x64\ ve x86\ yalnizca SQLite cekirdegini tutar
        foreach (var mim in new[] { "x64", "x86" })
        {
            string md = Path.Combine(kok, mim);
            if (!Directory.Exists(md)) continue;
            foreach (var f in Directory.GetFiles(md))
            {
                string ad = Path.GetFileName(f);
                if (ad.Equals("SQLite.Interop.dll", StringComparison.OrdinalIgnoreCase)) continue;
                if (!Icinde(IkiliUzantilar, Path.GetExtension(ad))) continue;
                try { File.Delete(f); r.Add("SILINDI (eski surum dosyasi): " + mim + "\\" + ad); }
                catch (Exception ex) { r.Add("SILINEMEDI: " + mim + "\\" + ad + " (" + ex.Message + ")"); }
            }
        }
        return r;
    }

    // ------------------------------------------------------------
    // ONCEKI SURUMDEN KALAN BEKLEYEN ISLER (rapor; hicbir sey silinmez).
    // ------------------------------------------------------------
    internal static List<string> BekleyenRaporu(string kok, bool sunucu, DateTime simdi)
    {
        var r = new List<string>();
        try
        {
            if (sunucu)
            {
                string q = Path.Combine(kok, "queue");
                int toplam = 0, eski = 0, makine = 0, karantina = 0;
                DateTime enEski = DateTime.MaxValue;
                var satirlar = new List<string>();
                if (Directory.Exists(q))
                    foreach (var d in Directory.GetDirectories(q))
                    {
                        var fs = new DirectoryInfo(d).GetFiles("*.gz");
                        string sorunlu = Path.Combine(d, "_sorunlu");
                        if (Directory.Exists(sorunlu)) karantina += Directory.GetFiles(sorunlu, "*.gz").Length;
                        if (fs.Length == 0) continue;
                        makine++; toplam += fs.Length;
                        DateTime mEski = DateTime.MaxValue;
                        foreach (var f in fs)
                        {
                            if (f.CreationTime < mEski) mEski = f.CreationTime;
                            if ((simdi - f.CreationTime).TotalDays >= 7) eski++;
                        }
                        if (mEski < enEski) enEski = mEski;
                        satirlar.Add("    " + Path.GetFileName(d) + ": " + fs.Length + " is, en eskisi " + mEski.ToString("yyyy-MM-dd HH:mm"));
                    }
                if (toplam == 0) r.Add("Onceki surumden bekleyen is yok (queue bos).");
                else
                {
                    r.Add("ONCEKI SURUMDEN BEKLEYEN " + toplam + " IS KORUNDU (" + makine + " makine, en eskisi "
                        + enEski.ToString("yyyy-MM-dd") + "):");
                    satirlar.Sort();
                    for (int i = 0; i < satirlar.Count && i < 15; i++) r.Add(satirlar[i]);
                    if (satirlar.Count > 15) r.Add("    ... ve " + (satirlar.Count - 15) + " makine daha");
                    r.Add("  Makine baglaninca isler teslim edilir.");
                    if (eski > 0)
                        r.Add("  " + eski + " is 7 gunden eski: yeni surum bunlari otomatik teslim etmez, panel > Kuyruk'ta "
                            + "'Suresi dolan' olarak gosterir (silinmez; 'Yeniden gonder' ile gonderilebilir).");
                }
                if (karantina > 0) r.Add("Karantinada " + karantina + " is var (panel > Kuyruk).");

                string sp = Path.Combine(kok, "spool");
                if (Directory.Exists(sp))
                {
                    var fs = new DirectoryInfo(sp).GetFiles("*.pdf");
                    if (fs.Length > 0)
                    {
                        r.Add("SPOOL'DA " + fs.Length + " YAKALANMIS AMA GONDERILMEMIS CIKTI KORUNDU:");
                        foreach (var f in fs)
                            r.Add("    " + f.Name + " (" + f.LastWriteTime.ToString("yyyy-MM-dd HH:mm") + ", " + Math.Max(1, f.Length / 1024)
                                + " KB) - kullanicinin ajani oturum acinca gonderilir");
                    }
                }
            }
            else
            {
                string j = Path.Combine(kok, "jobs");
                if (Directory.Exists(j))
                {
                    int n = 0;
                    foreach (var f in Directory.GetFiles(j))
                        if (!f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) n++;
                    if (n > 0) r.Add("ONCEKI SURUMDEN YAZDIRILMAMIS " + n + " IS KORUNDU: ajan acilinca yazdirilacak (jobs\\).");
                }
                string fl = Path.Combine(kok, "failed");
                if (Directory.Exists(fl))
                {
                    int n = Directory.GetFiles(fl).Length;
                    if (n > 0) r.Add("Daha once basilamamis " + n + " is inceleme icin failed\\ klasorunde duruyor (yeniden denenmez).");
                }
            }
        }
        catch (Exception ex) { r.Add("Bekleyen is raporu olusturulamadi: " + ex.Message); }
        return r;
    }

    // ------------------------------------------------------------
    // SISTEM KALINTILARI: Run kayitlari, zamanlanmis gorevler, kisayollar;
    // sunucuda ek olarak guvenlik duvari kurallari, eski portlarin HTTPS
    // kayitlari ve suresi dolmus Print360 sertifikalari.
    // ------------------------------------------------------------
    internal static readonly string[] GecerliRunAdlari = { "Print360ServerAgent", "Print360ClientAgent", "Print360Dashboard" };
    internal static readonly string[] GecerliGorevler = { "Print360 Yazdirma Ajani", "Print360 Panel Sunucusu" };
    internal static readonly string[] GecerliKisayollar = { "Print360 Panel.lnk", "Print360 Durum.lnk" };

    internal static bool RunKalintiMi(string ad, string veri)
    {
        ad = ad ?? ""; veri = veri ?? "";
        bool bizim = ad.StartsWith("Print360", StringComparison.OrdinalIgnoreCase)
                  || veri.IndexOf(@"\Print360\", StringComparison.OrdinalIgnoreCase) >= 0;
        return bizim && !Icinde(GecerliRunAdlari, ad);
    }

    // tamYol: "\Gorev" ya da "\Klasor\Gorev"
    internal static bool GorevKalintiMi(string tamYol)
    {
        tamYol = (tamYol ?? "").Trim().Trim('"');
        if (tamYol.Length == 0) return false;
        string ad = tamYol.Substring(tamYol.LastIndexOf('\\') + 1);
        bool bizim = ad.StartsWith("Print360", StringComparison.OrdinalIgnoreCase)
                  || tamYol.StartsWith(@"\Print360\", StringComparison.OrdinalIgnoreCase);
        return bizim && !Icinde(GecerliGorevler, ad);
    }

    internal static bool KuralKalintiMi(string ad, string httpPort, string httpsPort)
    {
        if (string.IsNullOrEmpty(ad) || !ad.StartsWith("Print360", StringComparison.OrdinalIgnoreCase)) return false;
        return !ad.Equals("Print360 " + httpPort, StringComparison.OrdinalIgnoreCase)
            && !ad.Equals("Print360 " + httpsPort, StringComparison.OrdinalIgnoreCase);
    }

    static void SistemKalintilariTemizle(bool sunucu, string httpPort, string httpsPort)
    {
        int n = 0;
        // 1) Run kayitlari (64 ve 32 bit gorunum + kurulumu yapan kullanici)
        var kokler = new List<Microsoft.Win32.RegistryKey>();
        try { kokler.Add(Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)); } catch { }
        try { kokler.Add(Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32)); } catch { }
        try { kokler.Add(Microsoft.Win32.Registry.CurrentUser); } catch { }
        foreach (var kk in kokler)
        {
            try
            {
                using (var k = kk.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) continue;
                    foreach (var ad in k.GetValueNames())
                    {
                        string veri = Convert.ToString(k.GetValue(ad));
                        if (!RunKalintiMi(ad, veri)) continue;
                        k.DeleteValue(ad, false);
                        Yaz("  KALINTI SILINDI: otomatik baslatma kaydi '" + ad + "' (" + kk.Name + ") -> " + veri);
                        n++;
                    }
                }
            }
            catch (Exception ex) { Yaz("  Run kayitlari taranamadi (" + kk.Name + "): " + ex.Message); }
        }

        // 2) Zamanlanmis gorevler
        try
        {
            string cikti;
            if (Calistir("schtasks.exe", "/Query /FO CSV /NH", 45, out cikti) == 0)
            {
                var gorulen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var satir in cikti.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!satir.StartsWith("\"")) continue;
                    int son = satir.IndexOf('"', 1);
                    if (son < 0) continue;
                    string yol = satir.Substring(1, son - 1);
                    if (!GorevKalintiMi(yol) || !gorulen.Add(yol)) continue;
                    if (Calistir("schtasks.exe", "/Delete /TN \"" + yol + "\" /F", 20) == 0)
                    { Yaz("  KALINTI SILINDI: zamanlanmis gorev '" + yol + "'"); n++; }
                }
            }
        }
        catch (Exception ex) { Yaz("  Zamanlanmis gorevler taranamadi: " + ex.Message); }

        // 3) Masaustu kisayollari (ortak masaustu)
        try
        {
            string md = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            foreach (var l in Directory.GetFiles(md, "Print360*.lnk"))
                if (!Icinde(GecerliKisayollar, Path.GetFileName(l)))
                { File.Delete(l); Yaz("  KALINTI SILINDI: kisayol " + Path.GetFileName(l)); n++; }
        }
        catch { }

        if (sunucu)
        {
            // 4) Guvenlik duvari: "Print360 Dashboard", "Print360 Dashboard HTTPS" (eski
            //    PowerShell kurulumu) ve artik kullanilmayan portlarin kurallari
            ZamanSinirli("Eski guvenlik duvari kurallari", 30, () =>
            {
                try
                {
                    Type t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                    if (t == null) return;
                    dynamic pol = Activator.CreateInstance(t);
                    var silinecek = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (dynamic kural in pol.Rules)
                    {
                        string ad = kural.Name;
                        if (KuralKalintiMi(ad, httpPort, httpsPort)) silinecek.Add(ad);
                    }
                    foreach (var ad in silinecek)
                    {
                        for (int i = 0; i < 10; i++) { try { pol.Rules.Remove(ad); } catch { break; } }
                        Yaz("  KALINTI SILINDI: guvenlik duvari kurali '" + ad + "'");
                        n++;
                    }
                }
                catch (Exception ex) { Yaz("  Guvenlik duvari taranamadi: " + ex.Message); }
            });

            // 5) Port degistiyse eski portun HTTPS kayitlari. Yalnizca BIZIM
            //    olusturdugumuz kayit silinir: onceki kurulumun kaydi (kurulum-sunucu.txt)
            //    ya da sertifika baglamasinda Print360 uygulama kimligi.
            // Kayitli portlar: onceki kurulumun KENDI yazdigi (kesin bizim).
            var kayitliPortlar = new HashSet<string>();
            try
            {
                string kf = Path.Combine(BASE, "kurulum-sunucu.txt");
                if (File.Exists(kf))
                    foreach (var l in File.ReadAllLines(kf))
                        if (l.StartsWith("httpport=") || l.StartsWith("httpsport=")) kayitliPortlar.Add(l.Substring(l.IndexOf('=') + 1).Trim());
            }
            catch { }
            var eskiPortlar = new HashSet<string>(kayitliPortlar);
            eskiPortlar.Add("8443"); eskiPortlar.Add("8360");   // eski varsayilanlar (kayit oncesi surumler)
            foreach (var pr in eskiPortlar)
            {
                if (pr == httpPort || pr == httpsPort || pr.Length == 0) continue;
                string c;
                Calistir("netsh.exe", "http show sslcert ipport=0.0.0.0:" + pr, 20, out c);
                bool bizimSsl = c.IndexOf("7A3F2B10-360A-4B3B-9E01", StringComparison.OrdinalIgnoreCase) >= 0;
                if (bizimSsl && Calistir("netsh.exe", "http delete sslcert ipport=0.0.0.0:" + pr, 20) == 0)
                { Yaz("  KALINTI SILINDI: eski port " + pr + " HTTPS sertifika baglamasi"); n++; }
                // Erisim kaydi (urlacl) sahibini gostermez: yalnizca bizim oldugu
                // KANITLANAN portta silinir (kayitta var ya da sertifika bizim).
                if (bizimSsl || kayitliPortlar.Contains(pr))
                    foreach (var sema in new[] { "http", "https" })
                        if (Calistir("netsh.exe", "http delete urlacl url=" + sema + "://+:" + pr + "/", 20) == 0)
                        { Yaz("  KALINTI SILINDI: eski port " + pr + " " + sema + " erisim kaydi"); n++; }
            }

            // 6) Suresi dolmus Print360 sertifikalari
            try
            {
                var my = new System.Security.Cryptography.X509Certificates.X509Store(
                    System.Security.Cryptography.X509Certificates.StoreName.My,
                    System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
                my.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadWrite);
                try
                {
                    var sil = new List<System.Security.Cryptography.X509Certificates.X509Certificate2>();
                    foreach (var c in my.Certificates)
                        if (c.Subject == "CN=Print360" && c.NotAfter < DateTime.Now) sil.Add(c);
                    foreach (var c in sil)
                    { my.Remove(c); Yaz("  KALINTI SILINDI: suresi dolmus sertifika " + c.Thumbprint + " (" + c.NotAfter.ToString("yyyy-MM-dd") + ")"); n++; }
                }
                finally { my.Close(); }
            }
            catch (Exception ex) { Yaz("  Sertifikalar taranamadi: " + ex.Message); }
        }
        Yaz("  Sistem kalintilari: " + (n == 0 ? "yok." : n + " oge temizlendi."));
    }

    // Yazici silindikten sonra geride kalan dosya portlari (C:\Print360\spool\...).
    // Her kurulum, her kullanici ve her mod icin port ekleniyor, hic silinmiyordu.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PORT_INFO_1 { public string pName; }
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool EnumPorts(string pName, uint Level, IntPtr pPorts, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

    static void EskiPortlariSil()
    {
        var portlar = new List<string>();
        try
        {
            uint gerek, adet;
            EnumPorts(null, 1, IntPtr.Zero, 0, out gerek, out adet);
            if (gerek == 0) return;
            IntPtr buf = Marshal.AllocHGlobal((int)gerek);
            try
            {
                if (!EnumPorts(null, 1, buf, gerek, out gerek, out adet)) return;
                int boy = Marshal.SizeOf(typeof(PORT_INFO_1));
                for (int i = 0; i < adet; i++)
                {
                    var pi = (PORT_INFO_1)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + (long)i * boy), typeof(PORT_INFO_1));
                    if (pi.pName != null && pi.pName.StartsWith(BASE + "\\", StringComparison.OrdinalIgnoreCase)) portlar.Add(pi.pName);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (Exception ex) { Yaz("  Yazici portlari taranamadi: " + ex.Message); return; }
        if (portlar.Count == 0) return;

        IntPtr h;
        var pd = new PRINTER_DEFAULTS { pDatatype = null, pDevMode = IntPtr.Zero, DesiredAccess = SERVER_ACCESS_ADMINISTER };
        if (!OpenPrinter(",XcvMonitor Local Port", out h, ref pd)) return;
        int silinen = 0;
        try
        {
            foreach (var port in portlar)
            {
                IntPtr b = Marshal.StringToHGlobalUni(port);
                try
                {
                    uint gerek, durum;
                    if (XcvData(h, "DeletePort", b, (uint)((port.Length + 1) * 2), IntPtr.Zero, 0, out gerek, out durum) && durum == 0) silinen++;
                }
                finally { Marshal.FreeHGlobal(b); }
            }
        }
        finally { ClosePrinter(h); }
        if (silinen > 0) Yaz("  KALINTI SILINDI: " + silinen + " eski yazici portu (" + BASE + "\\spool\\...)");
    }

    // Bu kurulumun olusturduklari: sonraki kurulum neyin "bizim" oldugunu bilsin.
    static void KurulumKaydiYaz(string rol, params string[] satirlar)
    {
        try
        {
            File.WriteAllText(Path.Combine(BASE, "kurulum-" + rol + ".txt"),
                "surum=" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + "\r\n"
                + "tarih=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n"
                + string.Join("\r\n", satirlar) + "\r\n", Encoding.ASCII);
        }
        catch { }
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
        if (GorevVar(PANEL_GOREVI))
        {
            Calistir("schtasks.exe", "/End /TN \"" + PANEL_GOREVI + "\"", 20);
            Calistir("schtasks.exe", "/Delete /TN \"" + PANEL_GOREVI + "\" /F", 20);
        }
        RunKaydiSil("Print360ServerAgent", "Print360Dashboard");
        SurecDurdur("Print360.ServerAgent", "Print360.Dashboard", "Print360.Panel");
        ZamanSinirli("Yazicilari kaldirma", 30, () => YazicilariSil());
        try { File.Delete(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Print360 Panel.lnk")); } catch { }
        AgTemizle();
        BinaryleriSil();
        SurecDogrula("Print360.ServerAgent", "Print360.Dashboard", "Print360.Panel");

        // Kaldirmadan sonra kuyrukta teslim edilmemis is kaldiysa bunu SOYLE.
        // Sessiz kalmak, kullanicinin ciktisinin nereye gittigini bilememesi
        // demekti; dosyalar duruyor, sadece kimse haber vermiyordu.
        try
        {
            string q = Path.Combine(BASE, "queue");
            if (Directory.Exists(q))
            {
                int kalan = Directory.GetFiles(q, "*.gz", SearchOption.AllDirectories).Length;
                if (kalan > 0)
                    Yaz("  NOT: Teslim edilmemis " + kalan + " is kuyrukta duruyor (" + q + "). "
                      + "Silinmedi; Print360 tekrar kurulursa teslim edilir.");
            }
        }
        catch { }

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
    // Panel gorevi kurulduysa true doner.
    static bool OtomatikBaslatma()
    {
        string ajan = Path.Combine(BASE, "Print360.ServerAgent.exe");
        string dash = Path.Combine(BASE, "Print360.Dashboard.exe");

        // PANEL SUNUCUSU KULLANICI OTURUMUNDA CALISMAMALI.
        // Eskiden HKLM\Run ile oturum acan ILK kullanicinin oturumunda basliyordu.
        // O kullanici oturumu KAPATINCA panel - ve HTTPS istemcilerinin is aldigi
        // /api/jobs - onunla birlikte kapaniyordu: baska biri yeni oturum acana
        // kadar hicbir HTTPS istemcisi cikti alamiyor, panel "Gonderildi"de
        // kaliyordu. Ayrica her oturum acilisi yeni bir kopya baslatmaya
        // calisiyordu. Artik SYSTEM olarak, acilista ve 5 dakikada bir
        // (cokerse geri gelsin) tetiklenen bir gorev calistirir.
        bool panelGorevi = PanelGoreviKur(dash);
        try
        {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                if (k != null)
                {
                    k.SetValue("Print360ServerAgent", "\"" + ajan + "\"");
                    if (panelGorevi) k.DeleteValue("Print360Dashboard", false);
                    else k.SetValue("Print360Dashboard", "\"" + dash + "\"");   // geri donus: eski yol
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
        return panelGorevi;
    }

    internal static string PanelGoreviXml(string dash)
    {
        // BootTrigger: acilista, kimse oturum acmadan baslar.
        // TimeTrigger + Repetition PT5M + IgnoreNew: BEKCI. Calisiyorsa dokunulmaz;
        //   cokmusse en gec 5 dakikada geri gelir (RestartOnFailure yalnizca 3
        //   deneme yapar). Tekrar BootTrigger'a degil TimeTrigger'a baglidir:
        //   BootTrigger tekrari ancak BIR SONRAKI acilista baslardi.
        // ExecutionTimeLimit PT0S: varsayilan 72 saat sinirinda gorev OLDURULURDU.
        // SYSTEM (S-1-5-18): oturumdan bagimsiz; https portu icin urlacl gerekmez.
        return @"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.3"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Print360 panel sunucusu (web paneli + istemci is dagitimi)</Description></RegistrationInfo>
  <Triggers>
    <BootTrigger><Enabled>true</Enabled></BootTrigger>
    <TimeTrigger>
      <StartBoundary>" + DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss") + @"</StartBoundary>
      <Enabled>true</Enabled>
      <Repetition><Interval>PT5M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>
    </TimeTrigger>
  </Triggers>
  <Principals><Principal id=""Author""><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>5</Priority>
    <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>
  </Settings>
  <Actions Context=""Author""><Exec><Command>" + dash + @"</Command><WorkingDirectory>" + BASE + @"</WorkingDirectory></Exec></Actions>
</Task>";
    }

    static bool PanelGoreviKur(string dash)
    {
        string xmlYol = Path.Combine(Path.GetTempPath(), "Print360Panel.xml");
        try
        {
            File.WriteAllText(xmlYol, PanelGoreviXml(dash), new UnicodeEncoding(false, true));
            Calistir("schtasks.exe", "/Delete /TN \"" + PANEL_GOREVI + "\" /F", 20);
            if (Calistir("schtasks.exe", "/Create /TN \"" + PANEL_GOREVI + "\" /XML \"" + xmlYol + "\" /F") == 0)
            {
                Yaz("  Panel sunucusu gorevi kuruldu (SYSTEM, acilista + 5 dk'da bir denetim).");
                return true;
            }
            Uyari("Panel sunucusu gorevi kurulamadi; panel eskisi gibi oturum acilisinda baslayacak "
                + "(oturumu acan kullanici cikis yapinca panel de kapanir).");
            return false;
        }
        catch (Exception ex) { Uyari("Panel gorevi: " + ex.Message); return false; }
        finally { try { File.Delete(xmlYol); } catch { } }
    }

    static bool GorevVar(string ad)
    {
        return Calistir("schtasks.exe", "/Query /TN \"" + ad + "\"", 20) == 0;
    }

    // Paneli gorev uzerinden baslat; 15 sn icinde surec gorunmezse eski yola don.
    // Hicbir durumda kurulum paneli calismaz halde birakmaz.
    static void PaneliBaslat(bool gorevle)
    {
        string dash = Path.Combine(BASE, "Print360.Dashboard.exe");
        if (gorevle)
        {
            Calistir("schtasks.exe", "/Change /TN \"" + PANEL_GOREVI + "\" /ENABLE", 20);
            Calistir("schtasks.exe", "/Run /TN \"" + PANEL_GOREVI + "\"", 20);
            for (int i = 0; i < 15; i++)
            {
                System.Threading.Thread.Sleep(1000);
                if (Process.GetProcessesByName("Print360.Dashboard").Length > 0)
                {
                    Yaz("  Panel sunucusu calisiyor (SYSTEM gorevi).");
                    return;
                }
            }
            Uyari("Panel gorevi 15 sn icinde baslamadi; panel bu oturumda baslatiliyor. "
                + "Gorev, sunucu yeniden basladiginda ya da 5 dakika icinde devralir.");
        }
        Baslat(dash);
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


    // Bir DLL'in 64-bit olup olmadigini PE basligindan okur.
    // Yanlis mimarideki bir AddIn, mstsc.exe'nin hic acilmamasina yol acar.
    // RDP eklentisinin kayit dali DLL'in mimarisine gore secilir (tests\PeMimari sinar).
    internal static string EklentiKoku(bool x64)
    {
        return x64
            ? @"SOFTWARE\Microsoft\Terminal Server Client\Default\AddIns\Print360"
            : @"SOFTWARE\WOW6432Node\Microsoft\Terminal Server Client\Default\AddIns\Print360";
    }

    // 64-bit eklenti yalnizca 64-bit Windows'ta kaydedilir (32-bit Windows'ta
    // mstsc 32-bit'tir ve 64-bit DLL'i yukleyemez).
    internal static bool VcKaydiYapilmali(bool isletim64, bool dll64)
    {
        return isletim64 || !dll64;
    }

    internal static bool PeX64(string dosya)
    {
        try
        {
            using (var fs = new FileStream(dosya, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs))
            {
                fs.Position = 0x3C;
                int pe = br.ReadInt32();
                fs.Position = pe;
                if (br.ReadUInt32() != 0x00004550) return true;   // PE imzasi degilse karisma
                ushort makine = br.ReadUInt16();
                return makine == 0x8664 || makine == 0xAA64;      // x64 veya ARM64
            }
        }
        catch { return true; }   // okunamadiysa varsayilan: 64-bit
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

    // Bir ikiliyi yerine koyar ve SONUCU DOGRULAR.
    // Dosya kilitliyse (ajan yeniden baslamis olabilir) tek denemede pes etmek,
    // kurulumun "tamamlandi" deyip ESKI SURUMU calisir birakmasina yol aciyordu.
    // Bu yuzden birkaç kez denenir; yine olmazsa bu bir UYARI degil, gercek bir
    // kurulum hatasidir ve oyle bildirilir.
    static void KopyalaVarsa(string kaynak, string hedef)
    {
        if (!File.Exists(kaynak)) { Yaz("  (atlandi, yok): " + kaynak); return; }
        string sonHata = null;
        for (int deneme = 1; deneme <= 4; deneme++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(hedef));
                File.Copy(kaynak, hedef, true);

                // Gercekten yerine gecti mi? Boyut ve zaman damgasi karsilastir.
                var k = new FileInfo(kaynak);
                var h = new FileInfo(hedef);
                if (h.Exists && h.Length == k.Length) return;
                sonHata = "kopyalandi ama boyut tutmuyor";
            }
            catch (Exception ex)
            {
                sonHata = ex.Message;
                // Kilidi birakmasi icin bekle; surec yeniden basladiysa durdur.
                SurecDurdur(Path.GetFileNameWithoutExtension(hedef));
                System.Threading.Thread.Sleep(400 * deneme);
            }
        }
        Uyari("GUNCELLENEMEDI: " + Path.GetFileName(hedef) + " (" + sonHata + "). "
            + "Bu dosya ESKI SURUMDE kaldi. Bilgisayari yeniden baslatip kurulumu tekrarlayin.");
    }

    static void SurecDurdur(params string[] adlar)
    {
        foreach (var a in adlar)
            foreach (var pr in Process.GetProcessesByName(a))
                try { pr.Kill(); pr.WaitForExit(3000); } catch { }
    }

    // ------------------------------------------------------------------
    // ISTEMCI AJANI KULLANICI OLARAK BASLATILIR.
    // Kurulum yukseltilmis (ve bazen BASKA bir hesapla - BT yoneticisi)
    // calisir. Ajani buradan dogrudan baslatmak, onu o hesapla ve yonetici
    // yetkisiyle calistiriyordu: kullanicinin yazicilari ve varsayilan yazicisi
    // gorunmuyor, cikti yanlis yere gidiyordu. Masaustu kabugu (explorer)
    // uzerinden baslatilinca ajan, oturumu acan kullanici olarak ve
    // yukseltilmeden calisir. Oturumda kabuk yoksa (dagitim araciyla SYSTEM
    // olarak kurulum) ajan baslatilmaz; kullanici oturum acinca Run kaydiyla baslar.
    // Ardindan ajanin GERCEKTEN calistigi dogrulanir; calismiyorsa sebebi aranir.
    // ------------------------------------------------------------------
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, int wtsInfoClass,
                                                  out IntPtr ppBuffer, out uint pBytesReturned);
    [DllImport("wtsapi32.dll")]
    static extern void WTSFreeMemory(IntPtr pMemory);

    static string OturumKullanicisi(int oturum)
    {
        try
        {
            IntPtr b; uint n;
            if (!WTSQuerySessionInformation(IntPtr.Zero, oturum, 5 /*WTSUserName*/, out b, out n)) return "";
            try { return (Marshal.PtrToStringUni(b) ?? "").Trim(); } finally { WTSFreeMemory(b); }
        }
        catch { return ""; }
    }

    // Masaustu (explorer) olan oturumlardan hangisinde ajan YOK? (saf - test edilir)
    internal static List<int> AjanEksikOturumlar(IEnumerable<int> masaustuOturumlari, IEnumerable<int> ajanOturumlari)
    {
        var ajan = new HashSet<int>(ajanOturumlari);
        var eksik = new List<int>();
        foreach (var o in new SortedSet<int>(masaustuOturumlari))
            if (o != 0 && !ajan.Contains(o)) eksik.Add(o);
        return eksik;
    }

    static void AjanKapsamiRaporu()
    {
        try
        {
            var masaustu = new List<int>(); var ajan = new List<int>();
            foreach (var pr in Process.GetProcessesByName("explorer")) try { masaustu.Add(pr.SessionId); } catch { }
            foreach (var pr in Process.GetProcessesByName("Print360.ServerAgent")) try { ajan.Add(pr.SessionId); } catch { }
            var eksik = AjanEksikOturumlar(masaustu, ajan);
            int toplam = new HashSet<int>(masaustu).Count;
            if (eksik.Count == 0) { Yaz("  Yazdirma ajani acik olan " + toplam + " kullanici oturumunun hepsinde calisiyor."); return; }
            var adlar = new List<string>();
            foreach (var o in eksik) { string k = OturumKullanicisi(o); adlar.Add((k.Length > 0 ? k : "?") + " (oturum " + o + ")"); }
            Uyari("Yazdirma ajani " + eksik.Count + " oturumda BASLATILAMADI: " + string.Join(", ", adlar.ToArray())
                + ". Bu kullanicilarin ciktilari oturumlarini kapatip acana kadar bekler.");
        }
        catch { }
    }

    static void IstemciAjaniniBaslat(string exe)
    {
        int oturum = Process.GetCurrentProcess().SessionId;
        bool kabukVar = false;
        foreach (var pr in Process.GetProcessesByName("explorer"))
            try { if (pr.SessionId == oturum) kabukVar = true; } catch { }
        if (!kabukVar || oturum == 0)
        {
            Yaz("[5/5] Bu oturumda masaustu yok (dagitim araciyla kurulum); ajan kullanici oturum acinca otomatik baslayacak.");
            return;
        }
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = false }); }
        catch (Exception ex) { Yaz("  Kabuk uzerinden baslatilamadi: " + ex.Message); }
        if (AjanBekle(oturum, 12)) { Yaz("[5/5] Ajan oturumu acan kullanici olarak calisiyor; oturum acilisinda otomatik baslayacak."); return; }

        Yaz("  Ajan kabuk uzerinden baslamadi; dogrudan deneniyor.");
        Baslat(exe);
        if (AjanBekle(oturum, 8))
        {
            Uyari("Ajan yalnizca kurulum hesabiyla baslatilabildi; bu oturumda cikti yanlis yaziciya gidebilir. "
                + "Kullanici oturumu kapatip acinca dogru hesapla baslar.");
            return;
        }
        Uyari(AjanBaslamadiSebebi(exe));
    }

    static bool AjanBekle(int oturum, int saniye)
    {
        for (int i = 0; i < saniye * 2; i++)
        {
            foreach (var pr in Process.GetProcessesByName("Print360.ClientAgent"))
                try { if (pr.SessionId == oturum) return true; } catch { }
            System.Threading.Thread.Sleep(500);
        }
        return false;
    }

    // Ajan neden baslamadi? En sik sebepler: antivirus dosyayi karantinaya aldi,
    // AppLocker / WDAC C:\Print360 icinden program calistirmayi engelliyor,
    // .NET Framework eksik/bozuk.
    static string AjanBaslamadiSebebi(string exe)
    {
        if (!File.Exists(exe))
            return "AJAN BASLAMADI: " + exe + " SILINMIS - antivirus karantinaya almis olabilir. Dosyayi guvenilir listesine "
                 + "ekleyip kurulumu tekrarlayin.";
        try
        {
            string c;
            Calistir("wevtutil.exe", "qe \"Microsoft-Windows-AppLocker/EXE and DLL\" /c:30 /rd:true /f:text", 20, out c);
            if (c.IndexOf("Print360", StringComparison.OrdinalIgnoreCase) >= 0)
                return "AJAN BASLAMADI: AppLocker " + exe + " dosyasini ENGELLIYOR. BT biriminden C:\\Print360 klasoru icin "
                     + "izin kurali isteyin; ardindan kurulumu tekrarlayin.";
            Calistir("wevtutil.exe", "qe \"Microsoft-Windows-CodeIntegrity/Operational\" /c:30 /rd:true /f:text", 20, out c);
            if (c.IndexOf("Print360", StringComparison.OrdinalIgnoreCase) >= 0)
                return "AJAN BASLAMADI: Windows uygulama denetimi (WDAC) " + exe + " dosyasini ENGELLIYOR. BT biriminden "
                     + "izin isteyin.";
            Calistir("wevtutil.exe", "qe Application /q:\"*[System[Provider[@Name='.NET Runtime' or @Name='Application Error']]]\" /c:10 /rd:true /f:text", 20, out c);
            if (c.IndexOf("Print360.ClientAgent", StringComparison.OrdinalIgnoreCase) >= 0)
                return "AJAN BASLAMADI: program acilirken coktu (Uygulama olay gunlugunde .NET/Application Error kaydi var). "
                     + ".NET Framework 4.5+ kurulu ve saglam mi? Ayrinti: Olay Goruntuleyicisi > Uygulama.";
        }
        catch { }
        return "AJAN BASLAMADI ve sebebi olay gunluklerinde bulunamadi. Antivirus/uygulama denetimi ya da .NET Framework "
             + "olabilir. Elle deneyin: " + exe;
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
        string yok;
        return Calistir(exe, args, zamanAsimiSn, out yok);
    }

    static int Calistir(string exe, string args, int zamanAsimiSn, out string ciktiMetni)
    {
        ciktiMetni = "";
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
                lock (cikti) ciktiMetni = cikti.ToString();
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
