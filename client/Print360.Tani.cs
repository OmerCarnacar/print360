// ============================================================
//  Print360 - ISTEMCI TANI PAKETI ve HATA AYIKLAMA MODU
//  Gelistirici : Omer CARNACAR  <omer.carnacar@outlook.com.tr>
//  Lisans      : UCRETSIZ SURUM - para ile satilamaz (bkz. LICENSE)
// ============================================================
// "Bazi makinelerde calismiyor" sorunu, o makineyi digerlerinden ayiran
// kosullari bilmeden cozulemez. Tani paketi bu kosullarin TAMAMINI tek bir
// zip'te toplar; kullanici tek tikla uretir, BT birimi uzaktan
// "Print360.ClientAgent.exe --tani" ile uretir.
//
// ICERIK: rapor.txt (asagidaki 11 bolum) + gunlukler + maskelenmis ayarlar +
//         klasor dokumleri (yalnizca dosya ADLARI).
// GIZLILIK: belge ICERIKLERI (PDF'ler) pakete GIRMEZ; istemci sifresi maskelenir.
// Her yoklama kendi try/catch'i ve zaman siniri icindedir: bir bolum
// basarisiz olsa da paket uretilir; basarisizligin kendisi de bilgidir.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

static partial class ClientAgent
{
    // Hata ayiklama modunda ayrintili satir ("[AYRINTI]" onekiyle).
    static void Ayr(string m)
    {
        if (Ortak.HataAyiklamaAcik()) Log("[AYRINTI] " + m);
    }

    static string Maskeli(string url)
    {
        if (url == null) return "";
        int i = url.IndexOf("key=", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return url;
        int j = url.IndexOf('&', i);
        return url.Substring(0, i) + "key=***" + (j > 0 ? url.Substring(j) : "");
    }

    // ------------------------------------------------------------------
    // Hata ayiklama modunu ac/kapat (tepsi menusu). Acilinca ortamin anlik
    // ozeti hemen gunluge yazilir: sonrasindaki ayrintili satirlar bu
    // ozetle birlikte okunur.
    // ------------------------------------------------------------------
    static void HataAyiklamaDegistir()
    {
        bool ac = !Ortak.HataAyiklamaAcik();
        string hata = Ortak.HataAyiklamaAyarla(ac, TimeSpan.FromHours(24));
        if (hata != null) { Log("Hata ayiklama modu degistirilemedi: " + hata); Bildir("Hata ayiklama", "Degistirilemedi: " + hata, false); return; }
        if (ac)
        {
            Log("=== HATA AYIKLAMA MODU ACILDI (24 saat; bitis " + Ortak.HataAyiklamaBitisi().ToString("yyyy-MM-dd HH:mm") + ") ===");
            new Thread(delegate()
            {
                try { foreach (var satir in TaniRaporu(false).Split('\n')) if (satir.Trim().Length > 0) Log("[AYRINTI] " + satir.TrimEnd('\r')); }
                catch (Exception ex) { Log("Ortam ozeti alinamadi: " + ex.Message); }
            }) { IsBackground = true }.Start();
            Bildir("Hata ayiklama modu ACIK", "24 saat boyunca her adim ayrintili kaydedilir. Sorunu tekrarlayin, sonra 'Tani paketi olustur'.", true);
        }
        else
        {
            Log("=== HATA AYIKLAMA MODU KAPATILDI ===");
            Bildir("Hata ayiklama modu kapali", "Gunluk olagan ayrintiya dondu.", true);
        }
    }

    // ------------------------------------------------------------------
    // TANI PAKETI: masaustune zip. Uretilen dosyanin yolunu doner.
    // ------------------------------------------------------------------
    internal static string TaniPaketiOlustur(string hedefKlasor)
    {
        if (string.IsNullOrEmpty(hedefKlasor))
            hedefKlasor = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(hedefKlasor) || !Directory.Exists(hedefKlasor)) hedefKlasor = Path.Combine(baseDir, "logs");
        Directory.CreateDirectory(hedefKlasor);
        string ad = "Print360-Tani-" + Environment.MachineName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip";
        string zipYol = Path.Combine(hedefKlasor, ad);
        if (File.Exists(zipYol)) File.Delete(zipYol);
        Log("Tani paketi hazirlaniyor: " + zipYol);

        using (var zip = ZipFile.Open(zipYol, ZipArchiveMode.Create))
        {
            Ekle(zip, "rapor.txt", TaniRaporu(true));

            // Gunlukler (asil + devredilmis + yazilamadiginda kullanilan yedekler)
            foreach (var f in new[] { logFile, logFile + ".1" })
                DosyaEkle(zip, f, "logs/" + Path.GetFileName(f));
            try
            {
                foreach (var f in Directory.GetFiles(Path.GetTempPath(), "Print360-*"))
                    DosyaEkle(zip, f, "yedek-gunlukler/" + Path.GetFileName(f));
            }
            catch { }
            try
            {
                foreach (var f in Directory.GetFiles(Path.Combine(baseDir, "logs"), "kurulum*.*"))
                    DosyaEkle(zip, f, "logs/" + Path.GetFileName(f));
            }
            catch { }

            // Ayarlar (sifre maskeli)
            try
            {
                if (File.Exists(iniFile))
                    Ekle(zip, "Print360.ini (maskeli).txt",
                         string.Join("\r\n", File.ReadAllLines(iniFile).Select(l => Ortak.GizliMaskele(l)).ToArray()));
            }
            catch (Exception ex) { Ekle(zip, "Print360.ini (OKUNAMADI).txt", ex.Message); }

            // Yazici oncelik listeleri ve bekleyen bildirimler (belge ADLARI icerebilir)
            try
            {
                foreach (var f in Directory.GetFiles(Path.Combine(baseDir, "stats"), "yazici-*.txt"))
                    DosyaEkle(zip, f, "stats/" + Path.GetFileName(f));
                DosyaEkle(zip, BekleyenBildirimDosyasi, "stats/" + Path.GetFileName(BekleyenBildirimDosyasi));
            }
            catch { }

            // Klasor dokumleri: yalnizca ADLAR, boyutlar, tarihler - icerik DEGIL
            Ekle(zip, "klasorler.txt", KlasorDokumu());
        }
        Log("Tani paketi hazir: " + zipYol + " (" + Math.Max(1, new FileInfo(zipYol).Length / 1024) + " KB)");
        try { File.WriteAllText(Path.Combine(baseDir, "logs", "tani-son.txt"), zipYol); } catch { }
        return zipYol;
    }

