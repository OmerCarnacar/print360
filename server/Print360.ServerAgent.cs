// ============================================================
//  Print360 - RDP Yazdirma ve Yonetim Cozumu
//  Gelistirici : Omer CARNACAR  <omer.carnacar@outlook.com.tr>
//  LinkedIn    : https://www.linkedin.com/in/omercarnacar/
//  Lisans      : UCRETSIZ SURUM - para ile satilamaz (bkz. LICENSE)
//  Telif       : (c) 2026 Omer CARNACAR
// ============================================================
// Print360 Server Agent v2
// RDP sunucusunda, kullanici oturumunda calisir.
// - C:\Print360\spool\<kullanici>.pdf dosyasini izler; yeni cikti olustugunda
//   \\tsclient\<surucu>\Print360\jobs klasorune tasir (RDP surucu yonlendirmesi).
// - Her isi C:\Print360\stats\jobs.csv'ye kaydeder (belge adi + sayfa sayisi
//   PrintService olay gunlugunden alinir).
// - Istemcinin "basildi" kayitlarini (stats\printed.csv) periyodik olarak
//   sunucuya geri ceker -> C:\Print360\stats\clients\<makine>.csv
using System;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

static class ServerAgent
{
    static string user = Environment.UserName;
    static string spoolDir = @"C:\Print360\spool";
    // Uc sanal yazici / uc spool dosyasi (is turu modeli):
    //  ""    -> <u>.pdf         : atanan/varsayilan yaziciya sessiz baski
    //  "SEC" -> <u>.sec.pdf     : istemcide yazici secim penceresi
    //  "PDF" -> <u>.pdfview.pdf : istemcide PDF olarak ac/kaydet
    static readonly string[] jobTypes = { "", "SEC", "PDF" };
    static string logFile = @"C:\Print360\logs\server-" + user + ".log";
    static string jobsCsv = @"C:\Print360\stats\jobs.csv";
    static string clientsDir = @"C:\Print360\stats\clients";
    static string rulesCsv = @"C:\Print360\rules.csv";
    static string connLog = @"C:\Print360\logs\connections.log";
    // RDP istemci makinesinin adi. ORTAM DEGISKENINE GUVENILMEZ: ajan
    // zamanlanmis gorevden veya yukseltilmis baglamda baslarsa CLIENTNAME BOS
    // olur; o zaman is kuyruga hic yazilmaz ve spool'da birikirdi.
    // Bu yuzden once Windows'un WTS API'sine sorulur (kesin kaynak).
    static string clientName = IstemciAdiBul();

    const int WTS_CURRENT_SESSION_ID = -1;
    const int WTSClientName = 10;
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, int wtsInfoClass,
                                                  out IntPtr ppBuffer, out uint pBytesReturned);
    [DllImport("wtsapi32.dll")]
    static extern void WTSFreeMemory(IntPtr pMemory);

    static string IstemciAdiBul()
    {
        // 1) WTS API - oturumun gercek RDP istemci adi
        try
        {
            IntPtr buf; uint n;
            if (WTSQuerySessionInformation(IntPtr.Zero, WTS_CURRENT_SESSION_ID, WTSClientName, out buf, out n))
            {
                try
                {
                    string ad = Marshal.PtrToStringUni(buf);
                    if (!string.IsNullOrEmpty(ad) && ad.Trim().Length > 0) return ad.Trim();
                }
                finally { WTSFreeMemory(buf); }
            }
        }
        catch { }
        // 2) Yedek: ortam degiskeni
        return (Environment.GetEnvironmentVariable("CLIENTNAME") ?? "").Trim();
    }
    static object csvLock = new object();
    static bool lastConn = false, firstConn = true;

    static void Main()
    {
        bool created;
        using (var mx = new Mutex(true, "Print360ServerAgent_" + user, out created))
        {
            if (!created) return; // zaten calisiyor
            Directory.CreateDirectory(Path.GetDirectoryName(logFile));
            Directory.CreateDirectory(spoolDir);
            Directory.CreateDirectory(clientsDir);
            Log("Ajan basladi (v" + Surum.Etiket + "). Spool: " + spoolDir + "\\" + user + ".*  Istemci: " + clientName);
            VarsayilanYaziciAyarla(); // RDP oturumunda varsayilan = Print360 (dogrudan lokal PC'ye)
            // VC modu: istemciden gelen onay/sayac/heartbeat'i RDP kanalindan oku
            // (ayni "P360" kanali cift yonlu). Bayrak kapaliysa hicbir sey degismez.
            if (Db.VChannelAcik)
            {
                VChannel.DinlemeyeBasla(VChannelMesajIsle);
                Log("VC dinleme basladi: istemci onay/sayac/heartbeat RDP kanalindan gelecek.");
            }
            int tick = 0;
            while (true)
            {
                try
                {
                    foreach (var t in jobTypes)
                    {
                        string sp = SpoolPath(t);
                        if (File.Exists(sp) && IsStable(sp)) Dispatch(t, sp);
                    }
                    // Istemci adi bos ise tazele: ajan, RDP oturumu tam kurulmadan
                    // baslamis olabilir (zamanlanmis gorev oturum acilisinda tetiklenir).
                    if (clientName.Length == 0)
                    {
                        string yeni = IstemciAdiBul();
                        if (yeni.Length > 0) { clientName = yeni; Log("Istemci adi belirlendi: " + clientName); }
                    }
                    if (++tick % 30 == 0) { PullClientStats(); CheckConnection(); VarsayilanYaziciAyarla(); } // ~60 sn'de bir
                    if (tick % 1800 == 0) PurgeArchive(); // saatte bir: 90 gunden eski arsivi sil
                }
                catch (Exception ex) { Log("HATA: " + ex.Message); }
                Thread.Sleep(2000);
            }
        }
    }

    static bool IsStable(string path)
    {
        try
        {
            long s1 = new FileInfo(path).Length;
            if (s1 == 0) return false;
            Thread.Sleep(1000);
            long s2 = new FileInfo(path).Length;
            if (s1 != s2) return false;
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            return true;
        }
        catch { return false; }
    }

    // RDP kanal baglantisinin durumunu izle ve degisimleri connections.log'a yaz
    static void CheckConnection()
    {
        bool now = false;
        foreach (char d in "CDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            try { if (Directory.Exists(@"\\tsclient\" + d)) { now = true; break; } } catch { }
        }
        if (now != lastConn || firstConn)
        {
            string msg = now
                ? "SUNUCU: '" + user + "' oturumu istemciye baglandi (istemci: " + clientName + ")"
                : "SUNUCU: '" + user + "' oturumunun istemci baglantisi YOK (tsclient kapali/kopuk)";
            AppendLine(connLog, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg);
            Db.Exec("INSERT INTO ConnLog(Tarih,Olay) VALUES(GETDATE(),@o)", "@o", msg);
            if (!now && !firstConn) Db.Alert("RDP", "'" + user + "' oturumunun RDP surucu kanali koptu");
            Log(msg);
        }
        lastConn = now; firstConn = false;
    }

    // Yetki kurallari: once SQL (Rules tablosu), erisilemezse rules.csv
    static void LoadRule(string tip, string ad, out bool engel, out int kota)
    {
        engel = false; kota = 0;
        var dt = Db.Query("SELECT Engel, Kota FROM Rules WHERE Tip=@t AND Ad=@a", "@t", tip, "@a", ad);
        if (dt != null)
        {
            if (dt.Rows.Count > 0)
            {
                engel = Convert.ToBoolean(dt.Rows[0][0]);
                kota = Convert.ToInt32(dt.Rows[0][1]);
            }
            return;
        }
        try
        {
            if (!File.Exists(rulesCsv)) return;
            foreach (var line in File.ReadAllLines(rulesCsv))
            {
                var p = line.Replace("\"", "").Split(',');
                if (p.Length >= 3 && p[0].Equals(tip, StringComparison.OrdinalIgnoreCase)
                    && p[1].Equals(ad, StringComparison.OrdinalIgnoreCase))
                {
                    engel = p[2] == "1";
                    if (p.Length >= 4) int.TryParse(p[3], out kota);
                    return;
                }
            }
        }
        catch { }
    }

    // Kullanicinin bugunku sayfa toplami (kota denetimi icin) - once SQL, sonra CSV
    static int TodayPages()
    {
        var o = Db.Scalar("SELECT ISNULL(SUM(Sayfa),0) FROM Jobs WHERE Kullanici=@u AND Durum='OK' AND Tarih >= CAST(GETDATE() AS DATE)", "@u", user);
        if (o != null) return Convert.ToInt32(o);
        int sum = 0;
        try
        {
            if (!File.Exists(jobsCsv)) return 0;
            string bugun = DateTime.Now.ToString("yyyy-MM-dd");
            foreach (var line in File.ReadAllLines(jobsCsv))
            {
                var p = SplitCsv(line);
                if (p.Length >= 5 && p[0].StartsWith(bugun) && p[1] == user
                    && (p.Length < 9 || p[8] == "OK"))
                {
                    int n; if (int.TryParse(p[4], out n)) sum += n;
                }
            }
        }
        catch { }
        return sum;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool SetDefaultPrinter(string name);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PRINTER_DEFAULTS_A { public string pDatatype; public IntPtr pDevMode; public int DesiredAccess; }
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, ref PRINTER_DEFAULTS_A pDefault);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool ClosePrinter(IntPtr hPrinter);

    static bool YaziciKurulu(string ad)
    {
        try
        {
            IntPtr h;
            var pd = new PRINTER_DEFAULTS_A { pDatatype = null, pDevMode = IntPtr.Zero, DesiredAccess = 0x00000008 };
            if (OpenPrinter(ad, out h, ref pd)) { ClosePrinter(h); return true; }
        }
        catch { }
        return false;
    }

    static bool varsayilanLoglandi = false;

    // Yazici adini dosya adina guvenli gomme (UTF-8 -> hex); client geri cozer.
    static string HexKodla(string s)
    {
        var b = System.Text.Encoding.UTF8.GetBytes(s);
        var sb = new System.Text.StringBuilder(b.Length * 2);
        foreach (var x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }

    // SUNUCUDA yazici secim penceresi: baslikta lokal PC adi, listede o PC'nin
    // yazicilari (PrinterHealth'ten). Donus:
    //   yazici adi  -> sunucuda secildi (ise gomulecek)
    //   ""          -> liste yok/hazir degil -> client kendi secsin (yedek akis)
    //   null        -> kullanici iptal etti
    static string SunucudaClientYaziciSec(string lokalPc)
    {
        // Bu client'in yazicilarini SQL'den al (heartbeat/YAZICI ile guncellenmis)
        var yazicilar = new System.Collections.Generic.List<string>();
        try
        {
            if (lokalPc.Length > 0)
            {
                var dt = Db.Query("SELECT Yazici, Durum FROM PrinterHealth WHERE Makine=@m ORDER BY Yazici", "@m", lokalPc);
                if (dt != null)
                    foreach (System.Data.DataRow r in dt.Rows)
                    {
                        string y = Convert.ToString(r[0]);
                        if (y.Length == 0) continue;
                        string d = Convert.ToString(r[1]);
                        yazicilar.Add(d.Length > 0 && d != "Hazir" ? y + "   (" + d + ")" : y);
                    }
            }
        }
        catch (Exception ex) { Log("SEC: client yazici listesi alinamadi: " + ex.Message); }

        if (yazicilar.Count == 0) { Log("SEC: '" + lokalPc + "' icin yazici listesi henuz yok - client tarafina devredildi."); return ""; }

        string secilen = null;
        var t = new Thread(() =>
        {
            try
            {
                using (var f = new System.Windows.Forms.Form())
                using (var cb = new System.Windows.Forms.ComboBox())
                using (var ok = new System.Windows.Forms.Button())
                using (var ipt = new System.Windows.Forms.Button())
                using (var lbl = new System.Windows.Forms.Label())
                {
                    f.Text = "Print360 - Yazici Sec";
                    f.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
                    f.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
                    f.MaximizeBox = false; f.MinimizeBox = false; f.TopMost = true;
                    f.ClientSize = new System.Drawing.Size(420, 130);
                    lbl.Text = "Lokal bilgisayar:  " + lokalPc + "\r\nHangi yaziciya basilsin?";
                    lbl.SetBounds(14, 12, 392, 40);
                    cb.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
                    cb.SetBounds(14, 56, 392, 26);
                    foreach (var y in yazicilar) cb.Items.Add(y);
                    cb.SelectedIndex = 0;
                    ok.Text = "Yazdir"; ok.SetBounds(230, 92, 84, 28);
                    ok.DialogResult = System.Windows.Forms.DialogResult.OK;
                    ipt.Text = "Iptal"; ipt.SetBounds(322, 92, 84, 28);
                    ipt.DialogResult = System.Windows.Forms.DialogResult.Cancel;
                    f.Controls.Add(lbl); f.Controls.Add(cb); f.Controls.Add(ok); f.Controls.Add(ipt);
                    f.AcceptButton = ok; f.CancelButton = ipt;
                    f.Shown += (s, e) => { f.Activate(); cb.Focus(); };
                    if (f.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        // Etiketteki "   (Durum)" son ekini at, gercek yazici adini al
                        string y = Convert.ToString(cb.SelectedItem);
                        int p = y.IndexOf("   (");
                        secilen = p > 0 ? y.Substring(0, p) : y;
                    }
                }
            }
            catch (Exception ex) { Log("SEC penceresi hatasi: " + ex.Message); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        if (!t.Join(180000)) { Log("SEC: 3 dk secim yapilmadi, iptal."); return null; }
        return secilen;   // null = iptal, aksi halde secilen yazici
    }

    // Kullanicinin RDP oturumunda varsayilan yazici = "Print360 Yazici Sec - <kullanici>".
    // Boylece kullanici Ctrl+P -> Yazdir deyince LOKAL PC'sinde kendi yazicilarinin
    // listesi acilir, istedigini secer ve cikti oradan cikar (secim deneyimi).
    // Yonetici dogrudan-baski isterse varsayilani "Print360 - <kullanici>" yapabilir;
    // ajan Print360 ailesinden herhangi bir yazici varsayilan ise dokunmaz.
    static void VarsayilanYaziciAyarla()
    {
        try
        {
            Db.ConnStr();   // db.ini ayarlari yuklensin (VarsayilanYaziciModu)
            string mod = (Db.VarsayilanYaziciModu ?? "dogrudan").Trim().ToLowerInvariant();
            // TEK yazici modeli: ad her zaman "Print360 - <kullanici>"; davranisi
            // kurulumda secilen porta gore belirlenir. (Eski kurulumlardan kalan
            // ayri "Yazici Sec"/"PDF" yazicilari varsa onlar da yedek olarak denenir.)
            string hedef = "Print360 - " + user;
            if (!YaziciKurulu(hedef))
            {
                if (mod == "sec" && YaziciKurulu("Print360 Yazici Sec - " + user)) hedef = "Print360 Yazici Sec - " + user;
                else if (mod == "pdf" && YaziciKurulu("Print360 PDF - " + user)) hedef = "Print360 PDF - " + user;
            }
            // Windows 10+ "varsayilan yazicimi Windows yonetsin" -> kapat (klasik mod)
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                           @"Software\Microsoft\Windows NT\CurrentVersion\Windows"))
                    if (k != null && Convert.ToString(k.GetValue("LegacyDefaultPrinterMode")) != "1")
                        k.SetValue("LegacyDefaultPrinterMode", 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { }

            // Mevcut varsayilan zaten bir Print360 yazicisiysa dokunma
            string mevcut = "";
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows NT\CurrentVersion\Windows"))
                    if (k != null)
                    {
                        var d = Convert.ToString(k.GetValue("Device"));
                        if (d.Length > 0) mevcut = d.Split(',')[0];
                    }
            }
            catch { }
            if (mevcut.StartsWith("Print360", StringComparison.OrdinalIgnoreCase)) return;

            if (SetDefaultPrinter(hedef))
            {
                Log("Varsayilan yazici '" + hedef + "' olarak ayarlandi" +
                    (mevcut.Length > 0 ? " (onceki: " + mevcut + ")" : "") +
                    (mod == "sec" ? " - yazdirinca yazici secim listesi acilacak."
                     : mod == "pdf" ? " - cikti istemcide PDF olarak acilacak."
                     : " - cikti dogrudan istemcinin VARSAYILAN yazicisina gidecek."));
                varsayilanLoglandi = false;
            }
            else if (!varsayilanLoglandi)
            {
                Log("UYARI: Varsayilan yazici ayarlanamadi ('" + hedef + "' kurulu degil olabilir - Install-Server ile olusturun).");
                varsayilanLoglandi = true; // ayni uyariyi tekrarlamamak icin
            }
        }
        catch (Exception ex) { Log("Varsayilan yazici: " + ex.Message); }
    }

    static string SpoolPath(string type)
    {
        return Path.Combine(spoolDir, user + (type == "SEC" ? ".sec.pdf" : type == "PDF" ? ".pdfview.pdf" : ".pdf"));
    }

    // Belge adini dosya adina gomulebilir hale getir (gecersiz karakterler,
    // uzunluk, ayirici '~'). Bos donerse dosya adi eskisi gibi tarih-saat olur.
    static string BelgeAdiTemizle(string doc)
    {
        if (string.IsNullOrEmpty(doc)) return "";
        doc = doc.Trim();
        // Bazi uygulamalar "Belge1 - Word" / "rapor.xlsx - Excel" gonderir: son eki at
        int tire = doc.LastIndexOf(" - ");
        if (tire > 0 && doc.Length - tire <= 20) doc = doc.Substring(0, tire).Trim();
        // Tam yol geldiyse yalniz dosya adini al
        try { if (doc.IndexOf('\\') >= 0 || doc.IndexOf('/') >= 0) doc = Path.GetFileName(doc); } catch { }
        var sb = new System.Text.StringBuilder();
        foreach (char c in doc)
        {
            if (c == '~' || c == '\\' || c == '/' || c == ':' || c == '*' || c == '?' ||
                c == '"' || c == '<' || c == '>' || c == '|' || c < 32) sb.Append('_');
            else sb.Append(c);
        }
        string s = sb.ToString().Trim(' ', '.', '_');
        if (s.Length > 60) s = s.Substring(0, 60).Trim();
        return s;
    }

    static void Dispatch(string jobType, string spoolFile)
    {
        // ORIJINAL BELGE ADI: PrintService olay gunlugunden okunur ve dosya adina
        // '~' ayiricisiyla gomulur. Istemci bu kismi "Belge" olarak gosterir;
        // kullanici tarih-saat yerine gercek belge adini gorur.
        string ilkDoc, ilkSayfa;
        LookupDocInfo(out ilkDoc, out ilkSayfa);
        string docSafe = BelgeAdiTemizle(ilkDoc);

        string name = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + user
                    + (jobType.Length > 0 ? "__" + jobType : "")
                    + (docSafe.Length > 0 ? "~" + docSafe : "") + ".pdf";

        // --- Yetki denetimi (sunucu tarafinda engel) ---
        bool uEngel, mEngel; int uKota, mKota;
        LoadRule("user", user, out uEngel, out uKota);
        LoadRule("machine", clientName, out mEngel, out mKota);
        string engelSebep = null;
        if (uEngel) engelSebep = "Kullanici engelli";
        else if (clientName.Length > 0 && mEngel) engelSebep = "Makine engelli";

        byte[] pdfOn = null;
        if (engelSebep == null && uKota > 0)
        {
            try { pdfOn = File.ReadAllBytes(spoolFile); } catch { }
            string dummy, sayfaS; LookupDocInfo(out dummy, out sayfaS);
            int sayfa; int.TryParse(sayfaS, out sayfa);
            if (sayfa == 0 && pdfOn != null) sayfa = CountPages(pdfOn);
            if (TodayPages() + sayfa > uKota) engelSebep = "Gunluk kota (" + uKota + " sayfa) asildi";
        }
        if (engelSebep != null)
        {
            byte[] pdfE = pdfOn;
            if (pdfE == null) { try { pdfE = File.ReadAllBytes(spoolFile); } catch { } }
            string docE, pagesE; LookupDocInfo(out docE, out pagesE);
            if (pagesE.Length == 0 && pdfE != null) pagesE = CountPages(pdfE).ToString();
            File.Delete(spoolFile);
            if (pdfE != null) ArchiveJob(pdfE, name); // engellenen is de denetim icin arsivlenir
            string paperE = pdfE != null ? DetectPaper(pdfE) : "";
            string kbE = pdfE != null ? ((pdfE.Length + 1023) / 1024).ToString() : "";
            AppendCsv(jobsCsv, new[] {
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), user, clientName, docE, pagesE,
                name, paperE, kbE, "ENGEL: " + engelSebep });
            int pgE; int.TryParse(pagesE, out pgE); int kbEi; int.TryParse(kbE, out kbEi);
            Db.Exec("INSERT INTO Jobs(Tarih,Kullanici,Makine,Belge,Sayfa,Dosya,Kagit,KB,Durum) VALUES(GETDATE(),@u,@m,@b,@s,@f,@k,@kb,@d)",
                "@u", user, "@m", clientName, "@b", docE, "@s", pgE, "@f", name, "@k", paperE, "@kb", kbEi, "@d", "ENGEL: " + engelSebep);
            Db.Alert("Engel", "Yazdirma engellendi: " + user + " / " + (docE.Length > 0 ? docE : "belge") + " (" + engelSebep + ")");
            Log("ENGELLENDI (" + engelSebep + "): " + docE);
            return;
        }

        // --- SEC isi: yazici secimini SUNUCUDA yap (lokal PC adi + yazicilari goster) ---
        // Client'in yazicilari PrinterHealth tablosunda (VC YAZICI ya da HTTPS ile gelir).
        // Secilen yazici ise gomulur (__SECTO__<hex>); client dogrudan ona basar.
        if (jobType == "SEC")
        {
            string sec = SunucudaClientYaziciSec(clientName);
            if (sec == null)   // kullanici iptal etti
            {
                try { File.Delete(spoolFile); } catch { }
                AppendCsv(jobsCsv, new[] { DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    user, clientName, "", "0", name, "", "", "IPTAL: yazici secilmedi" });
                Log("SEC: kullanici yazici secmedi, is iptal edildi.");
                return;
            }
            if (sec.Length > 0)   // sunucuda secildi -> ise gom; bos ise client kendi secer (yedek)
            {
                name = name.Replace("__SEC", "__SECTO__" + HexKodla(sec));
                Log("SEC: sunucuda yazici secildi -> '" + sec + "' (lokal PC: " + clientName + ")");
            }
        }

        byte[] pdf = pdfOn;
        if (pdf == null) { try { pdf = File.ReadAllBytes(spoolFile); } catch { } }
        // NOT: Arsivleme, is BASARIYLA gonderildikten sonra yapilir. Aksi halde
        // gonderim basarisiz olup is spool'da kalinca her denemede yeniden
        // arsivlenip diski doldururdu.

        string kanal = null;

        // 1. kanal: RDP Virtual Channel (db.ini VirtualChannel=1 ise; ayar/port/firewall gerekmez)
        //           Kanal yoksa (istemci eklentisi kurulu degil / RDP disi) Gonder false doner.
        if (Db.VChannelAcik && pdf != null && VChannel.Gonder(name, pdf))
            kanal = "VirtualChannel";

        // 2. kanal: HTTPS/dosya kuyrugu (GZip sikistirmali, tsclient gerektirmez)
        string hedefMakine = clientName;
        if (hedefMakine.Length == 0)
        {
            // Ajan bir RDP oturumunda degilse (konsol/hizmet oturumu) istemci adi
            // bilinemez. Bu durumda TEK bir istemci cevrimiciyse ona gonderiyoruz;
            // aksi halde is spool'da bekler (yanlis makineye gonderilmez).
            hedefMakine = TekOnlineIstemci();
            if (hedefMakine.Length > 0)
                Log("Istemci adi bos - tek cevrimici istemciye yonlendiriliyor: " + hedefMakine);
        }
        if (kanal == null && pdf != null && hedefMakine.Length > 0 && QueueJob(pdf, name, hedefMakine))
            kanal = "HTTPS-kuyruk";

        // 3. kanal (yedek): \\tsclient surucu yonlendirmesi
        if (kanal == null)
        {
            string jobsDir = FindClientDir("jobs", true);
            if (jobsDir == null)
            {
                if (clientName.Length == 0)
                    Log("UYARI: Is gonderilemedi - ISTEMCI ADI BELIRLENEMEDI. Ajan bir RDP " +
                        "oturumunda calismiyor olabilir (konsol/hizmet oturumu). Yazdiran kullanicinin " +
                        "KENDI RDP oturumunda 'Print360.ServerAgent.exe' calismalidir. Is bekletiliyor.");
                else
                    Log("UYARI: Is gonderilemedi ('" + clientName + "') - kuyruga yazilamadi ve " +
                        "\\\\tsclient bulunamadi. Is bekletiliyor.");
                return;
            }
            string tmp = Path.Combine(jobsDir, name + ".tmp");
            File.Copy(spoolFile, tmp, true);
            File.Move(tmp, Path.Combine(jobsDir, name));
            kanal = "tsclient";
        }
        File.Delete(spoolFile);
        if (pdf != null) ArchiveJob(pdf, name);   // gonderim basarili -> tek kez arsivle

        // Belge adi is basinda zaten okundu; bos geldiyse (olay gunlugu gecikmis
        // olabilir) burada bir kez daha denenir.
        string doc = ilkDoc, pages = ilkSayfa;
        if (doc.Length == 0 || pages.Length == 0) LookupDocInfo(out doc, out pages);
        string paper = pdf != null ? DetectPaper(pdf) : "";
        string kb = pdf != null ? ((pdf.Length + 1023) / 1024).ToString() : "";
        if (pages.Length == 0 && pdf != null) pages = CountPages(pdf).ToString();
        AppendCsv(jobsCsv, new[] {
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), user, clientName, doc, pages, name, paper, kb, "OK" });
        int pg2; int.TryParse(pages, out pg2); int kb2; int.TryParse(kb, out kb2);
        if (!Db.Exec("INSERT INTO Jobs(Tarih,Kullanici,Makine,Belge,Sayfa,Dosya,Kagit,KB,Durum) VALUES(GETDATE(),@u,@m,@b,@s,@f,@k,@kb,'OK')",
                "@u", user, "@m", clientName, "@b", doc, "@s", pg2, "@f", name, "@k", paper, "@kb", kb2))
            Log("SQL yazilamadi (CSV'de kayitli): " + Db.Err);
        Log("Is gonderildi [" + kanal + "]: " + name + "  Belge: " + doc + "  Sayfa: " + pages + "  Kagit: " + paper + "  " + kb + " KB");
    }

    // Her isin PDF kopyasini sikistirilmis arsive al (panelden indirme + denetim icin)
    static void ArchiveJob(byte[] pdf, string name)
    {
        try
        {
            string dir = @"C:\Print360\archive\" + DateTime.Now.ToString("yyyy-MM");
            Directory.CreateDirectory(dir);
            using (var fs = File.Create(Path.Combine(dir, name + ".gz")))
            using (var gz = new GZipStream(fs, CompressionMode.Compress))
                gz.Write(pdf, 0, pdf.Length);
        }
        catch (Exception ex) { Log("Arsiv hatasi: " + ex.Message); }
    }

    // 90 gunden eski arsiv dosyalarini temizle
    static void PurgeArchive()
    {
        try
        {
            string root = @"C:\Print360\archive";
            if (!Directory.Exists(root)) return;
            foreach (var d in Directory.GetDirectories(root))
            {
                foreach (var f in Directory.GetFiles(d))
                    if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-90))
                        try { File.Delete(f); } catch { }
                if (Directory.GetFiles(d).Length == 0 && Directory.GetDirectories(d).Length == 0)
                    try { Directory.Delete(d); } catch { }
            }
        }
        catch { }
    }

    // Isi GZip ile sikistirip kuyruga al; istemci HTTPS API'den ceker.
    // MSSQL ZORUNLU DEGILDIR: kuyrugun kendisi DOSYA tabanlidir
    //   C:\Print360\queue\<makine>\<is-adi>.gz
    // SQL varsa ayrica JobQueue tablosuna da yazilir (panel raporlari icin),
    // ama SQL erisilemezse is YINE DE gonderilir - eskiden burada is duserdi.
    // Son 3 dakikada kalp atisi gelen istemciler; TAM OLARAK BIR tane varsa adini
    // dondur. Ajan RDP oturumunda degilse (istemci adi bos) is bu makineye gider.
    // Birden fazla istemci varsa BOS doner - yanlis makineye gonderme riski alinmaz.
    static string TekOnlineIstemci()
    {
        var adlar = new System.Collections.Generic.List<string>();
        try
        {
            var dt = Db.Query("SELECT Makine FROM Heartbeat WHERE SonGorulme > DATEADD(minute,-3,GETDATE())");
            if (dt != null)
                foreach (System.Data.DataRow r in dt.Rows)
                {
                    string m = Convert.ToString(r[0]).Trim();
                    if (m.Length > 0) adlar.Add(m);
                }
            else
            {
                // SQL yoksa dosya: "makine","sonGorulme","yazici","ip"
                string hb = @"C:\Print360\stats\heartbeat.csv";
                if (File.Exists(hb))
                    foreach (var ln in File.ReadAllLines(hb))
                    {
                        var f = SplitCsv(ln);
                        if (f.Length < 2 || f[0].Trim().Length == 0) continue;
                        DateTime t;
                        if (DateTime.TryParseExact(f[1], "yyyy-MM-dd HH:mm:ss",
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out t) &&
                            t > DateTime.Now.AddMinutes(-3))
                            adlar.Add(f[0].Trim());
                    }
            }
        }
        catch (Exception ex) { Log("Cevrimici istemci sorgusu: " + ex.Message); }

        var tekil = new System.Collections.Generic.List<string>();
        foreach (var a in adlar)
        {
            bool var_ = false;
            foreach (var t in tekil) if (string.Equals(t, a, StringComparison.OrdinalIgnoreCase)) { var_ = true; break; }
            if (!var_) tekil.Add(a);
        }
        if (tekil.Count == 1) return tekil[0];
        if (tekil.Count > 1)
            Log("Istemci adi bos ve " + tekil.Count + " istemci cevrimici - hedef belirsiz, is bekletiliyor.");
        return "";
    }

    static bool QueueJob(byte[] pdf, string name, string hedefMakine = null)
    {
        try
        {
            string m0 = !string.IsNullOrEmpty(hedefMakine) ? hedefMakine : clientName;
            string mak = Sanitize(m0.Length > 0 ? m0 : "bilinmeyen");
            string qDir = Path.Combine(@"C:\Print360\queue", mak);
            Directory.CreateDirectory(qDir);
            string path = Path.Combine(qDir, name + ".gz");
            // Atomik yazim: istemci yarim dosya cekmesin (.tmp -> rename)
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            using (var gz = new GZipStream(fs, CompressionMode.Compress))
                gz.Write(pdf, 0, pdf.Length);
            if (File.Exists(path)) { try { File.Delete(path); } catch { } }
            File.Move(tmp, path);
            long gzLen = new FileInfo(path).Length;

            // SQL varsa kaydet (opsiyonel - basarisiz olsa da is kuyrukta kalir)
            if (!Db.Exec("INSERT INTO JobQueue(Makine,Dosya,Yol,BoyutKB,SikistirilmisKB,Olusturma,Durum) " +
                         "VALUES(@m,@f,@y,@b,@s,GETDATE(),'BEKLIYOR')",
                    "@m", clientName, "@f", name, "@y", path,
                    "@b", (int)((pdf.Length + 1023) / 1024), "@s", (int)((gzLen + 1023) / 1024)))
                Log("Not: SQL kuyruk kaydi yazilamadi (is dosya kuyrugundan gidecek): " + Db.Err);

            Log("Kuyruga alindi [" + mak + "]: " + name + "  " + (pdf.Length / 1024) + " KB -> " + (gzLen / 1024) + " KB (GZip, %"
                + (pdf.Length > 0 ? 100 - (int)(gzLen * 100 / pdf.Length) : 0) + " kucultme)");
            return true;
        }
        catch (Exception ex) { Log("Kuyruk hatasi: " + ex.Message); return false; }
    }

    // PrintService olay gunlugunden (Event 307) belge adi ve sayfa sayisi.
    // Parametreler: %1 is no, %2 belge, %3 kullanici, %4 makine, %5 yazici, %8 sayfa
    static void LookupDocInfo(out string doc, out string pages)
    {
        doc = ""; pages = "";
        try
        {
            var q = new EventLogQuery("Microsoft-Windows-PrintService/Operational",
                        PathType.LogName, "*[System[(EventID=307)]]") { ReverseDirection = true };
            using (var reader = new EventLogReader(q))
            {
                for (int i = 0; i < 50; i++)
                {
                    var ev = reader.ReadEvent();
                    if (ev == null) break;
                    if (ev.TimeCreated.HasValue && ev.TimeCreated.Value < DateTime.Now.AddMinutes(-3)) break;
                    var p = ev.Properties;
                    string yz = p.Count >= 8 ? Convert.ToString(p[4].Value) : "";
                    if (p.Count >= 8 &&
                        yz.StartsWith("Print360", StringComparison.OrdinalIgnoreCase) &&
                        yz.EndsWith("- " + user, StringComparison.OrdinalIgnoreCase))
                    {
                        doc = Convert.ToString(p[1].Value);
                        pages = Convert.ToString(p[7].Value);
                        return;
                    }
                }
            }
        }
        catch (Exception ex) { Log("Belge adi okunamadi (olay gunlugu): " + ex.Message); }
    }

    // PDF'in ilk MediaBox kaydindan kagit boyutunu tespit et (A4, A5, A3, Letter...)
    public static string DetectPaper(byte[] pdf)
    {
        try
        {
            string text = System.Text.Encoding.GetEncoding(28591).GetString(pdf);
            var m = Regex.Match(text, @"/MediaBox\s*\[\s*([\d.\-]+)\s+([\d.\-]+)\s+([\d.\-]+)\s+([\d.\-]+)\s*\]");
            if (!m.Success) return "";
            double x0 = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            double y0 = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            double x1 = double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            double y1 = double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
            double wmm = Math.Abs(x1 - x0) * 25.4 / 72.0, hmm = Math.Abs(y1 - y0) * 25.4 / 72.0;
            double a = Math.Min(wmm, hmm), b = Math.Max(wmm, hmm);   // dikey normalize
            var sizes = new[] {
                new { N = "A3",     W = 297.0, H = 420.0 },
                new { N = "A4",     W = 210.0, H = 297.0 },
                new { N = "A5",     W = 148.0, H = 210.0 },
                new { N = "A6",     W = 105.0, H = 148.0 },
                new { N = "Letter", W = 215.9, H = 279.4 },
                new { N = "Legal",  W = 215.9, H = 355.6 },
                new { N = "B4",     W = 250.0, H = 353.0 },
                new { N = "B5",     W = 176.0, H = 250.0 }
            };
            foreach (var s in sizes)
                if (Math.Abs(a - s.W) <= 6 && Math.Abs(b - s.H) <= 6) return s.N;
            return "Ozel " + Math.Round(a) + "x" + Math.Round(b) + "mm";
        }
        catch { return ""; }
    }

    // Olay gunlugu sayfa vermezse PDF'ten say
    public static int CountPages(byte[] pdf)
    {
        try
        {
            string text = System.Text.Encoding.GetEncoding(28591).GetString(pdf);
            int n = Regex.Matches(text, @"/Type\s*/Page[^s]").Count;
            return n > 0 ? n : 1;
        }
        catch { return 1; }
    }

    // Istemcideki basildi-kayitlarini sunucuya geri cek (cift tarafli senkron)
    // ---- VC ters yon: istemciden RDP kanalindan gelen mesajlari SQL'e isle ----
    // Dashboard'daki HTTP islevleriyle ayni tablolara yazar (Printed/Heartbeat/PrinterHealth),
    // boylece VC modunda da panel/raporlar aynen calisir (HTTPS gerekmez).
    static void VChannelMesajIsle(string tur, string icerik)
    {
        try
        {
            if (tur == "SAYAC")
            {
                // "date","machine","fileName","printer","durum"
                var f = SplitCsv(icerik.Trim());
                if (f.Length < 5) return;
                DateTime pt;
                if (!DateTime.TryParseExact(f[0], "yyyy-MM-dd HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out pt)) pt = DateTime.Now;
                Db.Exec("IF NOT EXISTS(SELECT 1 FROM Printed WHERE Dosya=@f AND Makine=@m AND Durum=@d) " +
                        "INSERT INTO Printed(Tarih,Makine,Dosya,Yazici,Durum) VALUES(@t,@m,@f,@y,@d)",
                    "@t", pt, "@m", f[1], "@f", f[2], "@y", f[3], "@d", f[4]);
                if (f[4].StartsWith("HATA", StringComparison.OrdinalIgnoreCase))
                    Db.Alert("Yazdirma", "Is BASILAMADI (VC): " + f[1] + " / " + f[2] + " (" + f[3] + ") - " + f[4]);
                Log("VC SAYAC: " + f[1] + " / " + f[2] + " (" + f[4] + ")");
            }
            else if (tur == "HB")
            {
                // machine=..;user=..;printer=..;os=..;ver=..
                var kv = ParsePairs(icerik);
                string makine = Al(kv, "machine"), yazici = Al(kv, "printer");
                string kUser = Al(kv, "user"), os = Al(kv, "os");
                if (makine.Length == 0) return;
                Db.Exec("IF EXISTS(SELECT 1 FROM Heartbeat WHERE Makine=@m) " +
                        "UPDATE Heartbeat SET SonGorulme=GETDATE(), " +
                        "Yazici=CASE WHEN @y='' THEN Yazici ELSE @y END, " +
                        "KullaniciAdi=CASE WHEN @u='' THEN KullaniciAdi ELSE @u END, " +
                        "OS=CASE WHEN @o='' THEN OS ELSE @o END WHERE Makine=@m " +
                        "ELSE INSERT INTO Heartbeat(Makine,SonGorulme,Yazici,IP,KullaniciAdi,OS) VALUES(@m,GETDATE(),@y,'',@u,@o)",
                    "@m", makine, "@y", yazici, "@u", kUser, "@o", os);
            }
            else if (tur == "YAZICI")
            {
                // ilk satir = makine, sonraki satirlar: "yazici","durum","hata","kuyruk"
                var satirlar = icerik.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (satirlar.Length < 2) return;
                string makine = satirlar[0].Trim();
                for (int i = 1; i < satirlar.Length; i++)
                {
                    var f = SplitCsv(satirlar[i]);
                    if (f.Length < 4) continue;
                    string yazici = f[0], durum = f[1], hata = f[2];
                    int q; int.TryParse(f[3], out q);
                    Db.Exec("IF EXISTS(SELECT 1 FROM PrinterHealth WHERE Makine=@m AND Yazici=@y) " +
                            "UPDATE PrinterHealth SET Durum=@d, Hata=@h, Kuyruk=@q, Guncelleme=GETDATE() WHERE Makine=@m AND Yazici=@y " +
                            "ELSE INSERT INTO PrinterHealth(Makine,Yazici,Durum,Hata,Kuyruk,Guncelleme) VALUES(@m,@y,@d,@h,@q,GETDATE())",
                        "@m", makine, "@y", yazici, "@d", durum, "@h", hata, "@q", q);
                }
            }
        }
        catch (Exception ex) { Log("VC mesaj islenemedi (" + tur + "): " + ex.Message); }
    }

    static System.Collections.Generic.Dictionary<string, string> ParsePairs(string s)
    {
        var d = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in (s ?? "").Split(';'))
        {
            int eq = p.IndexOf('=');
            if (eq > 0) d[p.Substring(0, eq).Trim()] = p.Substring(eq + 1).Trim();
        }
        return d;
    }
    static string Al(System.Collections.Generic.Dictionary<string, string> d, string k)
    {
        return d.ContainsKey(k) ? d[k] : "";
    }

    static void PullClientStats()
    {
        string statsDir = FindClientDir("stats", false);
        if (statsDir == null) return;
        string src = Path.Combine(statsDir, "printed.csv");
        if (!File.Exists(src)) return;
        string machine = clientName.Length > 0 ? clientName : user;
        string dst = Path.Combine(clientsDir, Sanitize(machine) + ".csv");
        try { File.Copy(src, dst, true); }
        catch (Exception ex) { Log("Istatistik cekilemedi: " + ex.Message); }
    }

    static string FindClientDir(string sub, bool create)
    {
        foreach (char d in "CDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            string root = @"\\tsclient\" + d;
            try
            {
                if (!Directory.Exists(root)) continue;
                string dir = Path.Combine(root, @"Print360\" + sub);
                if (create) Directory.CreateDirectory(dir);
                else if (!Directory.Exists(dir)) return null;
                return dir;
            }
            catch { }
        }
        return null;
    }

    static string Sanitize(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    static void AppendCsv(string path, string[] fields)
    {
        lock (csvLock)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append((fields[i] ?? "").Replace("\"", "\"\"")).Append('"');
            }
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { File.AppendAllText(path, sb.ToString() + "\r\n"); return; }
                catch { Thread.Sleep(200); } // baska oturum yaziyor olabilir
            }
        }
    }

    // Basit CSV satir cozumleme (tirnakli alan destekli)
    static string[] SplitCsv(string line)
    {
        var fields = new System.Collections.Generic.List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQ = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQ)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else if (c == '"') inQ = false;
                else cur.Append(c);
            }
            else
            {
                if (c == '"') inQ = true;
                else if (c == ',') { fields.Add(cur.ToString()); cur.Length = 0; }
                else cur.Append(c);
            }
        }
        fields.Add(cur.ToString());
        return fields.ToArray();
    }

    static void AppendLine(string path, string line)
    {
        lock (csvLock)
        {
            for (int i = 0; i < 5; i++)
            {
                try { File.AppendAllText(path, line + "\r\n"); return; }
                catch { Thread.Sleep(200); }
            }
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
}