    static void Ekle(ZipArchive zip, string ad, string icerik)
    {
        var e = zip.CreateEntry(ad);
        using (var w = new StreamWriter(e.Open(), new UTF8Encoding(true))) w.Write(icerik ?? "");
    }

    static void DosyaEkle(ZipArchive zip, string yol, string ad)
    {
        try
        {
            if (!File.Exists(yol)) return;
            var e = zip.CreateEntry(ad);
            // Gunluk o anda yaziliyor olabilir: paylasimli oku
            using (var kaynak = new FileStream(yol, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var hedef = e.Open()) kaynak.CopyTo(hedef);
        }
        catch (Exception ex) { try { Ekle(zip, ad + " (OKUNAMADI).txt", ex.Message); } catch { } }
    }

    static string KlasorDokumu()
    {
        var sb = new StringBuilder();
        foreach (var k in new[] { jobsDir, doneDir, failedDir, vcOutbox, Path.Combine(baseDir, "logs"), Path.Combine(baseDir, "stats") })
        {
            sb.AppendLine("== " + k);
            try
            {
                if (!Directory.Exists(k)) { sb.AppendLine("   (yok)"); continue; }
                var fs = new DirectoryInfo(k).GetFiles().OrderByDescending(f => f.LastWriteTime).ToList();
                sb.AppendLine("   " + fs.Count + " dosya");
                foreach (var f in fs.Take(40))
                    sb.AppendLine("   " + f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + "  " + (f.Length / 1024).ToString().PadLeft(7) + " KB  " + f.Name);
                if (fs.Count > 40) sb.AppendLine("   ... ve " + (fs.Count - 40) + " dosya daha");
            }
            catch (Exception ex) { sb.AppendLine("   OKUNAMADI: " + ex.Message); }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // TANI RAPORU. agTesti=false: hata ayiklama acilisindaki hizli ozet
    // (ag testleri atlanir). Her bolum bagimsizdir.
    // ------------------------------------------------------------------
    internal static string TaniRaporu(bool agTesti)
    {
        var r = new StringBuilder();
        // BULGULAR: bolumler okunurken tespit edilen SORUNLAR; raporun en ustune
        // yazilir. Raporu okuyan kisinin 11 bolumu tek tek yorumlamasi gerekmesin.
        var bulgular = new List<string>();
        Action<string> Bulgu = m => bulgular.Add(m);
        Action<string> B = baslik => r.AppendLine().AppendLine("==== " + baslik + " ====");
        Action<string, Action> Bolum = (baslik, is_) =>
        {
            B(baslik);
            try { is_(); } catch (Exception ex) { r.AppendLine("  (BU BOLUM ALINAMADI: " + ex.Message + ")"); }
        };

        // 0) Gunlukteki son sorunlar - once bunu oku
        Bolum("0. SON UYARI VE HATALAR (client.log)", () =>
        {
            var satirlar = new List<string>();
            foreach (var f in new[] { logFile + ".1", logFile })
            {
                if (!File.Exists(f)) continue;
                using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs))
                {
                    string l;
                    while ((l = sr.ReadLine()) != null)
                        if (l.Contains("UYARI") || l.Contains("HATA") || l.Contains("BASILAMADI") || l.Contains("ALINAMADI")
                            || l.Contains("BASLANGIC SORUNU") || l.Contains("KALDI") || l.Contains("TAKILDI") || l.Contains("DEGISTI")
                            || l.Contains("REDDEDILDI") || l.Contains("BEKLETILIYOR"))
                            satirlar.Add(l);
                }
            }
            if (satirlar.Count == 0) r.AppendLine("  (uyari/hata satiri yok)");
            foreach (var l in satirlar.Skip(Math.Max(0, satirlar.Count - 60))) r.AppendLine("  " + l);
        });

        Bolum("1. BILGISAYAR VE KULLANICI", () =>
        {
            r.AppendLine("  Bilgisayar adi     : " + Environment.MachineName + " (DNS: " + SafeDns() + ")");
            r.AppendLine("  Kurulum kimligi    : " + KurulumKimligi());
            r.AppendLine("  Isletim sistemi    : " + IsletimSistemi() + " | 64-bit isletim: " + Environment.Is64BitOperatingSystem
                         + " | 64-bit surec: " + Environment.Is64BitProcess);
            r.AppendLine("  .NET Framework     : " + NetSurumu() + " (CLR " + Environment.Version + ")");
            r.AppendLine("  Calisma suresi     : " + TimeSpan.FromMilliseconds(Environment.TickCount & int.MaxValue).ToString(@"d\.hh\:mm") + " (gun.saat:dk)");
            r.AppendLine("  Bolge / kod sayfasi: " + System.Globalization.CultureInfo.CurrentCulture.Name + " / ANSI " + Encoding.Default.WebName
                         + " / OEM " + System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            r.AppendLine("  Ajan hesabi        : " + Environment.UserDomainName + "\\" + Environment.UserName);
            string sahip = "";
            IntPtr b; uint n;
            if (WTSQuerySessionInformation(IntPtr.Zero, -1, 5, out b, out n))
                try { sahip = (System.Runtime.InteropServices.Marshal.PtrToStringUni(b) ?? "").Trim(); } finally { WTSFreeMemory(b); }
            if (sahip.Length > 0 && !sahip.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase))
                Bulgu("AJAN BASKA HESAPLA CALISIYOR ('" + Environment.UserName + "', oturum sahibi '" + sahip + "'): yazicilar o hesabinkiler. Oturumu kapatip acin.");
            if (NetSurumu().Contains("ALTI") || NetSurumu().StartsWith("OKUNAMADI"))
                Bulgu(".NET Framework 4.5+ YOK ya da okunamadi: " + NetSurumu());
            r.AppendLine("  Oturum sahibi      : " + sahip + " (oturum " + Process.GetCurrentProcess().SessionId + ")"
                         + (sahip.Length > 0 && !sahip.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase) ? "  <-- AJAN BASKA HESAPLA CALISIYOR" : ""));
            bool yuksek = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                          .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            r.AppendLine("  Yonetici (yukseltilmis): " + (yuksek ? "EVET  <-- gerekli degil, yazicilar farkli gorunebilir" : "hayir"));
            r.AppendLine("  Hata ayiklama modu : " + (Ortak.HataAyiklamaAcik() ? "ACIK (bitis " + Ortak.HataAyiklamaBitisi().ToString("yyyy-MM-dd HH:mm") + ")" : "kapali"));
        });

        Bolum("2. PRINT360 AJANLARI", () =>
        {
            foreach (var p in Process.GetProcessesByName("Print360.ClientAgent"))
            {
                string bas = ""; try { bas = p.StartTime.ToString("yyyy-MM-dd HH:mm:ss"); } catch { }
                r.AppendLine("  surec " + p.Id + " | oturum " + p.SessionId + " | baslangic " + bas + (p.Id == Process.GetCurrentProcess().Id ? "  (bu surec)" : ""));
            }
            int oturumlar = Process.GetProcessesByName("Print360.ClientAgent").Select(p => p.SessionId).Distinct().Count();
            if (oturumlar > 1) r.AppendLine("  NOT: " + oturumlar + " ayri oturumda ajan var (ayni PC'de birden fazla kullanici) - is kilidi cift ciktiyi onler.");
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            r.AppendLine("  Calisan dosya      : " + exe + " (" + FileVersionInfo.GetVersionInfo(exe).FileVersion + ")");
            string run = RunDegeri("Print360ClientAgent");
            if (run.StartsWith("YOK")) Bulgu("OTOMATIK BASLATMA KAYDI YOK: ajan oturum acilisinda baslamaz. Istemci kurulumunu tekrarlayin.");
            r.AppendLine("  Otomatik baslatma  : " + run);
            foreach (var x in new[] { "update.cmd", "Print360.ClientAgent.exe.new" })
                if (File.Exists(Path.Combine(baseDir, x))) r.AppendLine("  DIKKAT: yarim kalmis guncelleme dosyasi var: " + x);
        });

        Bolum("3. AYARLAR VE SUNUCU ADRESLERI", () =>
        {
            var cfg = ReadIni();
            foreach (var kv in cfg) r.AppendLine("  " + Ortak.GizliMaskele(kv.Key + "=" + kv.Value));
            r.AppendLine("  RDP kanal modu (VC): " + (VcAcik(cfg) ? "ACIK - isler RDP kanalindan" : "kapali - isler HTTPS'ten"));
            var sun = SunucuAdresleri(cfg);
            r.AppendLine("  Denenecek sunucular: " + (sun.Count == 0 ? "(YOK - Server bos ve acik RDP oturumu bulunamadi)" : string.Join(" , ", sun.ToArray())));
            if (sun.Count == 0) Bulgu("SUNUCU ADRESI YOK: Print360.ini'de Server bos ve acik RDP baglantisi bulunamadi - ajan hicbir sunucuyu yoklamiyor.");
            r.AppendLine("  Baglanti yolu      : " + (Ortak.DogrudanBaglan ? "DOGRUDAN (vekil atlanarak)" : "Windows vekil ayari"));
        });

        Bolum("4. ACIK RDP BAGLANTILARI", () =>
        {
            int n = 0;
            foreach (var c in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections())
                if (c.RemoteEndPoint.Port == 3389 || c.LocalEndPoint.Port == 3389)
                { r.AppendLine("  " + c.LocalEndPoint + " -> " + c.RemoteEndPoint + " " + c.State); n++; }
            if (n == 0) r.AppendLine("  (3389 portunda baglanti yok - RDP oturumu acik degil ya da farkli port/RD Gateway kullaniliyor)");

            // RDP ile baglanilan sunucular Print360'in yokladigi listede mi?
            var cfg = ReadIni();
            var yoklanan = new HashSet<string>();
            foreach (var url in SunucuAdresleri(cfg))
                try { foreach (var ip in Dns.GetHostAddresses(new Uri(url).Host)) yoklanan.Add(ip.ToString()); } catch { }
            var rdpSunucular = new HashSet<string>();
            foreach (var c in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections())
                if (c.RemoteEndPoint.Port == 3389 && c.State == System.Net.NetworkInformation.TcpState.Established)
                    rdpSunucular.Add(c.RemoteEndPoint.Address.ToString());
            var disarida = rdpSunucular.Where(x => !yoklanan.Contains(x)).ToList();
            if (rdpSunucular.Count > 0 && disarida.Count > 0)
            {
                r.AppendLine("  Print360'in YOKLAMADIGI RDP sunuculari: " + string.Join(", ", disarida.ToArray()));
                if (!VcAcik(cfg))
                    Bulgu("RDP ile baglanilan " + disarida.Count + " sunucu (" + string.Join(", ", disarida.Take(5).ToArray()) + (disarida.Count > 5 ? "..." : "")
                        + ") Print360'in yokladigi sunucular arasinda YOK ve RDP kanal eklentisi de calismiyor: bu sunuculardan yazdirilan isler "
                        + "bu makineye ULASMAZ. Print360.ini'deki Server satirini bosaltin (ajan RDP baglantilarindan bulur) ya da sunuculari virgulle ekleyin.");
            }
            string aktif = Path.Combine(vcOutbox, ".vc-aktif");
            if (rdpSunucular.Count > 0 && !File.Exists(aktif) && EklentiKayitli())
                Bulgu("RDP BAGLANTISI VAR AMA KANAL EKLENTISI YUKLENMEMIS: eklenti kayitli ama hicbir RDP oturumu onu yuklememis. "
                    + "mstsc yerine baska bir RDP programi (Remote Desktop Connection Manager, Royal TS, Windows App) kullaniliyor olabilir "
                    + "- bu programlar eklentiyi yuklemez; isler yalnizca HTTPS ile gelir.");
        });

        if (agTesti)
            Bolum("5. AG TESTLERI (her sunucu icin)", () =>
            {
                var cfg = ReadIni();
                var sun = SunucuAdresleri(cfg);
                if (sun.Count == 0) { r.AppendLine("  (test edilecek sunucu yok)"); return; }
                foreach (var url in sun) AgTesti(r, url);
            });

        foreach (var satir in r.ToString().Split('\n'))
        {
            string l = satir.Trim();
            if (l.StartsWith("TCP ") && l.Contains("ULASILAMADI") && l.Contains("panel portu"))
                Bulgu("PANEL PORTU KAPALI: " + l + " - sunucudaki guvenlik duvari ya da panel calismiyor.");
            if (l.StartsWith("DNS COZULEMEDI")) Bulgu("SUNUCU ADI COZULEMEDI: " + l);
            if (l.StartsWith("HTTPS /api/clientversion:") && !l.Contains(": OK")) Bulgu("SUNUCUYA HTTPS ILE ULASILAMIYOR: " + l);
            if (l.StartsWith("HTTPS /api/surum: OK") && l.Contains("-> <"))
                Bulgu("SUNUCU ESKI SURUMDE: /api/surum tanimiyor (2026.09.30 oncesi). Sunucu tarafindaki duzeltmeler etkin degil - sunucuyu guncelleyin.");
            if (l.StartsWith("HTTPS /api/clientversion: OK"))
            {
                int ok = l.IndexOf("-> ");
                string sv = ok > 0 ? l.Substring(ok + 3).Trim() : "";
                Version a, b;
                if (Version.TryParse(sv, out a) && Version.TryParse(Surum.V, out b) && a < b)
                    Bulgu("SUNUCU ISTEMCIDEN ESKI: sunucu " + sv + ", istemci " + Surum.V + ". Sunucuyu ayni surume guncelleyin.");
            }
        }

        Bolum("6. TLS / GUVENLI BAGLANTI AYARLARI", () =>
        {
            r.AppendLine("  .NET SecurityProtocol: " + ServicePointManager.SecurityProtocol);
            r.AppendLine("  .NET 4 SchUseStrongCrypto (64/32): " + Kayit(@"SOFTWARE\Microsoft\.NETFramework\v4.0.30319", "SchUseStrongCrypto", false)
                         + " / " + Kayit(@"SOFTWARE\Microsoft\.NETFramework\v4.0.30319", "SchUseStrongCrypto", true));
            r.AppendLine("  SChannel TLS 1.2 istemci Enabled: " + Kayit(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client", "Enabled", false)
                         + " (bos = Windows varsayilani)");
            var cfg = ReadIni();
            r.AppendLine("  Sertifika sabitleme (CertHash): " + (cfg.ContainsKey("CertHash") && cfg["CertHash"].Trim().Length > 0 ? cfg["CertHash"].Trim() : "(yok - her sertifika kabul)"));
        });

        Bolum("7. RDP KANAL EKLENTISI", () =>
        {
            foreach (var g in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
            {
                try
                {
                    using (var hk = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, g))
                    using (var k = hk.OpenSubKey(@"SOFTWARE\Microsoft\Terminal Server Client\Default\AddIns\Print360"))
                    {
                        string ad = k == null ? null : Convert.ToString(k.GetValue("Name"));
                        r.AppendLine("  Kayit (" + (g == Microsoft.Win32.RegistryView.Registry64 ? "64-bit" : "32-bit") + "): "
                                     + (k == null ? "yok" : ad + (File.Exists(ad) ? " [dosya var, " + (DllX64(ad) ? "x64" : "x86") + "]" : " [DOSYA YOK!]")));
                    }
                }
                catch (Exception ex) { r.AppendLine("  Kayit okunamadi: " + ex.Message); }
            }
            string mstsc = Path.Combine(Environment.SystemDirectory, "mstsc.exe");
            if (File.Exists(mstsc)) r.AppendLine("  mstsc.exe: " + FileVersionInfo.GetVersionInfo(mstsc).FileVersion);
            string aktif = Path.Combine(vcOutbox, ".vc-aktif");
            r.AppendLine("  Eklenti etkin isareti: " + (File.Exists(aktif) ? "VAR (" + File.GetLastWriteTime(aktif).ToString("yyyy-MM-dd HH:mm:ss") + ")" : "YOK - eklenti bu oturumda yuklenmemis"));
            int msg = Directory.Exists(vcOutbox) ? Directory.GetFiles(vcOutbox, "*.msg").Length : 0;
            r.AppendLine("  Kanalda bekleyen mesaj: " + msg + (msg > 3 ? "  <-- birikiyor: eklenti mesajlari tuketmiyor" : ""));
        });

        Bolum("8. YAZICILAR", () =>
        {
            using (var sv = new System.Management.ManagementObjectSearcher("SELECT State, StartMode FROM Win32_Service WHERE Name='Spooler'"))
                foreach (System.Management.ManagementObject o in sv.Get())
                {
                    r.AppendLine("  Yazdirma biriktiricisi (Spooler): " + o["State"] + " / " + o["StartMode"]);
                    if (!Convert.ToString(o["State"]).Equals("Running", StringComparison.OrdinalIgnoreCase))
                        Bulgu("YAZDIRMA BIRIKTIRICISI (Spooler) CALISMIYOR: hicbir sey basilamaz. Hizmetler'den baslatin.");
                }
            string vars = ""; try { vars = new System.Drawing.Printing.PrinterSettings().PrinterName; } catch { }
            r.AppendLine("  Varsayilan yazici: " + vars);
            var sira = KisiselYaziciListesi();
            string ilk = sira.Count > 0 ? sira[0] : vars;
            if (ilk.Length > 0)
            {
                string d = YaziciAnlikDurum(ilk);
                if (d != null) Bulgu("CIKTILARIN GIDECEGI YAZICI SORUNLU: '" + ilk + "' -> " + d + ". Isler Windows kuyrugunda bekler; yaziciyi acin/baglayin.");
            }
            else Bulgu("BU BILGISAYARDA VARSAYILAN YAZICI YOK.");
            r.AppendLine("  Kisisel oncelik listesi: " + string.Join(" > ", KisiselYaziciListesi().ToArray()));
            using (var ps = new System.Management.ManagementObjectSearcher(
                       "SELECT Name, PrinterStatus, WorkOffline, DetectedErrorState, DriverName, PortName, Network, Shared FROM Win32_Printer"))
                foreach (System.Management.ManagementObject p in ps.Get())
                {
                    string ad = Convert.ToString(p["Name"]);
                    string durum = YaziciAnlikDurum(ad);
                    r.AppendLine("  - " + ad + (ad == vars ? "  [VARSAYILAN]" : ""));
                    r.AppendLine("      durum: " + (durum ?? "hazir") + " | surucu: " + p["DriverName"] + " | port: " + p["PortName"]
                                 + " | ag: " + p["Network"]);
                }
            using (var js = new System.Management.ManagementObjectSearcher("SELECT Name, JobStatus, Status, TotalPages, TimeSubmitted FROM Win32_PrintJob"))
            {
                int n = 0;
                foreach (System.Management.ManagementObject j in js.Get())
                { r.AppendLine("  Windows kuyrugunda: " + j["Name"] + " | " + j["JobStatus"] + " / " + j["Status"] + " | " + j["TotalPages"] + " sayfa"); n++; }
                if (n == 0) r.AppendLine("  Windows yazdirma kuyrugu bos.");
            }
        });

        Bolum("9. YAZDIRMA MOTORU", () =>
        {
            var cfg = ReadIni();
            string sumatra = cfg.ContainsKey("SumatraPath") ? cfg["SumatraPath"] : Path.Combine(baseDir, "SumatraPDF.exe");
            if (!File.Exists(sumatra) && PdfIliskisi().Contains("calismaz"))
                Bulgu("YAZDIRMA MOTORU YOK: SumatraPDF bulunamadi ve yedek motor icin PDF programi da yok - isler basilamaz.");
            r.AppendLine("  SumatraPDF: " + (File.Exists(sumatra) ? sumatra + " (" + FileVersionInfo.GetVersionInfo(sumatra).FileVersion + ", "
                         + new FileInfo(sumatra).Length / 1024 + " KB)" : "YOK (" + sumatra + ") - yalnizca yedek motor (printto) kullanilir"));
            r.AppendLine("  .pdf dosya iliskisi (yedek motor): " + PdfIliskisi());
        });

        Bolum("10. KLASORLER, IZINLER, DISK", () =>
        {
            var sorun = Ortak.KlasorDenetle(new[] { baseDir, jobsDir, doneDir, failedDir, Path.Combine(baseDir, "logs"), Path.Combine(baseDir, "stats") });
            if (sorun.Count == 0) r.AppendLine("  Tum klasorler yazilabilir, disk alani yeterli.");
            foreach (var s in sorun) { r.AppendLine("  " + s); Bulgu(s); }
            r.AppendLine("  jobs (yazdirilacak): " + Say(jobsDir) + " | failed (basilamadi): " + Say(failedDir)
                         + " | bekleyen bildirim: " + BekleyenBildirimSayisi());
            int jb; if (int.TryParse(Say(jobsDir), out jb) && jb > 0) Bulgu(jb + " IS YAZDIRILMAYI BEKLIYOR (jobs klasoru) - ajan calisiyor mu, yazici hazir mi?");
            if (BekleyenBildirimSayisi() > 0) Bulgu(BekleyenBildirimSayisi() + " 'basildi' bildirimi sunucuya iletilemedi (panelde 'Teslim edildi' gorunur).");
        });

        Bolum("11. GUVENLIK YAZILIMI / UYGULAMA DENETIMI OLAYLARI", () =>
        {
            foreach (var g in new[] { "Microsoft-Windows-AppLocker/EXE and DLL", "Microsoft-Windows-CodeIntegrity/Operational" })
            {
                string c = Komut("wevtutil.exe", "qe \"" + g + "\" /c:50 /rd:true /f:text", 15);
                var ilgili = c.Split('\n').Where(l => l.IndexOf("Print360", StringComparison.OrdinalIgnoreCase) >= 0
                                                    || l.IndexOf("Sumatra", StringComparison.OrdinalIgnoreCase) >= 0).Take(10).ToList();
                r.AppendLine("  " + g + ": " + (ilgili.Count == 0 ? "Print360 ile ilgili kayit yok" : ilgili.Count + " ilgili satir"));
                if (ilgili.Count > 0) Bulgu("UYGULAMA DENETIMI (" + g.Split('-')[2].Split('/')[0] + ") Print360/SumatraPDF ile ilgili kayit uretmis: engelleniyor olabilir.");
                foreach (var l in ilgili) r.AppendLine("     " + l.Trim());
            }
            string uyg = Komut("wevtutil.exe", "qe Application /q:\"*[System[Provider[@Name='.NET Runtime' or @Name='Application Error']]]\" /c:20 /rd:true /f:text", 15);
            var cokme = uyg.Split('\n').Where(l => l.IndexOf("Print360", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("Sumatra", StringComparison.OrdinalIgnoreCase) >= 0).Take(10).ToList();
            r.AppendLine("  Uygulama cokmeleri (Print360/Sumatra): " + (cokme.Count == 0 ? "yok" : cokme.Count + " satir"));
            foreach (var l in cokme) r.AppendLine("     " + l.Trim());
        });
        var son = new StringBuilder();
        son.AppendLine("PRINT360 ISTEMCI TANI RAPORU");
        son.AppendLine("Uretildi : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " (" + TimeZoneInfo.Local.DisplayName + ")");
        son.AppendLine("Surum    : " + Surum.V + " | bilgisayar " + Environment.MachineName);
        son.AppendLine();
        son.AppendLine("==== BULGULAR (otomatik) ====");
        if (bulgular.Count == 0) son.AppendLine("  Belirgin bir sorun bulunamadi. Ayrinti icin asagidaki bolumlere ve gunluge bakin.");
        for (int i = 0; i < bulgular.Count; i++) son.AppendLine("  " + (i + 1) + ") " + bulgular[i]);
        son.Append(r);
        return son.ToString();
    }

    // Tek sunucu icin: DNS, TCP portlari, vekil, HTTPS istegi - her biri sureyle
    static void AgTesti(StringBuilder r, string url)
    {
        Uri u;
        try { u = new Uri(url); } catch { r.AppendLine("  GECERSIZ ADRES: " + url); return; }
        r.AppendLine("  --- " + url);
        var sw = Stopwatch.StartNew();
        IPAddress[] ipler = new IPAddress[0];
        try
        {
            ipler = Dns.GetHostAddresses(u.Host);
            r.AppendLine("  DNS: " + string.Join(", ", ipler.Select(i => i.ToString()).ToArray()) + " (" + sw.ElapsedMilliseconds + " ms)");
        }
        catch (Exception ex) { r.AppendLine("  DNS COZULEMEDI: " + ex.Message + " (" + sw.ElapsedMilliseconds + " ms)"); return; }

        foreach (int port in new[] { u.Port, 8360, 3389 }.Distinct())
        {
            sw.Restart();
            try
            {
                using (var t = new TcpClient())
                {
                    var a = t.BeginConnect(u.Host, port, null, null);
                    bool ok = a.AsyncWaitHandle.WaitOne(4000) && t.Connected;
                    r.AppendLine("  TCP " + port + ": " + (ok ? "ACIK" : "ULASILAMADI (4 sn)") + " (" + sw.ElapsedMilliseconds + " ms)"
                                 + (port == u.Port && !ok ? "  <-- panel portu kapali: guvenlik duvari / panel calismiyor" : ""));
                }
            }
            catch (Exception ex) { r.AppendLine("  TCP " + port + ": HATA " + ex.Message); }
        }

        try
        {
            var sp = WebRequest.GetSystemWebProxy();
            bool atla = sp.IsBypassed(u);
            r.AppendLine("  Windows vekil ayari: " + (atla ? "bu adres icin vekil YOK (dogrudan)" : "vekil " + sp.GetProxy(u)));
        }
        catch (Exception ex) { r.AppendLine("  Vekil okunamadi: " + ex.Message); }

        foreach (var yol in new[] { "/api/clientversion", "/api/surum" })
        {
            sw.Restart();
            try
            {
                using (var wc = new P360WebClient())
                {
                    string y = wc.DownloadString(new Uri(u, yol));
                    if (y.Length > 300) y = y.Substring(0, 300) + "...";
                    r.AppendLine("  HTTPS " + yol + ": OK (" + sw.ElapsedMilliseconds + " ms) -> " + y.Replace("\r", " ").Replace("\n", " "));
                }
            }
            catch (Exception ex) { r.AppendLine("  HTTPS " + yol + ": " + HataAcikla(ex) + " (" + sw.ElapsedMilliseconds + " ms)"); }
        }
    }

    static string SafeDns() { try { return Dns.GetHostEntry("").HostName; } catch { return "?"; } }

    static string IsletimSistemi()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                return k.GetValue("ProductName") + " " + k.GetValue("DisplayVersion") + " (derleme " + k.GetValue("CurrentBuild") + "." + k.GetValue("UBR") + ")";
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    static string NetSurumu()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
            {
                int rel = Convert.ToInt32(k.GetValue("Release"));
                string ad = rel >= 528040 ? "4.8+" : rel >= 461808 ? "4.7.2" : rel >= 460798 ? "4.7" : rel >= 394802 ? "4.6.2"
                          : rel >= 393295 ? "4.6" : rel >= 379893 ? "4.5.2" : rel >= 378389 ? "4.5" : "4.5 ALTI (yetersiz)";
                return ad + " (Release " + rel + ")";
            }
        }
        catch { return "OKUNAMADI (4.5 alti olabilir)"; }
    }

    static string RunDegeri(string ad)
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))
            {
                object v = k == null ? null : k.GetValue(ad);
                return v == null ? "YOK  <-- ajan oturum acilisinda baslamaz" : Convert.ToString(v);
            }
        }
        catch (Exception ex) { return "okunamadi: " + ex.Message; }
    }

    static string Kayit(string yol, string ad, bool otuzIki)
    {
        try
        {
            using (var hk = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,
                       otuzIki ? Microsoft.Win32.RegistryView.Registry32 : Microsoft.Win32.RegistryView.Registry64))
            using (var k = hk.OpenSubKey(yol))
            {
                object v = k == null ? null : k.GetValue(ad);
                return v == null ? "(bos)" : Convert.ToString(v);
            }
        }
        catch { return "?"; }
    }

    static bool EklentiKayitli()
    {
        foreach (var g in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
            try
            {
                using (var hk = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, g))
                using (var k = hk.OpenSubKey(@"SOFTWARE\Microsoft\Terminal Server Client\Default\AddIns\Print360"))
                    if (k != null) return true;
            }
            catch { }
        return false;
    }

    static bool DllX64(string dosya)
    {
        try
        {
            using (var fs = new FileStream(dosya, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs))
            {
                fs.Position = 0x3C; fs.Position = br.ReadInt32();
                if (br.ReadUInt32() != 0x00004550) return true;
                ushort m = br.ReadUInt16();
                return m == 0x8664 || m == 0xAA64;
            }
        }
        catch { return true; }
    }

    static string PdfIliskisi()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(".pdf"))
            {
                string prog = k == null ? null : Convert.ToString(k.GetValue(""));
                if (string.IsNullOrEmpty(prog)) return "YOK - yedek motor calismaz";
                using (var p = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(prog + @"\shell\printto\command"))
                    return prog + (p == null ? " (printto komutu YOK - yedek motor calismaz)" : " (printto var)");
            }
        }
        catch (Exception ex) { return "okunamadi: " + ex.Message; }
    }

    static string Say(string k) { try { return Directory.Exists(k) ? Directory.GetFiles(k).Length.ToString() : "klasor yok"; } catch { return "?"; } }

    static string Komut(string exe, string args, int sn)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                                                        StandardOutputEncoding = Ortak.KonsolKodlamasi(), StandardErrorEncoding = Ortak.KonsolKodlamasi() };
            using (var p = Process.Start(psi))
            {
                var cikti = p.StandardOutput.ReadToEndAsync();
                p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(sn * 1000)) { try { p.Kill(); } catch { } return ""; }
                return cikti.Result;
            }
        }
        catch { return ""; }
    }
}
