// ============================================================
//  Print360 - RDP Yazdirma ve Yonetim Cozumu
//  Gelistirici : Omer CARNACAR  <omer.carnacar@outlook.com.tr>
//  Lisans      : UCRETSIZ SURUM - para ile satilamaz (bkz. LICENSE)
//  Telif       : (c) 2026 Omer CARNACAR
// ============================================================
// Print360 Dashboard v2
// RDP sunucusunda calisir; http://localhost:8360 adresinde raporlama paneli sunar.
// Sayfalar: Genel Bakis | Makineler (Active Directory takibi) | Periyotlar | Isler
// Veri kaynaklari:
//   C:\Print360\stats\jobs.csv        -> gonderilen isler (tarih,kullanici,makine,belge,sayfa,dosya)
//   C:\Print360\stats\clients\*.csv   -> istemcilerden geri cekilen "basildi" kayitlari
//   Active Directory                  -> etki alanindaki bilgisayarlar (System.DirectoryServices)
using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

static class Dashboard
{
    static string Port = "8360";
    static string PortSsl = "8443";
    static string jobsCsv = @"C:\Print360\stats\jobs.csv";
    static string clientsDir = @"C:\Print360\stats\clients";
    static string logFile = @"C:\Print360\logs\dashboard.log";
    static string pwdFile = @"C:\Print360\panel.pwd";   // SHA-256 ozeti (kurulumda olusur)
    static string hbFile = @"C:\Print360\stats\heartbeat.csv";
    static string connLog = @"C:\Print360\logs\connections.log";
    static string rulesCsv = @"C:\Print360\rules.csv";
    static Dictionary<string, DateTime> sessions = new Dictionary<string, DateTime>();
    static object hbLock = new object();
    static Dictionary<string, bool> pingCache; static DateTime pingTime = DateTime.MinValue;

    class Sent { public DateTime Time; public string User, Machine, Doc, Pages, File, Paper, Status; public int PageN, KbN; }
    class AdPc { public string Name, Os; public DateTime? LastLogon; }
    class AdUser { public string Sam, Ad; public bool Aktif; public DateTime? LastLogon; }

    static List<AdPc> adCache;
    static DateTime adCacheTime = DateTime.MinValue;
    static string adError = null;
    static List<AdUser> adUserCache;
    static DateTime adUserCacheTime = DateTime.MinValue;

    static void Main()
    {
        bool created;
        using (var mx = new Mutex(true, "Print360Dashboard", out created))
        {
            if (!created) return;
            Db.ConnStr();                 // db.ini'yi yukle (port ayarlari dahil)
            Port = Db.HttpPort; PortSsl = Db.HttpsPort;
            // Oncelik: HTTPS + HTTP -> yalniz HTTP -> yalniz localhost
            var listener = new HttpListener();
            try
            {
                listener.Prefixes.Add("http://+:" + Port + "/");
                listener.Prefixes.Add("https://+:" + PortSsl + "/");
                listener.Start();
                Log("HTTPS aktif: https://+:" + PortSsl);
            }
            catch
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://+:" + Port + "/");
                try { listener.Start(); Log("HTTPS baglanamadi, yalniz HTTP: " + Port); }
                catch
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add("http://localhost:" + Port + "/");
                    try { listener.Start(); }
                    catch (Exception ex) { Log("Baslatilamadi: " + ex.Message); return; }
                }
            }
            Log("Dashboard basladi: http://localhost:" + Port);
            Db.EnsureSchema();
            new Thread(AlertMonitor) { IsBackground = true }.Start();
            new Thread(MailMonitor) { IsBackground = true }.Start();
            while (true)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch (Exception ex) { Log("Dinleyici hatasi: " + ex.Message); continue; }

                // HER ISTEK AYRI IS PARCACIGINDA islenir.
                // Eskiden istek dongunun icinde, TEK is parcaciginda islenirdi:
                // yavas bir istemciye 184 KB'lik bir yazdirma isini yazarken sunucu
                // TAMAMEN blokleniyordu. Sonuc: istemci ilk isi aliyor, hemen
                // ikinciyi istiyor, sunucu hala birinciyi bitirmedigi icin istek
                // zaman asimina ugruyordu ("1. yazdirma tamam, sonrasi yok").
                // Ayni sebeple ikinci bir istemcinin kalp atisi ve panel de bekliyordu.
                ThreadPool.QueueUserWorkItem(delegate(object o) { IstegiIsle((HttpListenerContext)o); }, ctx);
            }
        }
    }

    // Tek bir HTTP istegini isler. Dinleyici dongusu bunu is parcacigi havuzuna
    // devreder; boylece yavas bir istek digerlerini bekletmez.
    static void IstegiIsle(HttpListenerContext ctx)
    {
        try
        {
                    // Istemci ajanlarindan gelen "basildi" kayitlari (merkezi sayac)
                    if (ctx.Request.HttpMethod == "POST" &&
                        ctx.Request.Url.AbsolutePath.Equals("/api/printed", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleIngest(ctx);
                        return;
                    }
                    if (ctx.Request.HttpMethod == "POST" &&
                        ctx.Request.Url.AbsolutePath.Equals("/api/heartbeat", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleHeartbeat(ctx);
                        return;
                    }
                    if (ctx.Request.HttpMethod == "POST" &&
                        ctx.Request.Url.AbsolutePath.Equals("/api/printers", StringComparison.OrdinalIgnoreCase))
                    {
                        HandlePrinterHealth(ctx);
                        return;
                    }
                    // Otomatik guncelleme: istemci ajanlari surum sorar / yeni binary'yi indirir (auth'suz)
                    if (ctx.Request.HttpMethod == "GET" &&
                        ctx.Request.Url.AbsolutePath.Equals("/api/clientversion", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] vb = Encoding.UTF8.GetBytes(Surum.V);
                        ctx.Response.ContentType = "text/plain";
                        ctx.Response.ContentLength64 = vb.Length;
                        ctx.Response.OutputStream.Write(vb, 0, vb.Length);
                        ctx.Response.Close();
                        return;
                    }
                    if (ctx.Request.HttpMethod == "GET" &&
                        ctx.Request.Url.AbsolutePath.Equals("/api/clientexe", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleClientExe(ctx);
                        return;
                    }
                    if (ctx.Request.HttpMethod == "GET" &&
                        ctx.Request.Url.AbsolutePath.Equals("/api/jobs", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleJobFetch(ctx);
                        return;
                    }
                    if (ctx.Request.HttpMethod == "POST" &&
                        ctx.Request.Url.AbsolutePath.Equals("/api/jobs/done", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleJobDone(ctx);
                        return;
                    }
                    string html;
                    try { html = Route(ctx); }
                    catch (Exception ex) { html = "<pre>Hata: " + H(ex.ToString()) + "</pre>"; }
                    if (html == null) return; // yanit (yonlendirme vb.) zaten gonderildi
                    byte[] body = Encoding.UTF8.GetBytes(html);
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    ctx.Response.ContentLength64 = body.Length;
                    ctx.Response.OutputStream.Write(body, 0, body.Length);
                    ctx.Response.Close();
        }
        catch (Exception ex) { Log("Istek hatasi: " + ex.Message); }
    }

    // Istemciden POST ile gelen CSV satirini makine dosyasina ekle
    static void HandleIngest(HttpListenerContext ctx)
    {
        try
        {
            string machine = Q(ctx, "machine") ?? "bilinmeyen";
            bool ilkKayitP;
            if (!ClientAuth(ctx, machine, out ilkKayitP)) return;
            foreach (char c in Path.GetInvalidFileNameChars()) machine = machine.Replace(c, '_');
            string body;
            using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = sr.ReadToEnd();
            if (body.Length > 0 && body.Length < 10000)
            {
                Directory.CreateDirectory(clientsDir);
                File.AppendAllText(Path.Combine(clientsDir, machine + ".csv"),
                    body.TrimEnd('\r', '\n') + "\r\n");
                // SQL'e de isle (tarih,makine,dosya,yazici,durum)
                foreach (var line in body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var f = ParseCsvLine(line);
                    if (f.Length < 5) continue;
                    DateTime pt;
                    if (!DateTime.TryParseExact(f[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out pt))
                        pt = DateTime.Now;
                    Db.Exec("IF NOT EXISTS(SELECT 1 FROM Printed WHERE Dosya=@f AND Makine=@m AND Durum=@d) " +
                            "INSERT INTO Printed(Tarih,Makine,Dosya,Yazici,Durum) VALUES(@t,@m,@f,@y,@d)",
                        "@t", pt, "@m", f[1], "@f", f[2], "@y", f[3], "@d", f[4]);
                    // Istemci basamadiysa panele uyari dus
                    if (f[4].StartsWith("HATA", StringComparison.OrdinalIgnoreCase))
                        Db.Alert("Yazdirma", "Is BASILAMADI: " + f[1] + " / " + f[2] + " (" + f[3] + ") - " + f[4]);
                }
            }
            byte[] ok = Encoding.UTF8.GetBytes("OK");
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength64 = ok.Length;
            ctx.Response.OutputStream.Write(ok, 0, ok.Length);
            ctx.Response.Close();
        }
        catch (Exception ex) { Log("Ingest hatasi: " + ex.Message); try { ctx.Response.Close(); } catch { } }
    }

    // ---------------- E-posta raporu ----------------
    // Ayarlar: C:\Print360\mail.ini (Smtp, Port, TLS, Kullanici, Sifre, Kimden, Kime, Saat)
    static string mailIni = @"C:\Print360\mail.ini";
    static string mailLastFile = @"C:\Print360\logs\mail-last.txt";
    public static string MailErr;
    static string lastMailResult;

    static Dictionary<string, string> MailCfg()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(mailIni)) return d;
            foreach (var l in File.ReadAllLines(mailIni))
            {
                var t = l.Trim();
                if (t.Length == 0 || t.StartsWith(";") || t.StartsWith("#")) continue;
                var p = t.Split(new[] { '=' }, 2);
                if (p.Length == 2) d[p[0].Trim()] = p[1].Trim();
            }
        }
        catch { }
        return d;
    }

    // Her gun ayarli saatte (varsayilan 08:00) gunluk ozet e-postasi gonder
    static void MailMonitor()
    {
        while (true)
        {
            try
            {
                var cfg = MailCfg();
                if (cfg.ContainsKey("Smtp") && cfg.ContainsKey("Kime"))
                {
                    string saat = cfg.ContainsKey("Saat") && cfg["Saat"].Length >= 4 ? cfg["Saat"] : "08:00";
                    string bugun = DateTime.Today.ToString("yyyy-MM-dd");
                    string sonGonderim = "";
                    try { if (File.Exists(mailLastFile)) sonGonderim = File.ReadAllText(mailLastFile).Trim(); } catch { }
                    if (DateTime.Now.ToString("HH:mm") == saat && sonGonderim != bugun)
                    {
                        if (SendReport())
                        {
                            File.WriteAllText(mailLastFile, bugun);
                            Log("Gunluk rapor e-postasi gonderildi: " + cfg["Kime"]);
                        }
                        else
                        {
                            File.WriteAllText(mailLastFile, bugun); // ayni gun tekrar tekrar denemesin
                            Log("Rapor e-postasi GONDERILEMEDI: " + (MailErr ?? ""));
                            Db.Alert("Eposta", "Gunluk rapor e-postasi gonderilemedi: " + (MailErr ?? ""));
                        }
                    }
                }
            }
            catch (Exception ex) { Log("MailMonitor: " + ex.Message); }
            Thread.Sleep(30000);
        }
    }

    static bool SendReport()
    {
        try
        {
            var cfg = MailCfg();
            if (!cfg.ContainsKey("Smtp") || !cfg.ContainsKey("Kime")) { MailErr = "mail.ini eksik (Smtp/Kime)"; return false; }
            int port = 587; if (cfg.ContainsKey("Port")) int.TryParse(cfg["Port"], out port);
            using (var sc = new System.Net.Mail.SmtpClient(cfg["Smtp"], port))
            {
                sc.EnableSsl = !cfg.ContainsKey("TLS") || cfg["TLS"] != "0";
                if (cfg.ContainsKey("Kullanici") && cfg["Kullanici"].Length > 0)
                    sc.Credentials = new System.Net.NetworkCredential(cfg["Kullanici"], cfg.ContainsKey("Sifre") ? cfg["Sifre"] : "");
                string kimden = cfg.ContainsKey("Kimden") && cfg["Kimden"].Length > 0 ? cfg["Kimden"]
                              : (cfg.ContainsKey("Kullanici") ? cfg["Kullanici"] : "print360@" + Environment.MachineName);
                using (var msg = new System.Net.Mail.MailMessage())
                {
                    msg.From = new System.Net.Mail.MailAddress(kimden, "Print360");
                    foreach (var adr in cfg["Kime"].Split(',', ';'))
                        if (adr.Trim().Length > 0) msg.To.Add(adr.Trim());
                    msg.Subject = "Print360 Günlük Rapor — " + DateTime.Today.AddDays(-1).ToString("dd.MM.yyyy");
                    msg.Body = BuildMailReport();
                    msg.IsBodyHtml = true;
                    msg.BodyEncoding = Encoding.UTF8;
                    msg.SubjectEncoding = Encoding.UTF8;
                    sc.Send(msg);
                }
            }
            MailErr = null;
            return true;
        }
        catch (Exception ex) { MailErr = ex.Message; return false; }
    }

    // Dunku gunun ozeti (HTML)
    static string BuildMailReport()
    {
        var dun = DateTime.Today.AddDays(-1);
        var all = LoadSent();
        var gun = all.Where(s => s.Time.Date == dun).ToList();
        var ok = gun.Where(s => s.Status == "OK").ToList();
        var printed = LoadPrinted();
        var hb = LoadHb();
        int cevrimdisi = 0;
        foreach (var kv in hb) { bool o; HbDurum(hb, kv.Key, out o); if (!o) cevrimdisi++; }
        object okunmamis = Db.Scalar("SELECT COUNT(*) FROM Alerts WHERE Okundu=0");
            if (okunmamis == null) okunmamis = Db.OkunmamisUyari();   // SQL yoksa dosyadan

        var sb = new StringBuilder();
        sb.Append("<div style='font-family:Segoe UI,Arial,sans-serif;max-width:640px'>");
        sb.Append("<h2 style='color:#1f3a5f'>&#128424; Print360 G&uuml;nl&uuml;k Rapor &mdash; ")
          .Append(dun.ToString("dd.MM.yyyy dddd", new CultureInfo("tr-TR"))).Append("</h2>");
        sb.Append("<table cellpadding='8' style='border-collapse:collapse;width:100%'>");
        Action<string, string> satir = (a, b) =>
            sb.Append("<tr><td style='border:1px solid #dde;background:#f4f7fb;width:55%'>").Append(a)
              .Append("</td><td style='border:1px solid #dde;font-weight:bold'>").Append(b).Append("</td></tr>");
        satir("Toplam &ccedil;&#305;kt&#305;", ok.Count.ToString());
        satir("Toplam sayfa", ok.Sum(s => s.PageN).ToString());
        var costsM = LoadCosts();
        if (costsM.Count > 0) satir("Tahmini maliyet", Para(ToplamMaliyet(ok, costsM)));
        satir("Bas&#305;ld&#305; onay&#305;", ok.Count(s => printed.ContainsKey(s.File)).ToString());
        satir("Engellenen i&#351;", (gun.Count - ok.Count).ToString());
        satir("Aktif makine", ok.Select(s => MachineKey(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString());
        satir("&Ccedil;evrimd&#305;&#351;&#305; makine (&#351;u an)", cevrimdisi.ToString());
        if (okunmamis != null) satir("Okunmam&#305;&#351; uyar&#305;", Convert.ToString(okunmamis));
        sb.Append("</table>");

        if (ok.Count > 0)
        {
            sb.Append("<h3 style='color:#1f3a5f'>En &Ccedil;ok Yazd&#305;ranlar</h3><ul>");
            foreach (var g in ok.GroupBy(s => s.User).OrderByDescending(g => g.Sum(x => x.PageN)).Take(5))
                sb.Append("<li>").Append(H(g.Key)).Append(" &mdash; ").Append(g.Count()).Append(" &ccedil;&#305;kt&#305;, ")
                  .Append(g.Sum(x => x.PageN)).Append(" sayfa")
                  .Append(costsM.Count > 0 ? " (" + Para(ToplamMaliyet(g, costsM)) + ")" : "").Append("</li>");
            sb.Append("</ul><h3 style='color:#1f3a5f'>Makine Bazl&#305;</h3><ul>");
            foreach (var g in ok.GroupBy(s => MachineKey(s)).OrderByDescending(g => g.Count()).Take(5))
                sb.Append("<li>").Append(H(g.Key)).Append(" &mdash; ").Append(g.Count()).Append(" &ccedil;&#305;kt&#305;, ")
                  .Append(g.Sum(x => x.PageN)).Append(" sayfa</li>");
            sb.Append("</ul>");
        }
        var engelli = gun.Where(s => s.Status != "OK").Take(10).ToList();
        if (engelli.Count > 0)
        {
            sb.Append("<h3 style='color:#c0392b'>Engellenen &#304;&#351;ler</h3><ul>");
            foreach (var s in engelli)
                sb.Append("<li>").Append(H(s.User)).Append(" / ").Append(H(s.Doc.Length > 0 ? s.Doc : "belge"))
                  .Append(" &mdash; ").Append(H(s.Status.Replace("ENGEL:", ""))).Append("</li>");
            sb.Append("</ul>");
        }
        sb.Append("<p style='color:#889;font-size:12px'>Panel: https://").Append(Environment.MachineName)
          .Append(":").Append(PortSsl).Append(" &bull; Print360 otomatik raporu</p></div>");
        return sb.ToString();
    }

    // Uyari motoru: cevrimici -> cevrimdisi gecislerini izler (10 dk esigi)
    static void AlertMonitor()
    {
        var onceki = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool ilk = true;
        while (true)
        {
            try
            {
                var hb = LoadHb();
                foreach (var kv in hb)
                {
                    DateTime t;
                    bool online = DateTime.TryParseExact(kv.Value[1], "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && t > DateTime.Now.AddMinutes(-10);
                    bool prev;
                    if (!ilk && onceki.TryGetValue(kv.Key, out prev) && prev && !online)
                    {
                        string olay = "ISTEMCI: " + kv.Key + " cevrimdisi oldu (son gorulme: " + kv.Value[1] + ")";
                        Db.Alert("Cevrimdisi", "Makine cevrimdisi: " + kv.Key + " (son: " + kv.Value[1] + ")");
                        Db.Exec("INSERT INTO ConnLog(Tarih,Olay) VALUES(GETDATE(),@o)", "@o", olay);
                        try { File.AppendAllText(connLog, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + olay + "\r\n"); } catch { }
                    }
                    onceki[kv.Key] = online;
                }
                ilk = false;
            }
            catch (Exception ex) { Log("AlertMonitor: " + ex.Message); }
            Thread.Sleep(60000);
        }
    }

    // Istemci sifre denetimi. Donus: true = izinli.
    // Ilk temas: makine kayitsizsa gonderdigi sifreyle kaydedilir (TOFU).
    // Kayitli makine: sifre eslesmezse red + Guvenlik uyarisi.
    // Sifresi olmayan eski istemciler yalnizca kayitsiz makinelerde kabul edilir.
    static bool ClientAuth(HttpListenerContext ctx, string machine, out bool ilkKayit)
    {
        ilkKayit = false;
        string key = Q(ctx, "key") ?? "";
        var o = Db.Scalar("SELECT AnahtarHash FROM ClientKeys WHERE Makine=@m", "@m", machine);
        if (o == null && Db.Err != null) return true; // SQL yok -> eski davranis (CSV modu)
        string hash = key.Length > 0 ? Sha256Hex(key) : "";
        if (o == null)
        {
            if (hash.Length == 0) return true; // anahtar tanimlamamis eski istemci
            Db.Exec("INSERT INTO ClientKeys(Makine,AnahtarHash) VALUES(@m,@h)", "@m", machine, "@h", hash);
            ilkKayit = true;
            return true;
        }
        if (hash.Length > 0 && Convert.ToString(o).Trim() == hash) return true;
        string ip = ctx.Request.RemoteEndPoint != null ? ctx.Request.RemoteEndPoint.Address.ToString() : "?";
        Db.Alert("Guvenlik", "HATALI ISTEMCI SIFRESI: " + machine + " (IP: " + ip + ") - istek reddedildi");
        Log("Guvenlik: hatali istemci sifresi - " + machine + " @ " + ip);
        try
        {
            byte[] b = Encoding.UTF8.GetBytes("YETKISIZ");
            ctx.Response.StatusCode = 403;
            ctx.Response.ContentLength64 = b.Length;
            ctx.Response.OutputStream.Write(b, 0, b.Length);
            ctx.Response.Close();
        }
        catch { }
        return false;
    }

    // Guncel istemci ajani binary'sini dagit (C:\Print360\update\Print360.ClientAgent.exe)
    static void HandleClientExe(HttpListenerContext ctx)
    {
        try
        {
            string yol = @"C:\Print360\update\Print360.ClientAgent.exe";
            if (!File.Exists(yol)) { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }
            byte[] data = File.ReadAllBytes(yol);
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.AddHeader("X-Version", Surum.V);
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.Close();
        }
        catch (Exception ex) { Log("ClientExe hatasi: " + ex.Message); try { ctx.Response.Close(); } catch { } }
    }

    // Yazici saglik raporu: satirlari isle, soruna GECIS aninda uyari uret
    static void HandlePrinterHealth(HttpListenerContext ctx)
    {
        try
        {
            string machine = (Q(ctx, "machine") ?? "?").Trim();
            bool ilk;
            if (!ClientAuth(ctx, machine, out ilk)) return;
            string body;
            using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = sr.ReadToEnd();
            foreach (var line in body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = ParseCsvLine(line);
                if (f.Length < 4) continue;
                string yazici = f[0], durum = f[1], hata = f[2];
                int q; int.TryParse(f[3], out q);
                bool sorunlu = hata.Length > 0 || durum == "Cevrimdisi" || durum == "Durduruldu";

                // onceki durumla karsilastir: yeni sorun -> uyari (tekrarlanmaz)
                var onceki = Db.Query("SELECT Durum, Hata FROM PrinterHealth WHERE Makine=@m AND Yazici=@y",
                    "@m", machine, "@y", yazici);
                bool oncedenSorunlu = false;
                if (onceki != null && onceki.Rows.Count > 0)
                {
                    string oD = Convert.ToString(onceki.Rows[0][0]), oH = Convert.ToString(onceki.Rows[0][1]);
                    oncedenSorunlu = oH.Length > 0 || oD == "Cevrimdisi" || oD == "Durduruldu";
                }
                Db.Exec("IF EXISTS(SELECT 1 FROM PrinterHealth WHERE Makine=@m AND Yazici=@y) " +
                        "UPDATE PrinterHealth SET Durum=@d, Hata=@h, Kuyruk=@q, Guncelleme=GETDATE() WHERE Makine=@m AND Yazici=@y " +
                        "ELSE INSERT INTO PrinterHealth(Makine,Yazici,Durum,Hata,Kuyruk,Guncelleme) VALUES(@m,@y,@d,@h,@q,GETDATE())",
                    "@m", machine, "@y", yazici, "@d", durum, "@h", hata, "@q", q);
                // MSSQL ZORUNLU DEGIL: ayni kayit dosyaya da yazilir; panel SQL
                // yoksa bu dosyadan okur (eskiden yazici listesi bos gorunurdu).
                Db.YaziciSagligiYaz(machine, yazici, durum, hata, q);

                if (sorunlu && !oncedenSorunlu)
                    Db.Alert("Yazici", "Yazici sorunu: " + machine + " / " + yazici + " - "
                        + (hata.Length > 0 ? hata : durum));
                else if (!sorunlu && oncedenSorunlu)
                    Db.Exec("INSERT INTO ConnLog(Tarih,Olay) VALUES(GETDATE(),@o)",
                        "@o", "YAZICI: " + machine + " / " + yazici + " duzeldi (" + durum + ")");
            }
            byte[] ok = Encoding.UTF8.GetBytes("OK");
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength64 = ok.Length;
            ctx.Response.OutputStream.Write(ok, 0, ok.Length);
            ctx.Response.Close();
        }
        catch (Exception ex) { Log("PrinterHealth hatasi: " + ex.Message); try { ctx.Response.Close(); } catch { } }
    }

    // Makine adini klasor adina cevir. ServerAgent.Sanitize ile AYNI olmali;
    // yoksa kuyruk klasoru bulunamaz (is istemciye gitmez).

    // ---------------------------------------------------------------------
    // SORGU DIZESI COZUMU (UTF-8)
    // HttpListener.QueryString, yuzde-kodlu degerleri ISLETIM SISTEMININ ANSI
    // kod sayfasiyla cozer (Turkce sunucuda windows-1254). Istemci adlari ise
    // UTF-8 ile kodlar. Bu yuzden "%C4%B1" (Turkce i) sunucuda iki bozuk
    // karaktere donusuyordu; onaylanan is kuyrukta BULUNAMIYOR, "dosya yok =
    // zaten silinmis" sanilip OK donuluyor ve AYNI IS SONSUZA KADAR yeniden
    // veriliyordu ("ilk yazdirma oluyor, devami gelmiyor").
    // Cozum: sorguyu HAM URL'den kendimiz UTF-8 ile cozuyoruz.
    static string Q(HttpListenerContext ctx, string ad)
    {
        try
        {
            string raw = ctx.Request.RawUrl;
            if (raw == null) return null;
            int s = raw.IndexOf('?');
            if (s < 0) return null;
            string[] parcalar = raw.Substring(s + 1).Split('&');
            for (int i = 0; i < parcalar.Length; i++)
            {
                int e = parcalar[i].IndexOf('=');
                if (e < 0) continue;
                if (!string.Equals(parcalar[i].Substring(0, e), ad, StringComparison.OrdinalIgnoreCase)) continue;
                string deger = parcalar[i].Substring(e + 1).Replace("+", "%20");
                try { return Uri.UnescapeDataString(deger); } catch { return deger; }
            }
            return null;
        }
        catch { return null; }
    }

    static string Sanitize(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    // Istemci icin bekleyen isi ver (GZip'li icerik; basliklar: X-Job-Id, X-File-Name).
    // Is yoksa 204 doner. ACK gelene kadar BEKLIYOR kalir (istemci ayni dosyayi ikinci kez almaz).

    // Makine basina kuyruk kilidi: ayni makinenin es zamanli istekleri ayni
    // dosyayi almasin / silmesin.
    static readonly Dictionary<string, object> kuyrukKilitleri = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    static object KuyrukKilidi(string machine)
    {
        lock (kuyrukKilitleri)
        {
            object k;
            if (!kuyrukKilitleri.TryGetValue(machine, out k)) { k = new object(); kuyrukKilitleri[machine] = k; }
            return k;
        }
    }

    static void HandleJobFetch(HttpListenerContext ctx)
    {
        try
        {
            string machine = (Q(ctx, "machine") ?? "?").Trim();
            bool ilk;
            if (!ClientAuth(ctx, machine, out ilk)) return;

            // 1) DOSYA KUYRUGU (birincil - MSSQL GEREKMEZ):
            //    C:\Print360\queue\<makine>\<is>.gz  ->  ilk (en eski) isi ver.
            //    Is-Id olarak dosya adi kullanilir; ACK gelince dosya silinir.
            string qDir = Path.Combine(@"C:\Print360\queue", Sanitize(machine));
            string dosyaYol = null, dosyaAd = null;
            try
            {
                if (Directory.Exists(qDir))
                {
                    // AYNI makineden es zamanli iki istek (cok is parcacikli sunucu)
                    // ayni dosyayi almasin: makine basina kilit.
                    lock (KuyrukKilidi(machine))
                    {
                        var f = new DirectoryInfo(qDir).GetFiles("*.gz")
                                  .OrderBy(x => x.CreationTimeUtc).FirstOrDefault();
                        if (f != null) { dosyaYol = f.FullName; dosyaAd = f.Name.Substring(0, f.Name.Length - 3); }
                    }
                }
            }
            catch (Exception ex) { Log("Dosya kuyrugu okunamadi: " + ex.Message); }

            if (dosyaYol != null)
            {
                // SUNUCU TARAFI GUNLUGU: isin ne zaman ve ne kadar surede verildigi.
                // Istemci gunlugundeki sure ile karsilastirilinca gecikmenin sunucuda
                // mi yoksa agda mi oldugu net gorulur.
                var kron = System.Diagnostics.Stopwatch.StartNew();
                byte[] fdata = File.ReadAllBytes(dosyaYol);
                ctx.Response.ContentType = "application/octet-stream";
                // HTTP BASLIKLARI ASCII TASIR. Belge adinda Turkce harf varsa
                // (orn. "Yazdir" icindeki i) baslikta bozuluyordu: "Yazd1r".
                // Istemci onayi bozuk adla gonderiyor, sunucu o adda dosya
                // bulamiyor, kuyruktan dusuremiyor ve AYNI IS SONSUZA KADAR
                // yeniden veriliyordu ("ilk yazdirma oluyor, devami gelmiyor").
                // Cozum: adlari yuzde-kodlayarak ASCII yapiyoruz. Istemci kimligi
                // aynen geri gonderir; sorgu dizesi cozumlemesi orijinali verir.
                ctx.Response.AddHeader("X-Job-Id", "F:" + Uri.EscapeDataString(Path.GetFileName(dosyaYol)));
                ctx.Response.AddHeader("X-File-Name", Uri.EscapeDataString(dosyaAd));
                ctx.Response.ContentLength64 = fdata.Length;
                ctx.Response.OutputStream.Write(fdata, 0, fdata.Length);
                ctx.Response.Close();
                Log(string.Format("Is verildi -> {0} | {1} | {2} KB | {3} ms",
                    machine, dosyaAd, fdata.Length / 1024, kron.ElapsedMilliseconds));
                return;
            }

            // 2) SQL kuyrugu (eski surumlerden kalan kayitlar icin geriye uyumluluk)
            var dt = Db.Query("SELECT TOP 1 Id, Dosya, Yol FROM JobQueue WHERE Makine=@m AND Durum='BEKLIYOR' ORDER BY Id", "@m", machine);
            if (dt == null || dt.Rows.Count == 0 || !File.Exists(Convert.ToString(dt.Rows[0][2])))
            {
                if (dt != null && dt.Rows.Count > 0)  // dosyasi kayip kayit: dusur
                    Db.Exec("UPDATE JobQueue SET Durum='HATA' WHERE Id=@i", "@i", dt.Rows[0][0]);
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
                return;
            }
            byte[] data = File.ReadAllBytes(Convert.ToString(dt.Rows[0][2]));
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.AddHeader("X-Job-Id", Convert.ToString(dt.Rows[0][0]));
            ctx.Response.AddHeader("X-File-Name", Convert.ToString(dt.Rows[0][1]));
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.Close();
        }
        catch (Exception ex) { Log("JobFetch hatasi: " + ex.Message); try { ctx.Response.Close(); } catch { } }
    }

    // Istemci isi aldigini onaylar: kuyruktan dusur, gecici dosyayi sil

    // Kuyrukta gercekte hangi dosyalar var? (ad eslesmemesi tanisi icin)
    static string KuyruktakiAdlar(string qDir)
    {
        try
        {
            if (!Directory.Exists(qDir)) return "(klasor yok)";
            var f = Directory.GetFiles(qDir, "*.gz");
            if (f.Length == 0) return "(bos)";
            var ad = new List<string>();
            for (int i = 0; i < f.Length && i < 3; i++) ad.Add(Path.GetFileName(f[i]));
            return string.Join(" , ", ad.ToArray()) + (f.Length > 3 ? " ... (+" + (f.Length - 3) + ")" : "");
        }
        catch { return "(okunamadi)"; }
    }

    static void HandleJobDone(HttpListenerContext ctx)
    {
        try
        {
            string machine = (Q(ctx, "machine") ?? "?").Trim();
            bool ilk;
            if (!ClientAuth(ctx, machine, out ilk)) return;
            string ham = (Q(ctx, "id") ?? "0").Trim();
            if (ham.StartsWith("F:"))
            {
                // Dosya kuyrugu onayi (MSSQL gerekmez): yalnizca dosya adi kabul edilir
                string ad = Path.GetFileName(ham.Substring(2));   // yol gecisi engellenir
                string qDirOnay = Path.Combine(@"C:\Print360\queue", Sanitize(machine));
                string yol2 = Path.Combine(qDirOnay, ad);
                // Silme birkac kez denenir: dosya o anda baska bir istek tarafindan
                // okunuyor olabilir (paylasim ihlali). BASARISIZ olursa istemciye
                // 500 donulur ki "OK" sanip sonsuz onay dongusune girmesin.
                bool silindi = false; string sonHata = "";
                lock (KuyrukKilidi(machine))
                {
                    bool vardi = File.Exists(yol2);
                    for (int d = 0; d < 5 && !silindi; d++)
                    {
                        try
                        {
                            if (!File.Exists(yol2)) { silindi = true; break; }
                            File.Delete(yol2);
                            silindi = !File.Exists(yol2);
                        }
                        catch (Exception ex) { sonHata = ex.Message; Thread.Sleep(200); }
                    }
                    // "Dosya yok" ile "sildim" ayni sey DEGILDIR. Onaylanan is
                    // kuyrukta hic bulunamadiysa ad eslesmiyor demektir; bunu
                    // sessizce basari saymak sonsuz donguyu gizlerdi.
                    // Ad eslesmiyorsa kuyrukta BASKA dosyalar durur. Bunu "OK"
                    // saymak, ayni isin sonsuza kadar yeniden verilmesini SESSIZCE
                    // gizler (sahada tam olarak bu yasandi). Kuyruk bossa gercekten
                    // yapacak bir sey yoktur; doluysa bu bir HATADIR, oyle bildirilir.
                    if (!vardi)
                    {
                        string kuyrukta = KuyruktakiAdlar(qDirOnay);
                        Log("UYARI: Onaylanan is kuyrukta bulunamadi <- " + machine + " | aranan: " + ad
                          + " | kuyrukta: " + kuyrukta);
                        if (kuyrukta != "(bos)" && kuyrukta != "(klasor yok)")
                        {
                            silindi = false;
                            sonHata = "onaylanan is kuyrukta yok (ad eslesmiyor). Kuyrukta: " + kuyrukta;
                        }
                    }
                }
                if (silindi) Log("Onay alindi <- " + machine + " | " + ad + " | kuyruktan dusuruldu");
                else
                {
                    Log("ONAY ISLENEMEDI <- " + machine + " | " + ad + " | dosya silinemiyor: " + sonHata);
                    ctx.Response.StatusCode = 500;
                    byte[] hata = Encoding.UTF8.GetBytes("SILINEMEDI: " + sonHata);
                    ctx.Response.ContentLength64 = hata.Length;
                    ctx.Response.OutputStream.Write(hata, 0, hata.Length);
                    ctx.Response.Close();
                    return;
                }
                Db.Exec("UPDATE JobQueue SET Durum='ALINDI', Alinma=GETDATE() WHERE Makine=@m AND Dosya=@f",
                        "@m", machine, "@f", ad.EndsWith(".gz") ? ad.Substring(0, ad.Length - 3) : ad);
            }
            else
            {
                int id; int.TryParse(ham, out id);
                var yol = Db.Scalar("SELECT Yol FROM JobQueue WHERE Id=@i AND Makine=@m", "@i", id, "@m", machine);
                Db.Exec("UPDATE JobQueue SET Durum='ALINDI', Alinma=GETDATE() WHERE Id=@i AND Makine=@m", "@i", id, "@m", machine);
                if (yol != null) { try { File.Delete(Convert.ToString(yol)); } catch { } }
            }
            byte[] ok = Encoding.UTF8.GetBytes("OK");
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength64 = ok.Length;
            ctx.Response.OutputStream.Write(ok, 0, ok.Length);
            ctx.Response.Close();
        }
        catch (Exception ex) { Log("JobDone hatasi: " + ex.Message); try { ctx.Response.Close(); } catch { } }
    }

    // Istemci kalp atisi: cevrimici takibi + baglanti logu
    static void HandleHeartbeat(HttpListenerContext ctx)
    {
        try
        {
            string machine = (Q(ctx, "machine") ?? "?").Trim();
            string printer = Q(ctx, "printer") ?? "";
            string kUser = Q(ctx, "user") ?? "";
            string os = Q(ctx, "os") ?? "";
            string ip = ctx.Request.RemoteEndPoint != null ? ctx.Request.RemoteEndPoint.Address.ToString() : "";
            bool ilkKayit;
            if (!ClientAuth(ctx, machine, out ilkKayit)) return;
            if (ilkKayit) Db.Alert("YeniMakine", "Yeni istemci kaydedildi (sifreli): " + machine + " (IP: " + ip + ")");
            lock (hbLock)
            {
                var hb = LoadHb();
                DateTime prev;
                bool yeni = !hb.ContainsKey(machine) ||
                            !DateTime.TryParseExact(hb[machine][1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out prev) || prev < DateTime.Now.AddMinutes(-3);
                hb[machine] = new[] { machine, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), printer, ip };
                var sb = new StringBuilder();
                foreach (var v in hb.Values)
                    sb.Append(string.Join(",", v.Select(x => "\"" + x.Replace("\"", "\"\"") + "\""))).Append("\r\n");
                Directory.CreateDirectory(Path.GetDirectoryName(hbFile));
                File.WriteAllText(hbFile, sb.ToString());
                // SQL: hic gorulmemis makine mi? (yeni makine uyarisi icin)
                bool dbYeni = false;
                var say = Db.Scalar("SELECT COUNT(*) FROM Heartbeat WHERE Makine=@m", "@m", machine);
                if (say != null) dbYeni = Convert.ToInt32(say) == 0;
                // bos gelen alanlar mevcut degeri ezmesin
                Db.Exec("IF EXISTS(SELECT 1 FROM Heartbeat WHERE Makine=@m) " +
                        "UPDATE Heartbeat SET SonGorulme=GETDATE(), IP=@i, " +
                        "Yazici=CASE WHEN @y='' THEN Yazici ELSE @y END, " +
                        "KullaniciAdi=CASE WHEN @u='' THEN KullaniciAdi ELSE @u END, " +
                        "OS=CASE WHEN @o='' THEN OS ELSE @o END WHERE Makine=@m " +
                        "ELSE INSERT INTO Heartbeat(Makine,SonGorulme,Yazici,IP,KullaniciAdi,OS) VALUES(@m,GETDATE(),@y,@i,@u,@o)",
                    "@m", machine, "@y", printer, "@i", ip, "@u", kUser, "@o", os);
                if (yeni)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(connLog));
                    string olay = "ISTEMCI: " + machine + " cevrimici oldu (IP: " + ip + ", yazici: " + printer + ")";
                    File.AppendAllText(connLog, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + olay + "\r\n");
                    Db.Exec("INSERT INTO ConnLog(Tarih,Olay) VALUES(GETDATE(),@o)", "@o", olay);
                    if (dbYeni) Db.Alert("YeniMakine", "Yeni makine agda: " + machine + " (IP: " + ip + ")");
                }
            }
            byte[] ok = Encoding.UTF8.GetBytes("OK");
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength64 = ok.Length;
            ctx.Response.OutputStream.Write(ok, 0, ok.Length);
            ctx.Response.Close();
        }
        catch (Exception ex) { Log("Heartbeat hatasi: " + ex.Message); try { ctx.Response.Close(); } catch { } }
    }

    static Dictionary<string, string[]> LoadHb()
    {
        var d = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var dt = Db.Query("SELECT Makine,SonGorulme,Yazici,IP,ISNULL(KullaniciAdi,''),ISNULL(OS,'') FROM Heartbeat");
        if (dt != null)
        {
            foreach (System.Data.DataRow r in dt.Rows)
                d[Convert.ToString(r[0])] = new[] { Convert.ToString(r[0]),
                    r[1] == DBNull.Value ? "" : Convert.ToDateTime(r[1]).ToString("yyyy-MM-dd HH:mm:ss"),
                    Convert.ToString(r[2]), Convert.ToString(r[3]), Convert.ToString(r[4]), Convert.ToString(r[5]) };
            return d;
        }
        foreach (var r in ReadCsv(hbFile).Where(r => r.Length >= 4)) d[r[0]] = r;
        return d;
    }

    // Makine cevrimici mi? (son 3 dk icinde kalp atisi)
    static string HbDurum(Dictionary<string, string[]> hb, string machine, out bool online)
    {
        online = false;
        string[] r;
        if (!hb.TryGetValue(machine, out r)) return "";
        DateTime t;
        if (!DateTime.TryParseExact(r[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return "";
        var fark = DateTime.Now - t;
        online = fark.TotalMinutes <= 3;
        return online ? "&#9679; &Ccedil;evrimi&ccedil;i" : "&Ccedil;evrimd&#305;&#351;&#305; (" + r[1] + ")";
    }

    // Ping taramasi (5 dk onbellek) - domain/ag uyum kontrolu
    static Dictionary<string, bool> PingAll(IEnumerable<string> names)
    {
        if (pingCache != null && (DateTime.Now - pingTime).TotalMinutes < 5) return pingCache;
        var d = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var tasks = names.Distinct(StringComparer.OrdinalIgnoreCase).Select(n =>
            System.Threading.Tasks.Task.Run(() =>
            {
                bool ok = false;
                try { using (var p = new System.Net.NetworkInformation.Ping())
                          ok = p.Send(n, 400).Status == System.Net.NetworkInformation.IPStatus.Success; }
                catch { }
                lock (d) d[n] = ok;
            })).ToArray();
        System.Threading.Tasks.Task.WaitAll(tasks, 8000);
        pingCache = d; pingTime = DateTime.Now;
        return d;
    }

    // Maliyet tanimlari: kagit turu -> sayfa basina TL ("*" = tanimsiz turler icin)
    static Dictionary<string, decimal> LoadCosts()
    {
        var d = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var dt = Db.Query("SELECT Kagit, Fiyat FROM Costs");
        if (dt != null)
            foreach (System.Data.DataRow r in dt.Rows)
                d[Convert.ToString(r[0])] = Convert.ToDecimal(r[1]);
        return d;
    }

    static decimal IsMaliyet(Sent s, Dictionary<string, decimal> costs)
    {
        decimal f;
        if (costs.TryGetValue(s.Paper, out f)) return s.PageN * f;
        if (costs.TryGetValue("*", out f)) return s.PageN * f;
        return 0;
    }

    static decimal ToplamMaliyet(IEnumerable<Sent> list, Dictionary<string, decimal> costs)
    {
        decimal t = 0;
        foreach (var s in list) t += IsMaliyet(s, costs);
        return t;
    }

    static string Para(decimal t)
    {
        return t.ToString("N2", new CultureInfo("tr-TR")) + " &#8378;";
    }

    // Basilamayan isler: dosya adi -> hata kaydi (Durum 'HATA%' veya 'IPTAL')
    static Dictionary<string, string[]> LoadFailed()
    {
        var d = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var dt = Db.Query("SELECT Tarih,Makine,Dosya,Yazici,Durum FROM Printed WHERE Durum LIKE 'HATA%' OR Durum='IPTAL'");
        if (dt != null)
        {
            foreach (System.Data.DataRow r in dt.Rows)
                d[Convert.ToString(r[2])] = new[] {
                    r[0] == DBNull.Value ? "" : Convert.ToDateTime(r[0]).ToString("yyyy-MM-dd HH:mm:ss"),
                    Convert.ToString(r[1]), Convert.ToString(r[2]), Convert.ToString(r[3]), Convert.ToString(r[4]) };
            return d;
        }
        if (Directory.Exists(clientsDir))
            foreach (var f in Directory.GetFiles(clientsDir, "*.csv"))
                foreach (var r in ReadCsv(f).Where(r => r.Length >= 5 &&
                         (r[4].StartsWith("HATA", StringComparison.OrdinalIgnoreCase) || r[4] == "IPTAL")))
                    d[r[2]] = r;
        return d;
    }

    // Yetki kurallari - once SQL, sonra CSV
    static Dictionary<string, string[]> LoadRules()
    {
        var d = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var dt = Db.Query("SELECT Tip,Ad,Engel,Kota FROM Rules");
        if (dt != null)
        {
            foreach (System.Data.DataRow r in dt.Rows)
                d[Convert.ToString(r[0]).ToLowerInvariant() + "|" + Convert.ToString(r[1])] = new[] {
                    Convert.ToString(r[0]), Convert.ToString(r[1]),
                    Convert.ToBoolean(r[2]) ? "1" : "0", Convert.ToString(r[3]) };
            return d;
        }
        foreach (var r in ReadCsv(rulesCsv).Where(r => r.Length >= 3))
            d[r[0].ToLowerInvariant() + "|" + r[1]] = r;
        return d;
    }

    // ---------------- Veri ----------------

    static List<Sent> LoadSent()
    {
        // Once SQL, erisilemezse CSV
        var dt = Db.Query("SELECT Tarih,Kullanici,Makine,Belge,Sayfa,Dosya,Kagit,KB,Durum FROM Jobs ORDER BY Id");
        if (dt != null)
        {
            var list = new List<Sent>();
            foreach (System.Data.DataRow r in dt.Rows)
            {
                var paper = Convert.ToString(r[6]);
                list.Add(new Sent
                {
                    Time = r[0] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(r[0]),
                    User = Convert.ToString(r[1]), Machine = Convert.ToString(r[2]),
                    Doc = Convert.ToString(r[3]),
                    PageN = r[4] == DBNull.Value ? 0 : Convert.ToInt32(r[4]),
                    Pages = r[4] == DBNull.Value ? "" : Convert.ToString(r[4]),
                    File = Convert.ToString(r[5]),
                    Paper = paper.Length > 0 ? paper : "Bilinmiyor",
                    KbN = r[7] == DBNull.Value ? 0 : Convert.ToInt32(r[7]),
                    Status = Convert.ToString(r[8]).Length > 0 ? Convert.ToString(r[8]) : "OK"
                });
            }
            return list;
        }
        return ReadCsv(jobsCsv).Where(r => r.Length >= 6).Select(r =>
        {
            DateTime t;
            DateTime.TryParseExact(r[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
            int p; int.TryParse(r[4], out p);
            int kb = 0; if (r.Length > 7) int.TryParse(r[7], out kb);
            return new Sent { Time = t, User = r[1], Machine = r[2], Doc = r[3], Pages = r[4], File = r[5],
                              Paper = r.Length > 6 && r[6].Length > 0 ? r[6] : "Bilinmiyor", PageN = p, KbN = kb,
                              Status = r.Length > 8 && r[8].Length > 0 ? r[8] : "OK" };
        }).ToList();
    }

    static Dictionary<string, string[]> LoadPrinted()
    {
        var d = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var dt = Db.Query("SELECT Tarih,Makine,Dosya,Yazici,Durum FROM Printed WHERE Durum='OK'");
        if (dt != null)
        {
            foreach (System.Data.DataRow r in dt.Rows)
                d[Convert.ToString(r[2])] = new[] {
                    r[0] == DBNull.Value ? "" : Convert.ToDateTime(r[0]).ToString("yyyy-MM-dd HH:mm:ss"),
                    Convert.ToString(r[1]), Convert.ToString(r[2]), Convert.ToString(r[3]), "OK" };
            return d;
        }
        if (Directory.Exists(clientsDir))
            foreach (var f in Directory.GetFiles(clientsDir, "*.csv"))
                foreach (var r in ReadCsv(f).Where(r => r.Length >= 5 && r[4] == "OK"))
                    d[r[2]] = r;
        return d;
    }

    // Active Directory'deki bilgisayarlar (5 dk onbellek)
    static List<AdPc> LoadAd()
    {
        if (adCache != null && (DateTime.Now - adCacheTime).TotalMinutes < 5) return adCache;
        var list = new List<AdPc>();
        adError = null;
        try
        {
            using (var root = new DirectoryEntry())   // varsayilan etki alani
            using (var ds = new DirectorySearcher(root, "(objectCategory=computer)",
                       new[] { "name", "operatingSystem", "lastLogonTimestamp" }))
            {
                ds.PageSize = 500;
                foreach (SearchResult r in ds.FindAll())
                {
                    var pc = new AdPc
                    {
                        Name = Prop(r, "name"),
                        Os = Prop(r, "operatingSystem")
                    };
                    if (r.Properties.Contains("lastlogontimestamp"))
                    {
                        try { pc.LastLogon = DateTime.FromFileTime((long)r.Properties["lastlogontimestamp"][0]); }
                        catch { }
                    }
                    list.Add(pc);
                }
            }
        }
        catch (Exception ex)
        {
            adError = ex.Message;
            Log("AD sorgusu basarisiz: " + ex.Message);
        }
        adCache = list; adCacheTime = DateTime.Now;
        return list;
    }

    static string Prop(SearchResult r, string name)
    {
        return r.Properties.Contains(name) && r.Properties[name].Count > 0
            ? Convert.ToString(r.Properties[name][0]) : "";
    }

    // Active Directory kullanicilari (5 dk onbellek) - domain uyum kontrolu
    static List<AdUser> LoadAdUsers()
    {
        if (adUserCache != null && (DateTime.Now - adUserCacheTime).TotalMinutes < 5) return adUserCache;
        var list = new List<AdUser>();
        try
        {
            using (var root = new DirectoryEntry())
            using (var ds = new DirectorySearcher(root, "(&(objectCategory=person)(objectClass=user))",
                       new[] { "samaccountname", "displayname", "lastlogontimestamp", "useraccountcontrol" }))
            {
                ds.PageSize = 500;
                foreach (SearchResult r in ds.FindAll())
                {
                    var u = new AdUser
                    {
                        Sam = Prop(r, "samaccountname"),
                        Ad = Prop(r, "displayname"),
                        Aktif = true
                    };
                    int uac; if (int.TryParse(Prop(r, "useraccountcontrol"), out uac)) u.Aktif = (uac & 2) == 0;
                    if (r.Properties.Contains("lastlogontimestamp"))
                        try { u.LastLogon = DateTime.FromFileTime((long)r.Properties["lastlogontimestamp"][0]); } catch { }
                    list.Add(u);
                }
            }
        }
        catch (Exception ex) { adError = ex.Message; }
        adUserCache = list; adUserCacheTime = DateTime.Now;
        return list;
    }

    // ---------------- Yonlendirme + kimlik dogrulama ----------------

    static string Route(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        string path = (req.Url.AbsolutePath ?? "/").TrimEnd('/').ToLowerInvariant();
        if (path == "") path = "/";

        // GUVENLIK: Panel korumasizsa (ne PanelUsers ne panel.pwd tanimli) yalnizca
        // SUNUCUNUN KENDISINDEN erisilebilir. Aksi halde agdaki herkes bask' gecmisini,
        // belge adlarini ve arsivlenmis PDF'leri parolasiz okuyabilirdi.
        // Istemci ajanlarinin /api/* uc noktalari bu kontrolden ONCE karsilandigi icin
        // yazdirma isleyisi etkilenmez.
        if (!AuthAktif() && !req.IsLocal)
        {
            Log("Guvenlik: parolasiz panele uzaktan erisim reddedildi - " +
                (req.RemoteEndPoint != null ? req.RemoteEndPoint.Address.ToString() : "?"));
            return "<div class='warn'><b>Panel korumas&#305;z oldu&#287;u i&ccedil;in uzaktan eri&#351;ime kapal&#305;.</b><br><br>"
                 + "Bu sunucuda panel eri&#351;im parolas&#305; tan&#305;ml&#305; de&#287;il. Parolas&#305;z bir panel, "
                 + "a&#287;daki herkese bask&#305; ge&ccedil;mi&#351;ini, belge adlar&#305;n&#305; ve ar&#351;ivlenmi&#351; "
                 + "&ccedil;&#305;kt&#305;lar&#305; a&ccedil;ard&#305;. Bu nedenle yaln&#305;zca sunucunun kendisinden "
                 + "(localhost) a&ccedil;&#305;labilir.<br><br>"
                 + "<b>A&ccedil;mak i&ccedil;in:</b> kurulumu tekrar &ccedil;al&#305;&#351;t&#305;r&#305;p "
                 + "<i>Y&ouml;netim Paneli</i> sayfas&#305;ndaki <i>Panel eri&#351;im parolas&#305;</i> alan&#305;n&#305; doldurun.</div>";
        }

        if (AuthAktif())
        {
            if (path == "/login") return HandleLogin(ctx);
            if (path == "/cikis")
            {
                var ck = req.Cookies["p360"];
                if (ck != null) sessions.Remove(ck.Value);
                Redirect(ctx, "/login");
                return null;
            }
            var c = req.Cookies["p360"];
            bool auth = c != null && sessions.ContainsKey(c.Value) && sessions[c.Value] > DateTime.Now;
            if (!auth) { Redirect(ctx, "/login"); return null; }
        }

        switch (path)
        {
            case "/tani": return Page("tani", PageTani());
            case "/veritabani": return Page("veritabani", PageVeritabani(ctx));
            case "/makineler": return Page("makineler", PageMachines());
            case "/yazicilar": return Page("yazicilar", PagePrinters());
            case "/kullanicilar": return Page("kullanicilar", PageUsers());
            case "/periyot": return Page("periyot", PagePeriods(req));
            case "/isler": return Page("isler", PageJobs(req));
            case "/yetki": return Page("yetki", PageRules(null));
            case "/yetki/kaydet": return SaveRules(ctx);
            case "/yetki/maliyet":
                if (req.HttpMethod == "POST")
                {
                    string bodyM;
                    using (var srM = new StreamReader(req.InputStream, Encoding.UTF8)) bodyM = srM.ReadToEnd();
                    if (Db.Exec("DELETE FROM Costs"))
                        foreach (var kvM in bodyM.Split('&'))
                        {
                            var pM = kvM.Split(new[] { '=' }, 2);
                            if (pM.Length != 2 || !pM[0].StartsWith("c_")) continue;
                            string kagit = WebUtility.UrlDecode(WebUtility.UrlDecode(pM[0].Substring(2)).Replace('+', ' '));
                            decimal fiyat;
                            if (decimal.TryParse(WebUtility.UrlDecode(pM[1]).Trim().Replace(",", "."),
                                    NumberStyles.Any, CultureInfo.InvariantCulture, out fiyat) && fiyat > 0)
                                Db.Exec("INSERT INTO Costs(Kagit,Fiyat) VALUES(@k,@f)", "@k", kagit, "@f", fiyat);
                        }
                    Log("Maliyet tanimlari guncellendi");
                }
                Redirect(ctx, "/yetki");
                return null;
            case "/yetki/anahtar":
                if (req.HttpMethod == "POST")
                {
                    string bodyA;
                    using (var srA = new StreamReader(req.InputStream, Encoding.UTF8)) bodyA = srA.ReadToEnd();
                    foreach (var kvA in bodyA.Split('&'))
                    {
                        var pA = kvA.Split(new[] { '=' }, 2);
                        if (pA.Length == 2 && pA[0] == "makine")
                        {
                            string mkA = WebUtility.UrlDecode(pA[1].Replace('+', ' '));
                            Db.Exec("DELETE FROM ClientKeys WHERE Makine=@m", "@m", mkA);
                            Db.Alert("Guvenlik", "Istemci sifresi sifirlandi: " + mkA);
                        }
                    }
                }
                Redirect(ctx, "/yetki");
                return null;
            case "/pdf": return ServePdf(ctx);
            case "/export/isler.csv": return CsvJobs(ctx);
            case "/export/makineler.csv": return CsvMachines(ctx);
            case "/export/kullanicilar.csv": return CsvUsers(ctx);
            case "/export/maliyet.csv": return CsvUserCosts(ctx);
            case "/export/periyot.csv": return CsvPeriod(ctx);
            case "/rapor/test":
                if (req.HttpMethod == "POST")
                    lastMailResult = SendReport()
                        ? "Rapor e-postas&#305; g&ouml;nderildi (" + DateTime.Now.ToString("HH:mm:ss") + ")."
                        : "G&Ouml;NDER&#304;LEMED&#304;: " + H(MailErr ?? "bilinmeyen hata");
                Redirect(ctx, "/uyarilar");
                return null;
            case "/uyarilar": return Page("uyarilar", PageAlerts());
            case "/uyarilar/okundu":
                Db.Exec("UPDATE Alerts SET Okundu=1 WHERE Okundu=0"); Db.UyarilariOkunduIsaretle();
                Redirect(ctx, "/uyarilar");
                return null;
            default: return Page("genel", PageOverview());
        }
    }

    static string GetPwdHash()
    {
        try { return File.Exists(pwdFile) ? File.ReadAllText(pwdFile).Trim() : ""; }
        catch { return ""; }
    }

    // Panel kullanicilari: SQL PanelUsers tablosu (60 sn onbellek).
    // SQL yoksa/tablo bossa panel.pwd tek-sifre moduna duser.
    static Dictionary<string, string> userCache;
    static DateTime userCacheTime = DateTime.MinValue;

    static Dictionary<string, string> AuthUsers()
    {
        if (userCache != null && (DateTime.Now - userCacheTime).TotalSeconds < 60) return userCache;
        Dictionary<string, string> d = null;
        var dt = Db.Query("SELECT Kullanici, SifreHash FROM PanelUsers");
        if (dt != null)
        {
            d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Data.DataRow r in dt.Rows)
                d[Convert.ToString(r[0])] = Convert.ToString(r[1]).Trim();
        }
        userCache = d; userCacheTime = DateTime.Now;
        return d;
    }

    static bool AuthAktif()
    {
        var u = AuthUsers();
        if (u != null && u.Count > 0) return true;
        return GetPwdHash().Length > 0;
    }

    static bool GirisDogru(string kullanici, string sifre)
    {
        var u = AuthUsers();
        if (u != null && u.Count > 0)
        {
            string h;
            return kullanici.Length > 0 && u.TryGetValue(kullanici, out h) && h == Sha256Hex(sifre);
        }
        return Sha256Hex(sifre) == GetPwdHash();
    }

    static string Sha256Hex(string s)
    {
        using (var sha = SHA256.Create())
        {
            var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            var sb = new StringBuilder();
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
    }

    static void Redirect(HttpListenerContext ctx, string to)
    {
        ctx.Response.StatusCode = 302;
        ctx.Response.RedirectLocation = to;
        ctx.Response.Close();
    }

    static string HandleLogin(HttpListenerContext ctx)
    {
        bool wrong = false;
        bool kullaniciModu = AuthUsers() != null && AuthUsers().Count > 0;
        if (ctx.Request.HttpMethod == "POST")
        {
            string body;
            using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = sr.ReadToEnd();
            string pwd = "", usr = "";
            foreach (var kv in body.Split('&'))
            {
                var p = kv.Split(new[] { '=' }, 2);
                if (p.Length == 2 && p[0] == "pwd") pwd = WebUtility.UrlDecode(p[1]);
                if (p.Length == 2 && p[0] == "usr") usr = WebUtility.UrlDecode(p[1]);
            }
            if (GirisDogru(usr, pwd))
            {
                // suresi dolan oturumlari temizle, yeni oturum ac (12 saat)
                foreach (var k in sessions.Where(s => s.Value < DateTime.Now).Select(s => s.Key).ToList())
                    sessions.Remove(k);
                string token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                sessions[token] = DateTime.Now.AddHours(12);
                ctx.Response.AppendHeader("Set-Cookie", "p360=" + token + "; Path=/; HttpOnly");
                Redirect(ctx, "/");
                return null;
            }
            wrong = true;
            Thread.Sleep(800); // kaba kuvvet yavaslatma
        }
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang='tr'><head><meta charset='utf-8'><title>Print360 Giri&#351;</title><style>");
        sb.Append("body{font-family:'Segoe UI',sans-serif;background:#1f3a5f;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0}");
        sb.Append(".box{background:#fff;border-radius:12px;padding:34px 38px;box-shadow:0 8px 30px rgba(0,0,0,.3);width:320px;text-align:center}");
        sb.Append(".box h1{font-size:20px;color:#1f3a5f;margin:0 0 6px}.box p{font-size:13px;color:#667;margin:0 0 18px}");
        sb.Append("input{width:100%;box-sizing:border-box;padding:10px 12px;border:1px solid #cdd7e5;border-radius:8px;font-size:14px;margin-bottom:12px}");
        sb.Append("button{width:100%;padding:10px;background:#1f3a5f;color:#fff;border:0;border-radius:8px;font-size:14px;font-weight:600;cursor:pointer}");
        sb.Append(".err{color:#c0392b;font-size:13px;margin-bottom:10px}");
        sb.Append("</style></head><body><div class='box'>");
        sb.Append("<h1>&#128424; Print360</h1><p>Yazd&#305;rma Paneli</p>");
        if (wrong) sb.Append("<div class='err'>Hatal&#305; giri&#351;, tekrar deneyin.</div>");
        sb.Append("<form method='post' action='/login'>");
        if (kullaniciModu)
            sb.Append("<input type='text' name='usr' placeholder='Kullan&#305;c&#305; ad&#305;' autofocus>")
              .Append("<input type='password' name='pwd' placeholder='&#350;ifre'>");
        else
            sb.Append("<input type='password' name='pwd' placeholder='&#350;ifre' autofocus>");
        sb.Append("<button type='submit'>Giri&#351;</button></form></div></body></html>");
        return sb.ToString();
    }

    // ---------------- Sayfalar ----------------

    static string PageOverview()
    {
        var all = LoadSent();
        var sent = all.Where(s => s.Status == "OK").ToList();
        int engellenen = all.Count - sent.Count;
        var printed = LoadPrinted();
        int totalPages = sent.Sum(s => s.PageN);
        var machines = sent.GroupBy(s => MachineKey(s))
            .Select(g => new { M = g.Key, Sent = g.Count(), Printed = g.Count(s => printed.ContainsKey(s.File)), Pages = g.Sum(s => s.PageN) })
            .OrderByDescending(x => x.Sent).ToList();
        var users = sent.GroupBy(s => s.User)
            .Select(g => new { U = g.Key, Sent = g.Count(), Pages = g.Sum(s => s.PageN) })
            .OrderByDescending(x => x.Sent).ToList();
        var today = sent.Count(s => s.Time.Date == DateTime.Today);

        double avgPages = sent.Count > 0 ? Math.Round((double)totalPages / sent.Count, 1) : 0;
        double totalMb = Math.Round(sent.Sum(s => (double)s.KbN) / 1024.0, 1);
        var costs = LoadCosts();
        bool maliyetli = costs.Count > 0;

        // --- Bagli istemciler (canli kalp atisi) ---
        var hbO = LoadHb();
        var istemciler = new List<string[]>();   // makine, kullanici, ip, yazici, os, sonGorulme, onlineBayrak
        foreach (var kv in hbO)
        {
            bool onl; HbDurum(hbO, kv.Key, out onl);
            var v = kv.Value;
            istemciler.Add(new string[] {
                kv.Key,
                v.Length > 4 ? v[4] : "", v.Length > 3 ? v[3] : "", v.Length > 2 ? v[2] : "",
                v.Length > 5 ? v[5] : "", v.Length > 1 ? v[1] : "", onl ? "1" : "0" });
        }
        istemciler = istemciler.OrderByDescending(x => x[6])
                               .ThenBy(x => x[0], StringComparer.OrdinalIgnoreCase).ToList();
        int bagliSayi = istemciler.Count(x => x[6] == "1");

        var sb = new StringBuilder();
        if (!AuthAktif())
            sb.Append("<div class='warn'><b>&#9888; Bu panel parolas&#305;z.</b> &#350;u anda yaln&#305;zca bu "
                    + "sunucudan a&ccedil;&#305;labiliyor; a&#287;daki di&#287;er bilgisayarlardan eri&#351;im "
                    + "reddediliyor. Uzaktan y&ouml;netmek i&ccedil;in kurulumu tekrar &ccedil;al&#305;&#351;t&#305;r&#305;p "
                    + "panel eri&#351;im parolas&#305;n&#305; tan&#305;mlay&#305;n.</div>");
        sb.Append("<div class='info'>&#128273; ").Append(H(Lisans.DurumMetni())).Append("</div>");
        sb.Append("<div class='cards'>");
        Card(sb, sent.Count.ToString(), "Toplam &Ccedil;&#305;kt&#305;");
        Card(sb, sent.Count(s => printed.ContainsKey(s.File)).ToString(), "Bas&#305;ld&#305; (onayl&#305;)");
        Card(sb, totalPages.ToString(), "Toplam Sayfa");
        Card(sb, avgPages.ToString(CultureInfo.InvariantCulture), "Ort. Sayfa/&#304;&#351;");
        Card(sb, totalMb.ToString(CultureInfo.InvariantCulture) + " MB", "Toplam Veri");
        Card(sb, today.ToString(), "Bug&uuml;n");
        Card(sb, machines.Count.ToString(), "Makine");
        Card(sb, bagliSayi.ToString(), "Ba&#287;l&#305; &#304;stemci");
        Card(sb, engellenen.ToString(), "Engellenen");
        object bekleyen = Db.Scalar("SELECT COUNT(*) FROM JobQueue WHERE Durum='BEKLIYOR'");
        if (bekleyen != null) Card(sb, Convert.ToString(bekleyen), "Kuyrukta");
        if (maliyetli) Card(sb, Para(ToplamMaliyet(sent, costs)), "Toplam Maliyet");
        sb.Append("</div>");

        sb.Append("<div class='cols'><div>");

        // --- Bagli istemciler tablosu (cevrimici olanlar ustte) ---
        sb.Append("<h2>Ba&#287;l&#305; &#304;stemciler</h2>");
        sb.Append("<div class='info'>Ajan&#305; &ccedil;al&#305;&#351;an istemciler. &#9679; = son 3 dakika i&ccedil;inde kalp at&#305;&#351;&#305; al&#305;nd&#305;.</div>");
        sb.Append("<table><tr><th>Makine</th><th>Kullan&#305;c&#305;</th><th>IP</th>")
          .Append("<th>Varsay&#305;lan Yaz&#305;c&#305;</th><th>Son G&ouml;r&uuml;lme</th><th>Durum</th></tr>");
        foreach (var c in istemciler)
        {
            bool onl = c[6] == "1";
            sb.Append("<tr><td><b>").Append(H(c[0])).Append("</b>")
              .Append(c[4].Length > 0 ? "<br><span class='mut'>" + H(c[4]) + "</span>" : "").Append("</td>")
              .Append("<td>").Append(c[1].Length > 0 ? H(c[1]) : "&mdash;").Append("</td>")
              .Append("<td>").Append(c[2].Length > 0 ? H(c[2]) : "&mdash;").Append("</td>")
              .Append("<td>").Append(c[3].Length > 0 ? H(c[3]) : "&mdash;").Append("</td>")
              .Append("<td>").Append(H(c[5])).Append("</td>")
              .Append(onl ? "<td><span class='rz rz-ak' title='Kalp at&#305;&#351;&#305; al&#305;n&#305;yor'><i></i>Aktif</span></td>"
                          : "<td><span class='rz rz-ps' title='Kalp at&#305;&#351;&#305; kesildi'><i></i>&Ccedil;evrimd&#305;&#351;&#305;</span></td>")
              .Append("</tr>");
        }
        if (istemciler.Count == 0)
            sb.Append("<tr><td colspan='6' class='mut'>Hen&uuml;z istemci ba&#287;lanmad&#305;. ")
              .Append("&#304;stemci bilgisayarlara Print360-Client-Setup.exe kurun.</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>Makine Bazl&#305;</h2><table><tr><th>Makine</th><th>G&ouml;nderilen</th><th>Bas&#305;lan</th><th>Sayfa</th></tr>");
        foreach (var m in machines)
            sb.Append("<tr><td>").Append(H(m.M)).Append("</td><td>").Append(m.Sent)
              .Append("</td><td>").Append(m.Printed).Append("</td><td>").Append(m.Pages).Append("</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>Kullan&#305;c&#305; Bazl&#305;</h2><table><tr><th>Kullan&#305;c&#305;</th><th>&Ccedil;&#305;kt&#305;</th><th>Sayfa</th>")
          .Append(maliyetli ? "<th>Maliyet</th>" : "").Append("</tr>");
        foreach (var u in users)
        {
            sb.Append("<tr><td>").Append(H(u.U)).Append("</td><td>").Append(u.Sent).Append("</td><td>").Append(u.Pages).Append("</td>");
            if (maliyetli) sb.Append("<td>").Append(Para(ToplamMaliyet(sent.Where(s => s.User == u.U), costs))).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        sb.Append("</div><div>");

        // Kagit turu dagilimi
        sb.Append("<h2>Ka&#287;&#305;t T&uuml;r&uuml;</h2><table><tr><th>Ka&#287;&#305;t</th><th>&Ccedil;&#305;kt&#305;</th><th>Sayfa</th><th>Oran</th>")
          .Append(maliyetli ? "<th>Maliyet</th>" : "").Append("</tr>");
        foreach (var g in sent.GroupBy(s => s.Paper).OrderByDescending(g => g.Count()))
        {
            sb.Append("<tr><td>").Append(H(g.Key)).Append("</td><td>").Append(g.Count())
              .Append("</td><td>").Append(g.Sum(s => s.PageN)).Append("</td><td>")
              .Append(sent.Count > 0 ? Math.Round(100.0 * g.Count() / sent.Count) : 0).Append("%</td>");
            if (maliyetli) sb.Append("<td>").Append(Para(ToplamMaliyet(g, costs))).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");

        // Yazici bazli (istemci onay kayitlarindan)
        var byPrinter = sent.Where(s => printed.ContainsKey(s.File))
            .GroupBy(s => printed[s.File].Length > 3 ? printed[s.File][3] : "?")
            .Select(g => new { P = g.Key, N = g.Count(), Pg = g.Sum(s => s.PageN) })
            .OrderByDescending(x => x.N).ToList();
        sb.Append("<h2>Lokal Yaz&#305;c&#305; Bazl&#305; <span class='mut' style='font-weight:400;font-size:12px'>(makine &#92; yaz&#305;c&#305;)</span></h2>")
          .Append("<table><tr><th>Lokal PC &#92; Yaz&#305;c&#305;</th><th>&Ccedil;&#305;kt&#305;</th><th>Sayfa</th></tr>");
        foreach (var p in byPrinter)
            sb.Append("<tr><td>").Append(H(p.P)).Append("</td><td>").Append(p.N).Append("</td><td>").Append(p.Pg).Append("</td></tr>");
        if (byPrinter.Count == 0) sb.Append("<tr><td colspan='3' class='mut'>Hen&uuml;z onay kayd&#305; yok</td></tr>");
        sb.Append("</table>");
        sb.Append("</div></div>");

        sb.Append("<h2>Son 20 &#304;&#351;</h2>");
        JobTable(sb, Enumerable.Reverse(sent).Take(20), printed);
        return sb.ToString();
    }

    // Bir yazici "AKTIF" sayilir mi?  Iki sart birden aranir:
    //   1) Raporu TAZE olmali (istemci ajani <=5 dk once temas etmis) -> baglanti canli
    //   2) Yazicinin kendisi hazir/yazdiriyor olmali ve hata bildirmemis
    // Ajan durduysa yazici "Hazir" gorunse bile rapor eskir; o yuzden ikisi de sart.
    // kayit dizisi: [makine, yazici, durum, hata, kuyruk, guncelleme]
    const int TAZELIK_DK = 5;

    static bool RaporTaze(string guncelleme)
    {
        DateTime g;
        if (!DateTime.TryParse(guncelleme, out g)) return false;
        return g > DateTime.Now.AddMinutes(-TAZELIK_DK);
    }

    static bool YaziciAktif(string[] r)
    {
        return RaporTaze(r[5]) && r[3].Length == 0 && (r[2] == "Hazir" || r[2] == "Yazdiriyor");
    }

    // Yazicinin baglanti rozeti: yesil AKTIF / sari SORUN / kirmizi CEVRIMDISI / gri PASIF
    static string YaziciRozet(string[] r)
    {
        bool taze = RaporTaze(r[5]);
        if (!taze)
            return "<span class='rz rz-ps' title='&#304;stemci ajan&#305; " + TAZELIK_DK
                 + " dakikad&#305;r rapor g&ouml;ndermedi'><i></i>Pasif</span>";
        if (r[2] == "Cevrimdisi")
            return "<span class='rz rz-kp' title='Yaz&#305;c&#305; &ccedil;evrimd&#305;&#351;&#305;'><i></i>&Ccedil;evrimd&#305;&#351;&#305;</span>";
        if (r[3].Length > 0 || r[2] == "Durduruldu")
            return "<span class='rz rz-uy' title='" + H(r[3].Length > 0 ? r[3] : r[2]) + "'><i></i>Sorunlu</span>";
        return "<span class='rz rz-ak' title='Ba&#287;l&#305; ve yazd&#305;rmaya haz&#305;r'><i></i>Aktif</span>";
    }

    // Yazici sagligi sayfasi
    static string PagePrinters()
    {
        var sb = new StringBuilder();
        // MSSQL ZORUNLU DEGIL: once SQL denenir, yoksa dosya deposundan okunur.
        // [makine, yazici, durum, hata, kuyruk, guncelleme]
        var kayit = new List<string[]>();
        var dt = Db.Query("SELECT Makine, Yazici, Durum, Hata, Kuyruk, Guncelleme FROM PrinterHealth ORDER BY Makine, Yazici");
        if (dt != null)
            foreach (System.Data.DataRow r in dt.Rows)
                kayit.Add(new[] { Convert.ToString(r[0]), Convert.ToString(r[1]), Convert.ToString(r[2]),
                                  Convert.ToString(r[3]), r[4] == DBNull.Value ? "0" : Convert.ToString(r[4]),
                                  r[5] == DBNull.Value ? "" : Convert.ToDateTime(r[5]).ToString("yyyy-MM-dd HH:mm:ss") });
        else
        {
            kayit = Db.YaziciSagligiOku();
            kayit.Sort((a, b) => string.Compare(a[0] + "|" + a[1], b[0] + "|" + b[1], StringComparison.OrdinalIgnoreCase));
        }

        int toplam = kayit.Count, sorunlu = 0, cevrimdisi = 0, kuyrukToplam = 0, aktif = 0;
        foreach (var r in kayit)
        {
            if (r[3].Length > 0 || r[2] == "Durduruldu") sorunlu++;
            if (r[2] == "Cevrimdisi") cevrimdisi++;
            int q; int.TryParse(r[4], out q); kuyrukToplam += q;
            if (YaziciAktif(r)) aktif++;
        }
        sb.Append("<div class='info'>&#304;stemci ajanlar&#305; yerel yaz&#305;c&#305;lar&#305;n&#305; dakikada bir tarar (WMI). ")
          .Append("<b>&#9679; Aktif</b> = istemci ajan&#305; son ").Append(TAZELIK_DK)
          .Append(" dakika i&ccedil;inde rapor g&ouml;nderdi <i>ve</i> yaz&#305;c&#305; yazd&#305;rmaya haz&#305;r. ")
          .Append("Ajan durursa yaz&#305;c&#305; <b>Pasif</b>'e d&uuml;&#351;er &mdash; &ccedil;&uuml;nk&uuml; o an ger&ccedil;ekten yazd&#305;r&#305;lamaz. ")
          .Append("Sorun olu&#351;tu&#287;u anda Uyar&#305;lar'a d&uuml;&#351;er; d&uuml;zelme ba&#287;lant&#305; loglar&#305;na yaz&#305;l&#305;r.</div>");
        sb.Append("<div class='cards'>");
        Card(sb, toplam.ToString(), "Toplam Yaz&#305;c&#305;");
        Card(sb, aktif.ToString(), "&#9679; Aktif (ba&#287;l&#305;)");
        Card(sb, sorunlu.ToString(), "Sorunlu");
        Card(sb, cevrimdisi.ToString(), "&Ccedil;evrimd&#305;&#351;&#305;");
        Card(sb, kuyrukToplam.ToString(), "Kuyruktaki &#304;&#351;");
        sb.Append("</div>");
        sb.Append("<table><tr><th>Ba&#287;lant&#305;</th><th>Makine</th><th>Yaz&#305;c&#305;</th><th>Durum</th>")
          .Append("<th>Sorun</th><th>Kuyruk</th><th>Son G&uuml;ncelleme</th></tr>");
        foreach (var r in kayit)
        {
            string d = r[2], h = r[3];
            DateTime g; if (!DateTime.TryParse(r[5], out g)) g = DateTime.MinValue;
            bool eski = g != DateTime.MinValue && g < DateTime.Now.AddMinutes(-5);
            string dCls = d == "Hazir" || d == "Yazdiriyor" ? "ok" : (d == "Cevrimdisi" || d == "Durduruldu" ? "wait" : "mut");
            int q; int.TryParse(r[4], out q);
            sb.Append("<tr><td>").Append(YaziciRozet(r))
              .Append("</td><td>").Append(H(r[0]))
              .Append("</td><td>").Append(H(r[1]))
              .Append("</td><td class='").Append(dCls).Append("'>").Append(H(d))
              .Append("</td><td>").Append(h.Length > 0 ? "<span style='color:#c0392b;font-weight:600'>" + H(h) + "</span>" : "&mdash;")
              .Append("</td><td>").Append(q)
              .Append("</td><td").Append(eski ? " class='mut'" : "").Append(">")
              .Append(g == DateTime.MinValue ? "" : g.ToString("yyyy-MM-dd HH:mm:ss")).Append(eski ? " (eski)" : "")
              .Append("</td></tr>");
        }
        if (toplam == 0) sb.Append("<tr><td colspan='7' class='mut'>Hen&uuml;z yaz&#305;c&#305; raporu gelmedi. &#304;stemci ajanlar&#305;n&#305;n g&uuml;ncel s&uuml;r&uuml;mde ve sunucu adresinin tan&#305;ml&#305; oldu&#287;undan emin olun.</td></tr>");
        sb.Append("</table>");
        return sb.ToString();
    }

    // Kullanicilar raporu: yazdirma istatistikleri + Active Directory hesap kontrolu
    static string PageUsers()
    {
        var all = LoadSent();
        var sent = all.Where(s => s.Status == "OK").ToList();
        var adUsers = LoadAdUsers();
        var stats = sent.GroupBy(s => s.User, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var blocked = all.Where(s => s.Status != "OK")
            .GroupBy(s => s.User, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var names = adUsers.Select(u => u.Sam).Union(stats.Keys, StringComparer.OrdinalIgnoreCase)
                           .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        var sb = new StringBuilder();
        if (adError != null)
            sb.Append("<div class='warn'>Active Directory sorgulanamad&#305;: ").Append(H(adError)).Append("</div>");
        else
            sb.Append("<div class='info'>Etki alan&#305;nda <b>").Append(adUsers.Count).Append("</b> kullan&#305;c&#305; hesab&#305; bulundu.</div>");

        sb.Append("<a class='exp' href='/export/kullanicilar.csv'>&#11015; Excel'e Aktar</a><br>");
        sb.Append("<div class='cards'>");
        Card(sb, names.Count.ToString(), "Toplam Kullan&#305;c&#305;");
        Card(sb, stats.Count.ToString(), "Yazd&#305;ran");
        Card(sb, adUsers.Count(u => !u.Aktif).ToString(), "AD Pasif Hesap");
        Card(sb, blocked.Values.Sum().ToString(), "Engellenen &#304;&#351;");
        sb.Append("</div>");

        var costsU = LoadCosts();
        bool mU = costsU.Count > 0;
        sb.Append("<table><tr><th>Kullan&#305;c&#305;</th><th>AD</th><th>Ad Soyad</th><th>AD Hesap</th><th>Son AD Oturumu</th>")
          .Append("<th>&Ccedil;&#305;kt&#305;</th><th>Sayfa</th>").Append(mU ? "<th>Maliyet</th>" : "")
          .Append("<th>Veri</th><th>Engellenen</th><th>Son Yazd&#305;rma</th><th>En &Ccedil;ok Ka&#287;&#305;t</th></tr>");
        foreach (var n in names)
        {
            var au = adUsers.FirstOrDefault(u => string.Equals(u.Sam, n, StringComparison.OrdinalIgnoreCase));
            List<Sent> st; stats.TryGetValue(n, out st);
            int eng; blocked.TryGetValue(n, out eng);
            string kagit = st != null ? st.GroupBy(s => s.Paper).OrderByDescending(g => g.Count()).First().Key : "";
            sb.Append("<tr><td>").Append(H(n)).Append("</td>")
              .Append("<td>").Append(au != null ? "&#10003;" : "&mdash;").Append("</td>")
              .Append("<td>").Append(H(au != null ? au.Ad : "")).Append("</td>")
              .Append("<td>").Append(au == null ? "" : (au.Aktif ? "<span class='ok'>Aktif</span>" : "<span class='wait'>Devre d&#305;&#351;&#305;</span>")).Append("</td>")
              .Append("<td>").Append(au != null && au.LastLogon.HasValue ? au.LastLogon.Value.ToString("yyyy-MM-dd HH:mm") : "").Append("</td>")
              .Append("<td>").Append(st != null ? st.Count : 0).Append("</td>")
              .Append("<td>").Append(st != null ? st.Sum(s => s.PageN) : 0).Append("</td>")
              .Append(mU ? "<td>" + (st != null ? Para(ToplamMaliyet(st, costsU)) : "") + "</td>" : "")
              .Append("<td>").Append(st != null ? Math.Round(st.Sum(s => (double)s.KbN) / 1024.0, 1) + " MB" : "").Append("</td>")
              .Append("<td>").Append(eng > 0 ? "<span class='wait'>" + eng + "</span>" : "0").Append("</td>")
              .Append("<td>").Append(st != null ? st.Max(s => s.Time).ToString("yyyy-MM-dd HH:mm") : "").Append("</td>")
              .Append("<td>").Append(H(kagit)).Append("</td></tr>");
        }
        sb.Append("</table>");

        // Kisi bazli maliyet dokumu: kullanici x kagit turu matrisi
        if (mU)
        {
            var kagitTurleri = sent.GroupBy(s => s.Paper).OrderByDescending(g => g.Sum(x => x.PageN))
                                   .Select(g => g.Key).ToList();
            sb.Append("<h2>Ki&#351;i Bazl&#305; Maliyet D&ouml;k&uuml;m&uuml; (ka&#287;&#305;t t&uuml;r&uuml;ne g&ouml;re)</h2>");
            sb.Append("<a class='exp' href='/export/maliyet.csv'>&#11015; Excel'e Aktar</a>");
            sb.Append("<table><tr><th>Kullan&#305;c&#305;</th>");
            foreach (var k in kagitTurleri) sb.Append("<th>").Append(H(k)).Append("</th>");
            sb.Append("<th>Toplam</th></tr>");
            foreach (var u in stats.OrderByDescending(kv => ToplamMaliyet(kv.Value, costsU)))
            {
                sb.Append("<tr><td>").Append(H(u.Key)).Append("</td>");
                foreach (var k in kagitTurleri)
                {
                    var alt = u.Value.Where(s => s.Paper.Equals(k, StringComparison.OrdinalIgnoreCase)).ToList();
                    sb.Append("<td>").Append(alt.Count > 0
                        ? alt.Sum(s => s.PageN) + " sf<br><b>" + Para(ToplamMaliyet(alt, costsU)) + "</b>"
                        : "&mdash;").Append("</td>");
                }
                sb.Append("<td><b>").Append(Para(ToplamMaliyet(u.Value, costsU))).Append("</b></td></tr>");
            }
            // Genel toplam satiri
            sb.Append("<tr style='background:#eef3fa;font-weight:600'><td>TOPLAM</td>");
            foreach (var k in kagitTurleri)
            {
                var alt = sent.Where(s => s.Paper.Equals(k, StringComparison.OrdinalIgnoreCase)).ToList();
                sb.Append("<td>").Append(alt.Sum(s => s.PageN)).Append(" sf<br>").Append(Para(ToplamMaliyet(alt, costsU))).Append("</td>");
            }
            sb.Append("<td>").Append(Para(ToplamMaliyet(sent, costsU))).Append("</td></tr>");
            sb.Append("</table>");
        }
        else
            sb.Append("<div class='info' style='margin-top:16px'>Ki&#351;i bazl&#305; maliyet d&ouml;k&uuml;m&uuml; i&ccedil;in ")
              .Append("<a href='/yetki'>Yetkiler &rarr; Maliyet Tan&#305;mlar&#305;</a> b&ouml;l&uuml;m&uuml;nden ka&#287;&#305;t fiyatlar&#305;n&#305; girin.</div>");
        return sb.ToString();
    }

    // Yetki yonetimi: kullanici/makine engeli + gunluk sayfa kotasi
    static string PageRules(string mesaj)
    {
        var all = LoadSent();
        var rules = LoadRules();
        var adUsers = LoadAdUsers();
        var ad = LoadAd();
        var users = adUsers.Select(u => u.Sam).Union(all.Select(s => s.User), StringComparer.OrdinalIgnoreCase)
                           .Where(n => n.Length > 0).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var machines = ad.Select(a => a.Name).Union(all.Select(s => s.Machine), StringComparer.OrdinalIgnoreCase)
                         .Where(n => n.Length > 0).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        var sb = new StringBuilder();
        if (mesaj != null) sb.Append("<div class='info'>").Append(mesaj).Append("</div>");
        sb.Append("<div class='info'>&#304;&#351;aretlenen kullan&#305;c&#305;/makine yazd&#305;ramaz; engellenen i&#351;ler kay&#305;tlara \"Engellendi\" olarak ge&ccedil;er. ")
          .Append("Kota: kullan&#305;c&#305;n&#305;n g&uuml;nl&uuml;k toplam sayfa limiti (0 = limitsiz). Denetim sunucu taraf&#305;nda uygulan&#305;r.</div>");
        sb.Append("<form method='post' action='/yetki/kaydet'>");

        sb.Append("<div class='cols'><div>");
        sb.Append("<h2>Kullan&#305;c&#305;lar</h2><table><tr><th>Kullan&#305;c&#305;</th><th>Engelle</th><th>G&uuml;nl&uuml;k Sayfa Kotas&#305;</th></tr>");
        foreach (var u in users)
        {
            string[] r; rules.TryGetValue("user|" + u, out r);
            bool eng = r != null && r[2] == "1";
            string kota = r != null && r.Length > 3 ? r[3] : "0";
            string enc = WebUtility.UrlEncode(u);
            sb.Append("<tr><td>").Append(H(u)).Append("</td>")
              .Append("<td><input type='checkbox' name='b_u_").Append(enc).Append("' value='1'").Append(eng ? " checked" : "").Append("></td>")
              .Append("<td><input type='number' name='q_u_").Append(enc).Append("' value='").Append(H(kota)).Append("' min='0' style='width:80px'></td></tr>");
        }
        sb.Append("</table></div><div>");
        sb.Append("<h2>Makineler</h2><table><tr><th>Makine</th><th>Engelle</th></tr>");
        foreach (var m in machines)
        {
            string[] r; rules.TryGetValue("machine|" + m, out r);
            bool eng = r != null && r[2] == "1";
            sb.Append("<tr><td>").Append(H(m)).Append("</td>")
              .Append("<td><input type='checkbox' name='b_m_").Append(WebUtility.UrlEncode(m)).Append("' value='1'").Append(eng ? " checked" : "").Append("></td></tr>");
        }
        sb.Append("</table></div></div>");
        sb.Append("<div style='margin-top:16px'><button type='submit' class='dateform' style='background:#1f3a5f;color:#fff;border:0;border-radius:8px;padding:10px 26px;font-size:14px;cursor:pointer'>Kaydet</button></div>");
        sb.Append("</form>");

        // Maliyet tanimlari (kagit turu basina sayfa fiyati)
        sb.Append("<h2>Maliyet Tan&#305;mlar&#305; (sayfa ba&#351;&#305;na &#8378;)</h2>");
        var costs = LoadCosts();
        if (Db.Ok())
        {
            var kagitlar = new List<string> { "A3", "A4", "A5", "A6", "Letter", "Legal", "B4", "B5", "Bilinmiyor", "*" };
            foreach (var p in all.Select(s => s.Paper).Distinct(StringComparer.OrdinalIgnoreCase))
                if (p.Length > 0 && !kagitlar.Contains(p, StringComparer.OrdinalIgnoreCase)) kagitlar.Add(p);
            sb.Append("<div class='info'>Ka&#287;&#305;t t&uuml;r&uuml; ba&#351;&#305;na sayfa maliyeti girin (&ouml;rn. 0,50). ")
              .Append("<b>*</b> sat&#305;r&#305; tan&#305;ms&#305;z t&uuml;rler i&ccedil;in ge&ccedil;erlidir. Bo&#351;/0 = maliyetsiz. ")
              .Append("Fiyat girildi&#287;inde t&uuml;m raporlara maliyet s&uuml;tunlar&#305; eklenir.</div>");
            sb.Append("<form method='post' action='/yetki/maliyet'><table style='max-width:420px'><tr><th>Ka&#287;&#305;t</th><th>Sayfa Fiyat&#305; (&#8378;)</th></tr>");
            foreach (var k in kagitlar)
            {
                decimal f; costs.TryGetValue(k, out f);
                sb.Append("<tr><td>").Append(k == "*" ? "* (di&#287;er t&uuml;m t&uuml;rler)" : H(k)).Append("</td>")
                  .Append("<td><input type='text' name='c_").Append(WebUtility.UrlEncode(k)).Append("' value='")
                  .Append(f > 0 ? f.ToString("0.####", CultureInfo.InvariantCulture).Replace(".", ",") : "")
                  .Append("' placeholder='0,00' style='width:90px'></td></tr>");
            }
            sb.Append("</table><div style='margin-top:10px'><button type='submit' style='background:#1f3a5f;color:#fff;border:0;border-radius:8px;padding:8px 22px;font-size:13px;cursor:pointer'>Maliyetleri Kaydet</button></div></form>");
        }
        else sb.Append("<div class='warn'>SQL yok: maliyet tan&#305;mlar&#305; kullan&#305;lam&#305;yor.</div>");

        // Istemci sifreleri (ClientKeys) yonetimi
        sb.Append("<h2>&#304;stemci &#350;ifreleri</h2>");
        var ck = Db.Query("SELECT Makine, Olusturma FROM ClientKeys ORDER BY Makine");
        if (ck == null)
            sb.Append("<div class='warn'>SQL yok: istemci &#351;ifreleri y&ouml;netilemiyor.</div>");
        else
        {
            sb.Append("<div class='info'>Her istemci kurulumda belirlenen &#351;ifresiyle sunucuya kaydolur (ilk temas). ")
              .Append("Kay&#305;tl&#305; makineden gelen hatal&#305; &#351;ifre reddedilir ve G&uuml;venlik uyar&#305;s&#305; d&uuml;&#351;er. ")
              .Append("S&#305;f&#305;rlarsan&#305;z makine bir sonraki temas&#305;nda yeni &#351;ifresiyle yeniden kaydolur.</div>");
            sb.Append("<table><tr><th>Makine</th><th>Kay&#305;t Tarihi</th><th></th></tr>");
            foreach (System.Data.DataRow r in ck.Rows)
                sb.Append("<tr><td>").Append(H(Convert.ToString(r[0]))).Append("</td><td>")
                  .Append(r[1] == DBNull.Value ? "" : Convert.ToDateTime(r[1]).ToString("yyyy-MM-dd HH:mm"))
                  .Append("</td><td><form method='post' action='/yetki/anahtar' style='margin:0'>")
                  .Append("<input type='hidden' name='makine' value='").Append(H(Convert.ToString(r[0]))).Append("'>")
                  .Append("<button type='submit' style='background:#c05555;color:#fff;border:0;border-radius:6px;padding:4px 14px;font-size:12px;cursor:pointer'>S&#305;f&#305;rla</button>")
                  .Append("</form></td></tr>");
            if (ck.Rows.Count == 0) sb.Append("<tr><td colspan='3' class='mut'>Hen&uuml;z &#351;ifreli istemci kayd&#305; yok.</td></tr>");
            sb.Append("</table>");
        }
        return sb.ToString();
    }

    // Uyarilar sayfasi (SQL Alerts tablosu) + e-posta raporu durumu
    static string PageAlerts()
    {
        var sb = new StringBuilder();
        var mc = MailCfg();
        if (mc.ContainsKey("Smtp") && mc.ContainsKey("Kime"))
            sb.Append("<div class='info'>&#128231; G&uuml;nl&uuml;k e-posta raporu aktif: her g&uuml;n <b>")
              .Append(H(mc.ContainsKey("Saat") ? mc["Saat"] : "08:00")).Append("</b> &rarr; <b>").Append(H(mc["Kime"]))
              .Append("</b> <form method='post' action='/rapor/test' style='display:inline;margin-left:10px'>")
              .Append("<button type='submit' style='background:#1f3a5f;color:#fff;border:0;border-radius:6px;padding:5px 14px;font-size:12px;cursor:pointer'>&#9993; Test g&ouml;nder</button></form></div>");
        else
            sb.Append("<div class='warn'>G&uuml;nl&uuml;k e-posta raporu yap&#305;land&#305;r&#305;lmam&#305;&#351;. ")
              .Append("Kurulumu yeniden &ccedil;al&#305;&#351;t&#305;r&#305;n veya <code>C:\\Print360\\mail.ini</code> olu&#351;turun ")
              .Append("(Smtp=, Port=587, TLS=1, Kullanici=, Sifre=, Kimden=, Kime=, Saat=08:00).</div>");
        if (lastMailResult != null)
            sb.Append("<div class='info'>Son test: ").Append(lastMailResult).Append("</div>");
        // MSSQL ZORUNLU DEGIL: SQL varsa oradan, yoksa dosya deposundan oku.
        // [tarih, tur, mesaj, okundu]
        var kayit = new List<string[]>();
        var dt = Db.Query("SELECT TOP 200 Tarih,Tur,Mesaj,Okundu FROM Alerts ORDER BY Id DESC");
        if (dt != null)
            foreach (System.Data.DataRow r in dt.Rows)
                kayit.Add(new[] { r[0] == DBNull.Value ? "" : Convert.ToDateTime(r[0]).ToString("yyyy-MM-dd HH:mm:ss"),
                                  Convert.ToString(r[1]), Convert.ToString(r[2]),
                                  Convert.ToBoolean(r[3]) ? "1" : "0" });
        else
            kayit = Db.UyarilariOku(200);

        int okunmamis = 0, bugun = 0;
        foreach (var r in kayit)
        {
            if (r[3] != "1") okunmamis++;
            DateTime t; if (DateTime.TryParse(r[0], out t) && t.Date == DateTime.Today) bugun++;
        }
        sb.Append("<div class='cards'>");
        Card(sb, okunmamis.ToString(), "Okunmam&#305;&#351;");
        Card(sb, bugun.ToString(), "Bug&uuml;n");
        Card(sb, kayit.Count.ToString(), "Son 200 Kay&#305;t");
        sb.Append("</div>");
        sb.Append("<form method='post' action='/uyarilar/okundu' style='margin-bottom:12px'>")
          .Append("<button type='submit' style='background:#1f3a5f;color:#fff;border:0;border-radius:8px;padding:8px 20px;font-size:13px;cursor:pointer'>T&uuml;m&uuml;n&uuml; okundu i&#351;aretle</button></form>");
        sb.Append("<table><tr><th>Tarih</th><th>T&uuml;r</th><th>Mesaj</th><th>Durum</th></tr>");
        foreach (var r in kayit)
        {
            bool okundu = r[3] == "1";
            sb.Append("<tr").Append(okundu ? ">" : " style='font-weight:600'>")
              .Append("<td>").Append(H(r[0]))
              .Append("</td><td>").Append(H(r[1]))
              .Append("</td><td>").Append(H(r[2]))
              .Append("</td><td>").Append(okundu ? "<span class='mut'>Okundu</span>" : "<span class='wait'>Yeni</span>")
              .Append("</td></tr>");
        }
        if (kayit.Count == 0) sb.Append("<tr><td colspan='4' class='mut'>Hen&uuml;z uyar&#305; yok.</td></tr>");
        sb.Append("</table>");
        return sb.ToString();
    }

    static string SaveRules(HttpListenerContext ctx)
    {
        if (ctx.Request.HttpMethod != "POST") { Redirect(ctx, "/yetki"); return null; }
        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            body = sr.ReadToEnd();
        var engel = new HashSet<string>();
        var kota = new Dictionary<string, int>();
        foreach (var kv in body.Split('&'))
        {
            var p = kv.Split(new[] { '=' }, 2);
            if (p.Length != 2) continue;
            string key = WebUtility.UrlDecode(p[0]), val = WebUtility.UrlDecode(p[1]);
            if (key.StartsWith("b_u_")) engel.Add("user|" + WebUtility.UrlDecode(key.Substring(4)));
            else if (key.StartsWith("b_m_")) engel.Add("machine|" + WebUtility.UrlDecode(key.Substring(4)));
            else if (key.StartsWith("q_u_"))
            {
                int q; int.TryParse(val, out q);
                if (q > 0) kota["user|" + WebUtility.UrlDecode(key.Substring(4))] = q;
            }
        }
        var sb = new StringBuilder();
        foreach (var k in engel.Union(kota.Keys))
        {
            var parts = k.Split(new[] { '|' }, 2);
            int q; kota.TryGetValue(k, out q);
            sb.Append("\"").Append(parts[0]).Append("\",\"").Append(parts[1].Replace("\"", "\"\""))
              .Append("\",\"").Append(engel.Contains(k) ? "1" : "0").Append("\",\"").Append(q).Append("\"\r\n");
        }
        File.WriteAllText(rulesCsv, sb.ToString());
        // SQL'e de yaz (ajanin birincil kaynagi)
        if (Db.Exec("DELETE FROM Rules"))
            foreach (var k in engel.Union(kota.Keys))
            {
                var parts = k.Split(new[] { '|' }, 2);
                int q; kota.TryGetValue(k, out q);
                Db.Exec("INSERT INTO Rules(Tip,Ad,Engel,Kota) VALUES(@t,@a,@e,@k)",
                    "@t", parts[0], "@a", parts[1], "@e", engel.Contains(k) ? 1 : 0, "@k", q);
            }
        Log("Yetki kurallari guncellendi (" + engel.Count + " engel, " + kota.Count + " kota)");
        Redirect(ctx, "/yetki");
        return null;
    }

    // ============================================================
    //  TANI SAYFASI - "yazdiriyorum ama bir sey olmuyor" sorununu
    //  tahmin yurutmeden gosterir: yazicilar, surucu, spool, kuyruk,
    //  ajan durumu ve son gunluk satirlari.
    // ============================================================
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PRN_INFO_2
    {
        public string pServerName, pPrinterName, pShareName, pPortName, pDriverName, pComment, pLocation;
        public IntPtr pDevMode;
        public string pSepFile, pPrintProcessor, pDatatype, pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, cJobs, AveragePPM;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DRV_INFO_1 { public string pName; }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool EnumPrinters(uint Flags, string Name, uint Level, IntPtr pPrinterEnum,
                                    uint cbBuf, out uint pcbNeeded, out uint pcReturned);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool EnumPrinterDrivers(string pName, string pEnvironment, uint Level,
                                          IntPtr pDriverInfo, uint cbBuf, out uint pcbNeeded, out uint pcReturned);
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, int wtsInfoClass,
                                                  out IntPtr ppBuffer, out uint pBytesReturned);
    [DllImport("wtsapi32.dll")]
    static extern void WTSFreeMemory(IntPtr pMemory);

    static List<string[]> Yazicilar()
    {
        var liste = new List<string[]>();
        try
        {
            uint gerek, adet;
            EnumPrinters(2 /*LOCAL*/, null, 2, IntPtr.Zero, 0, out gerek, out adet);
            if (gerek == 0) return liste;
            IntPtr buf = Marshal.AllocHGlobal((int)gerek);
            try
            {
                if (!EnumPrinters(2, null, 2, buf, gerek, out gerek, out adet)) return liste;
                int boyut = Marshal.SizeOf(typeof(PRN_INFO_2));
                for (int i = 0; i < adet; i++)
                {
                    var pi = (PRN_INFO_2)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + i * boyut), typeof(PRN_INFO_2));
                    liste.Add(new[] { pi.pPrinterName ?? "", pi.pPortName ?? "", pi.pDriverName ?? "" });
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { }
        return liste;
    }

    static bool SurucuVar(string ad)
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
                int boyut = Marshal.SizeOf(typeof(DRV_INFO_1));
                for (int i = 0; i < adet; i++)
                {
                    var di = (DRV_INFO_1)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + i * boyut), typeof(DRV_INFO_1));
                    if (string.Equals(di.pName, ad, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { }
        return false;
    }

    static void TaniKutu(StringBuilder sb, string baslik, bool iyi, string mesaj)
    {
        // baslik/mesaj zaten HTML varliklari icerir -> tekrar kacislama YOK
        sb.Append("<div class='").Append(iyi ? "info" : "warn").Append("'><b>")
          .Append(iyi ? "&#10003; " : "&#9888; ").Append(baslik).Append("</b><br>").Append(mesaj).Append("</div>");
    }

    static void SonSatirlar(StringBuilder sb, string baslik, string dosya, int adet)
    {
        sb.Append("<h2>").Append(baslik).Append("</h2>");   // baslik zaten HTML varlikli
        try
        {
            if (!File.Exists(dosya)) { sb.Append("<div class='mut'>Dosya yok: ").Append(H(dosya)).Append("</div>"); return; }
            string[] tum;
            using (var fs = new FileStream(dosya, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
                tum = sr.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            sb.Append("<pre style='background:#fff;border-radius:8px;padding:12px;font-size:12px;overflow:auto;max-height:320px'>");
            for (int i = Math.Max(0, tum.Length - adet); i < tum.Length; i++) sb.Append(H(tum[i])).Append("\n");
            sb.Append("</pre>");
        }
        catch (Exception ex) { sb.Append("<div class='mut'>Okunamadi: ").Append(H(ex.Message)).Append("</div>"); }
    }

    static string PageTani()
    {
        var sb = new StringBuilder();
        sb.Append("<div class='info'>Bu sayfa &quot;yazd&#305;r&#305;yorum ama bir &#351;ey olmuyor&quot; sorununu ")
          .Append("ad&#305;m ad&#305;m g&ouml;sterir. Sorun varsa turuncu kutulara bak&#305;n.</div>");

        // 0) KURULU SURUMLER - "kurdum ama eski surum calisiyor" karisikligini bitirir.
        //    Dosyalarin surumu ile panelin surumu AYNI olmalidir.
        sb.Append("<h2>0. Kurulu s&uuml;r&uuml;mler</h2><table><tr><th>Bile&#351;en</th><th>S&uuml;r&uuml;m</th><th>Tarih</th></tr>");
        sb.Append("<tr><td>Panel (bu sayfa)</td><td><b>").Append(H(Surum.V)).Append("</b></td><td>").Append(H(Surum.YapimTam)).Append("</td></tr>");
        foreach (var bn in new[] { "Print360.ServerAgent.exe", "Print360.Dashboard.exe", "Print360.Panel.exe" })
        {
            string yol = Path.Combine(@"C:\Print360", bn), v = "(yok)", t = "";
            try
            {
                if (File.Exists(yol))
                {
                    v = System.Diagnostics.FileVersionInfo.GetVersionInfo(yol).FileVersion ?? "?";
                    t = File.GetLastWriteTime(yol).ToString("yyyy-MM-dd HH:mm");
                }
            }
            catch { }
            sb.Append("<tr><td>").Append(H(bn)).Append("</td><td>").Append(H(v))
              .Append("</td><td>").Append(H(t)).Append("</td></tr>");
        }
        sb.Append("<tr><td>Veritaban&#305;</td><td><b>").Append(H(Db.MotorAdi)).Append("</b></td><td>")
          .Append(Db.Motor == Db.DbMotor.Sqlite ? H(Db.SqliteDosya) : "&mdash;").Append("</td></tr>");
        sb.Append("</table>");
        try
        {
            var pr = System.Diagnostics.Process.GetProcessesByName("Print360.ServerAgent");
            if (pr.Length > 0)
            {
                string cv = "?";
                try { cv = System.Diagnostics.FileVersionInfo.GetVersionInfo(pr[0].MainModule.FileName).FileVersion; } catch { }
                sb.Append("<div class='mut'>&Ccedil;al&#305;&#351;an ajan s&uuml;r&uuml;m&uuml;: <b>").Append(H(cv))
                  .Append("</b> &mdash; dosya s&uuml;r&uuml;m&uuml;nden farkl&#305;ysa ajan yeniden ba&#351;lat&#305;lmal&#305;.</div>");
            }
        }
        catch { }

        // 1) Yazici surucusu
        bool srv = SurucuVar("Microsoft Print to PDF");
        TaniKutu(sb, "1. Yaz&#305;c&#305; s&uuml;r&uuml;c&uuml;s&uuml; (Microsoft Print to PDF)", srv,
            srv ? "S&uuml;r&uuml;c&uuml; kurulu. Sanal yaz&#305;c&#305;lar olu&#351;turulabilir."
                : "<b>S&Uuml;R&Uuml;C&Uuml; YOK!</b> Windows Server'da bu &ouml;zellik varsay&#305;lan kapal&#305;d&#305;r ve " +
                  "bu y&uuml;zden Print360 yaz&#305;c&#305;lar&#305; olu&#351;turulamaz &mdash; yazd&#305;rma &ccedil;al&#305;&#351;maz.<br>" +
                  "&Ccedil;&ouml;z&uuml;m: Sunucu Y&ouml;neticisi &rarr; &Ouml;zellik Ekle &rarr; <b>Microsoft Print to PDF</b>, " +
                  "sonra Print360 sunucu kurulumunu tekrar &ccedil;al&#305;&#351;t&#305;r&#305;n.");

        // 2) Print360 yazicilari
        var yaz = Yazicilar();
        var p360 = yaz.Where(x => x[0].StartsWith("Print360", StringComparison.OrdinalIgnoreCase)).ToList();
        sb.Append("<h2>2. Print360 Yaz&#305;c&#305;lar&#305;</h2>");
        if (p360.Count == 0)
            TaniKutu(sb, "Hi&ccedil; Print360 yaz&#305;c&#305;s&#305; yok!", false,
                "Kullan&#305;c&#305;lar yazd&#305;ramaz. Sunucu kurulumunu tekrar &ccedil;al&#305;&#351;t&#305;r&#305;n " +
                "(&ouml;nce yukar&#305;daki s&uuml;r&uuml;c&uuml; sorununu &ccedil;&ouml;z&uuml;n).");
        else
        {
            sb.Append("<table><tr><th>Yaz&#305;c&#305;</th><th>Port (spool dosyas&#305;)</th><th>S&uuml;r&uuml;c&uuml;</th></tr>");
            foreach (var y in p360)
                sb.Append("<tr><td>").Append(H(y[0])).Append("</td><td>").Append(H(y[1]))
                  .Append("</td><td>").Append(H(y[2])).Append("</td></tr>");
            sb.Append("</table>");
        }

        // 3) Ajan calisiyor mu
        var ajan = System.Diagnostics.Process.GetProcessesByName("Print360.ServerAgent");
        TaniKutu(sb, "3. Yazd&#305;rma ajan&#305;", ajan.Length > 0,
            ajan.Length > 0
                ? ajan.Length + " ajan &ccedil;al&#305;&#351;&#305;yor. <b>Not:</b> ajan, yazd&#305;ran kullan&#305;c&#305;n&#305;n " +
                  "KEND&#304; oturumunda &ccedil;al&#305;&#351;mal&#305;d&#305;r."
                : "<b>AJAN &Ccedil;ALI&#350;MIYOR!</b> &#304;&#351;ler yakalanmaz. Ba&#351;lat&#305;n: " +
                  "<code>C:\\Print360\\Print360.ServerAgent.exe</code>");

        // 3b) RDP istemci adi - BOSSA is kuyruga yazilamaz (isler spool'da birikir)
        string istAdi = "";
        try
        {
            IntPtr b; uint n;
            if (WTSQuerySessionInformation(IntPtr.Zero, -1, 10 /*WTSClientName*/, out b, out n))
            { try { istAdi = (Marshal.PtrToStringUni(b) ?? "").Trim(); } finally { WTSFreeMemory(b); } }
        }
        catch { }
        if (istAdi.Length == 0) istAdi = (Environment.GetEnvironmentVariable("CLIENTNAME") ?? "").Trim();
        TaniKutu(sb, "3b. Bu oturumun RDP istemci ad&#305;", istAdi.Length > 0,
            istAdi.Length > 0
                ? "&#304;stemci: <b>" + H(istAdi) + "</b> &mdash; i&#351;ler bu makineye g&ouml;nderilir."
                : "<b>BO&#350;!</b> Bu oturum bir RDP oturumu de&#287;il (konsol/hizmet oturumu). " +
                  "Ajan b&ouml;yle bir oturumda &ccedil;al&#305;&#351;&#305;rsa i&#351;in K&#304;ME g&ouml;nderilece&#287;ini bilemez ve " +
                  "i&#351;ler spool'da birikir.<br>Yazd&#305;ran kullan&#305;c&#305; sunucuya <b>RDP ile</b> ba&#287;lanmal&#305; ve " +
                  "ajan o oturumda &ccedil;al&#305;&#351;mal&#305;d&#305;r.");

        // 4) Spool (yakalanmayi bekleyen isler)
        sb.Append("<h2>4. Spool klas&ouml;r&uuml; <span class='mut'>(C:\\Print360\\spool)</span></h2>");
        try
        {
            var sp = Directory.Exists(@"C:\Print360\spool")
                   ? new DirectoryInfo(@"C:\Print360\spool").GetFiles() : new FileInfo[0];
            if (sp.Length == 0)
                sb.Append("<div class='mut'>Bo&#351; &mdash; bu normaldir (i&#351;ler an&#305;nda al&#305;n&#305;r). ")
                  .Append("Ama <b>hi&ccedil; yazd&#305;ramad&#305;ysan&#305;z</b> ve burada da bir &#351;ey olu&#351;muyorsa, ")
                  .Append("yazd&#305;rd&#305;&#287;&#305;n&#305;z yaz&#305;c&#305; bir Print360 yaz&#305;c&#305;s&#305; DE&#286;&#304;LD&#304;R.</div>");
            else
            {
                sb.Append("<table><tr><th>Dosya</th><th>Boyut</th><th>Zaman</th></tr>");
                foreach (var f in sp.OrderByDescending(x => x.LastWriteTime).Take(20))
                    sb.Append("<tr><td>").Append(H(f.Name)).Append("</td><td>").Append(f.Length)
                      .Append("</td><td>").Append(f.LastWriteTime.ToString("HH:mm:ss")).Append("</td></tr>");
                sb.Append("</table>");
                sb.Append("<div class='warn'>Spool'da bekleyen i&#351; var &mdash; ajan bunlar&#305; alam&#305;yor demektir.</div>");
            }
        }
        catch (Exception ex) { sb.Append("<div class='mut'>").Append(H(ex.Message)).Append("</div>"); }

        // 5) Kuyruk (istemciye gitmeyi bekleyen isler)
        sb.Append("<h2>5. G&ouml;nderim kuyru&#287;u <span class='mut'>(C:\\Print360\\queue)</span></h2>");
        try
        {
            int n = 0; var satir = new StringBuilder();
            if (Directory.Exists(@"C:\Print360\queue"))
                foreach (var d in Directory.GetDirectories(@"C:\Print360\queue"))
                    foreach (var f in Directory.GetFiles(d, "*.gz"))
                    {
                        n++;
                        if (n <= 20) satir.Append("<tr><td>").Append(H(Path.GetFileName(d))).Append("</td><td>")
                            .Append(H(Path.GetFileName(f))).Append("</td></tr>");
                    }
            if (n == 0) sb.Append("<div class='mut'>Bo&#351; &mdash; bekleyen i&#351; yok.</div>");
            else sb.Append("<table><tr><th>Makine</th><th>&#304;&#351;</th></tr>").Append(satir)
                   .Append("</table><div class='warn'>").Append(n)
                   .Append(" i&#351; istemciyi bekliyor (istemci ajan&#305; kapal&#305; olabilir).</div>");
        }
        catch (Exception ex) { sb.Append("<div class='mut'>").Append(H(ex.Message)).Append("</div>"); }

        // 6) Gunlukler
        try
        {
            var lg = Directory.Exists(@"C:\Print360\logs")
                   ? new DirectoryInfo(@"C:\Print360\logs").GetFiles("server-*.log")
                       .OrderByDescending(f => f.LastWriteTime).FirstOrDefault() : null;
            if (lg != null) SonSatirlar(sb, "6. Ajan g&uuml;nl&uuml;&#287;&uuml; (" + lg.Name + ")", lg.FullName, 30);
            else sb.Append("<h2>6. Ajan g&uuml;nl&uuml;&#287;&uuml;</h2><div class='warn'>Hi&ccedil; ajan g&uuml;nl&uuml;&#287;&uuml; yok &mdash; ajan hi&ccedil; &ccedil;al&#305;&#351;mam&#305;&#351; olabilir.</div>");
        }
        catch { }
        SonSatirlar(sb, "7. Kurulum g&uuml;nl&uuml;&#287;&uuml;", @"C:\Print360\logs\kurulum.log", 40);
        return sb.ToString();
    }

    // ---- Veritabani sayfasi: kurulumda SQL secilmediyse panelden aktiflestir ----
    static string vtMesaj, vtHata;

    static string PageVeritabani(HttpListenerContext ctx)
    {
        if (ctx.Request.HttpMethod == "POST")
        {
            string body;
            using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = sr.ReadToEnd();
            string srv = "", usr = "", pwd = "";
            foreach (var kv in body.Split('&'))
            {
                var p = kv.Split(new[] { '=' }, 2);
                if (p.Length != 2) continue;
                string v = WebUtility.UrlDecode(p[1].Replace('+', ' '));
                if (p[0] == "srv") srv = v.Trim();
                else if (p[0] == "usr") usr = v.Trim();
                else if (p[0] == "pwd") pwd = v;
            }
            if (usr.Length == 0) usr = "sa";
            string m;
            if (Db.SqlKur(srv, usr, pwd, out m))
            {
                vtMesaj = m; vtHata = null;
                Db.Alert("Veritabani", "MSSQL panelden etkinlestirildi: " + srv);
                Log("MSSQL panelden etkinlestirildi: " + srv);
            }
            else { vtHata = m; vtMesaj = null; }
        }

        var motor = Db.Motor;
        bool bagli = motor == Db.DbMotor.MsSql;
        var sb = new StringBuilder();
        if (vtMesaj != null) sb.Append("<div class='info'>&#10003; ").Append(H(vtMesaj)).Append("</div>");
        if (vtHata != null) sb.Append("<div class='warn'>Ba&#287;lan&#305;lamad&#305;: ").Append(H(vtHata)).Append("</div>");

        sb.Append("<div class='cards'>");
        Card(sb, H(Db.MotorAdi), "Aktif Veritaban&#305;");
        sb.Append("</div>");

        if (motor == Db.DbMotor.Sqlite)
        {
            long boy = 0; try { if (File.Exists(Db.SqliteDosya)) boy = new FileInfo(Db.SqliteDosya).Length; } catch { }
            sb.Append("<div class='info'><b>SQLite kullan&#305;l&#305;yor &mdash; MSSQL GEREKM&#304;YOR.</b><br>")
              .Append("T&uuml;m kay&#305;tlar (i&#351;ler, sayaçlar, yaz&#305;c&#305;lar, uyar&#305;lar, kotalar) yerel veritaban&#305;na yaz&#305;l&#305;yor: ")
              .Append("<code>").Append(H(Db.SqliteDosya)).Append("</code> (").Append(boy / 1024).Append(" KB).<br>")
              .Append("Kurulum gerektirmez, yedeklemek i&ccedil;in bu tek dosyay&#305; kopyalaman&#305;z yeterlidir. ")
              .Append("&#304;sterseniz a&#351;a&#287;&#305;dan MSSQL'e ge&ccedil;ebilirsiniz.</div>");
        }
        else if (motor == Db.DbMotor.Yok)
            sb.Append("<div class='warn'>Ne MSSQL ne SQLite kullan&#305;labiliyor; sistem CSV dosya modunda. ")
              .Append(Db.Err != null ? "<br><span class='mut'>" + H(Db.Err) + "</span>" : "").Append("</div>");

        if (bagli)
            sb.Append("<div class='info'><b>Veritaban&#305; ba&#287;l&#305;.</b> T&uuml;m kay&#305;tlar MSSQL'e yaz&#305;l&#305;yor; ")
              .Append("raporlar, kotalar ve merkezi sayaçlar tam olarak &ccedil;al&#305;&#351;&#305;yor. ")
              .Append("Ayarlar&#305; de&#287;i&#351;tirmek i&ccedil;in a&#351;a&#287;&#305;daki formu kullanabilirsiniz.</div>");
        else
            sb.Append("<div class='warn'><b>&#350;u anda CSV/dosya modundas&#305;n&#305;z.</b> Sistem tam &ccedil;al&#305;&#351;&#305;yor, ")
              .Append("ancak ge&ccedil;mi&#351;e d&ouml;n&uuml;k raporlama ve kotalar i&ccedil;in MSSQL &ouml;nerilir. ")
              .Append("Kurulumda veritaban&#305;n&#305; se&ccedil;mediyseniz buradan etkinle&#351;tirebilirsiniz.")
              .Append(Db.Err != null ? "<br><span class='mut'>Son hata: " + H(Db.Err) + "</span>" : "")
              .Append("</div>");

        sb.Append("<h2>MSSQL Ba&#287;lant&#305;s&#305;</h2>")
          .Append("<form method='post' action='/veritabani'>")
          .Append("<table>")
          .Append("<tr><th>SQL sunucusu</th><td><input name='srv' style='width:280px' value='")
          .Append(H(Environment.MachineName)).Append("' placeholder='SUNUCU veya SUNUCU\\ORNEK'></td></tr>")
          .Append("<tr><th>Kullan&#305;c&#305;</th><td><input name='usr' style='width:280px' value='sa'></td></tr>")
          .Append("<tr><th>&#350;ifre</th><td><input name='pwd' type='password' style='width:280px'></td></tr>")
          .Append("</table>")
          .Append("<p><button type='submit'>Veritaban&#305;n&#305; Kur ve Ba&#287;lan</button></p>")
          .Append("</form>")
          .Append("<div class='mut'>Bu i&#351;lem <b>Print360</b> veritaban&#305;n&#305; olu&#351;turur (yoksa), ")
          .Append("tablolar&#305; kurar ve <code>C:\\Print360\\db.ini</code> dosyas&#305;n&#305; g&uuml;nceller. ")
          .Append("Mevcut veriler silinmez. De&#287;i&#351;ikli&#287;in t&uuml;m bile&#351;enlerde ge&ccedil;erli olmas&#305; i&ccedil;in ")
          .Append("sunucu ajan&#305;n&#305; (Print360.ServerAgent) yeniden ba&#351;lat&#305;n.</div>");
        return sb.ToString();
    }

    static string PageMachines()
    {
        var sent = LoadSent().Where(s => s.Status == "OK").ToList();
        var printed = LoadPrinted();
        var ad = LoadAd();
        var stats = sent.GroupBy(s => MachineKey(s), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new
            {
                Sent = g.Count(),
                Printed = g.Count(s => printed.ContainsKey(s.File)),
                Pages = g.Sum(s => s.PageN),
                Last = g.Max(s => s.Time)
            }, StringComparer.OrdinalIgnoreCase);

        // AD + yazdiran + bagli (heartbeat) + istemci kayitli (ClientKeys) makineler
        var names = ad.Select(a => a.Name).Union(stats.Keys, StringComparer.OrdinalIgnoreCase)
                      .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var hb = LoadHb();
        foreach (var k in hb.Keys) if (!names.Contains(k, StringComparer.OrdinalIgnoreCase)) names.Add(k);
        var ckM = Db.Query("SELECT Makine FROM ClientKeys");
        if (ckM != null) foreach (System.Data.DataRow r in ckM.Rows)
        { var m = Convert.ToString(r[0]); if (!names.Contains(m, StringComparer.OrdinalIgnoreCase)) names.Add(m); }
        names = names.Where(n => !string.IsNullOrWhiteSpace(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var ping = PingAll(names);

        var sb = new StringBuilder();
        if (adError != null)
            sb.Append("<div class='warn'>Active Directory sorgulanamad&#305; (sunucu etki alan&#305;nda de&#287;il olabilir): ")
              .Append(H(adError)).Append("</div>");
        else
            sb.Append("<div class='info'>Etki alan&#305;nda <b>").Append(ad.Count)
              .Append("</b> bilgisayar bulundu. Ping ve AD listesi 5 dakikada bir yenilenir; ajan ba&#287;lant&#305;s&#305; (&#9679;) son 3 dk kalp at&#305;&#351;&#305;na g&ouml;redir.</div>");

        sb.Append("<a class='exp' href='/export/makineler.csv'>&#11015; Excel'e Aktar</a><br>");
        int online = 0; foreach (var n in names) { bool o; HbDurum(hb, n, out o); if (o) online++; }
        sb.Append("<div class='cards'>");
        Card(sb, names.Count.ToString(), "Toplam Makine");
        Card(sb, online.ToString(), "Ajan &Ccedil;evrimi&ccedil;i");
        Card(sb, ping.Count(p => p.Value).ToString(), "Ping Yan&#305;t&#305;");
        Card(sb, ad.Count.ToString(), "AD Kay&#305;tl&#305;");
        sb.Append("</div>");

        sb.Append("<table><tr><th>Makine</th><th>AD</th><th>&#304;&#351;letim Sistemi</th><th>Ping</th><th>Ajan Ba&#287;lant&#305;s&#305;</th>")
          .Append("<th>&#304;stemci Kullan&#305;c&#305;s&#305;</th><th>&Ccedil;&#305;kt&#305;</th><th>Bas&#305;lan</th><th>Sayfa</th><th>Son Yazd&#305;rma</th><th>Durum</th></tr>");
        foreach (var n in names)
        {
            var a = ad.FirstOrDefault(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
            bool hasStats = stats.ContainsKey(n);
            var st = hasStats ? stats[n] : null;
            bool onl; string hbS = HbDurum(hb, n, out onl);
            bool pi; ping.TryGetValue(n, out pi);
            string durum, cls;
            if (hasStats && st.Last > DateTime.Now.AddDays(-7)) { durum = "Aktif yazd&#305;r&#305;yor"; cls = "ok"; }
            else if (hasStats) { durum = "Pasif (7+ g&uuml;n)"; cls = "wait"; }
            else { durum = "Hi&ccedil; yazd&#305;rmad&#305;"; cls = "mut"; }
            sb.Append("<tr><td>").Append(H(n)).Append("</td>")
              .Append("<td>").Append(a != null ? "&#10003;" : "&mdash;").Append("</td>")
              .Append("<td>").Append(H(a != null ? a.Os : "")).Append("</td>")
              .Append("<td>").Append(pi ? "<span class='ok'>&#10003;</span>" : "&mdash;").Append("</td>")
              .Append("<td>").Append(onl ? "<span class='ok'>" + hbS + "</span>" : (hbS.Length > 0 ? "<span class='mut'>" + hbS + "</span>" : "&mdash;")).Append("</td>")
              .Append("<td>").Append(hb.ContainsKey(n) && hb[n].Length > 4 && hb[n][4].Length > 0
                  ? H(hb[n][4]) + (hb[n].Length > 5 && hb[n][5].Length > 0 ? " <span class='mut'>(" + H(hb[n][5]) + ")</span>" : "") : "").Append("</td>")
              .Append("<td>").Append(hasStats ? st.Sent.ToString() : "0").Append("</td>")
              .Append("<td>").Append(hasStats ? st.Printed.ToString() : "0").Append("</td>")
              .Append("<td>").Append(hasStats ? st.Pages.ToString() : "0").Append("</td>")
              .Append("<td>").Append(hasStats ? st.Last.ToString("yyyy-MM-dd HH:mm") : "").Append("</td>")
              .Append("<td class='").Append(cls).Append("'>").Append(durum).Append("</td></tr>");
        }
        sb.Append("</table>");

        // Baglanti loglari (istemci heartbeat + sunucu RDP kanali) - once SQL
        sb.Append("<h2>Son Ba&#287;lant&#305; Olaylar&#305;</h2><table><tr><th>Kay&#305;t</th></tr>");
        var cl = Db.Query("SELECT TOP 30 Tarih,Olay FROM ConnLog ORDER BY Id DESC");
        if (cl != null)
        {
            foreach (System.Data.DataRow r in cl.Rows)
                sb.Append("<tr><td>").Append(r[0] == DBNull.Value ? "" : Convert.ToDateTime(r[0]).ToString("yyyy-MM-dd HH:mm:ss"))
                  .Append("  ").Append(H(Convert.ToString(r[1]))).Append("</td></tr>");
            if (cl.Rows.Count == 0) sb.Append("<tr><td class='mut'>Hen&uuml;z ba&#287;lant&#305; kayd&#305; yok.</td></tr>");
        }
        else
        {
            try
            {
                if (File.Exists(connLog))
                    foreach (var line in File.ReadAllLines(connLog).Reverse().Take(30))
                        sb.Append("<tr><td>").Append(H(line)).Append("</td></tr>");
                else sb.Append("<tr><td class='mut'>Hen&uuml;z ba&#287;lant&#305; kayd&#305; yok.</td></tr>");
            }
            catch { }
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    static string PagePeriods(HttpListenerRequest req)
    {
        var sent = LoadSent().Where(s => s.Status == "OK").ToList();
        DateTime from, to;
        string q = req.QueryString["aralik"] ?? "30gun";
        string qf = req.QueryString["from"], qt = req.QueryString["to"];
        if (!string.IsNullOrEmpty(qf) && DateTime.TryParse(qf, out from)) { q = "ozel"; }
        else from = DateTime.MinValue;
        if (!string.IsNullOrEmpty(qt) && DateTime.TryParse(qt, out to)) to = to.Date.AddDays(1).AddSeconds(-1);
        else to = DateTime.Now;
        if (q != "ozel")
        {
            switch (q)
            {
                case "bugun": from = DateTime.Today; break;
                case "hafta": from = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7)); break; // Pazartesi
                case "ay": from = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); break;
                default: from = DateTime.Today.AddDays(-29); q = "30gun"; break;
            }
            to = DateTime.Now;
        }
        var range = sent.Where(s => s.Time >= from && s.Time <= to).ToList();

        var sb = new StringBuilder();
        sb.Append("<div class='tabs'>");
        Tab(sb, "bugun", "Bug&uuml;n", q); Tab(sb, "hafta", "Bu Hafta", q);
        Tab(sb, "ay", "Bu Ay", q); Tab(sb, "30gun", "Son 30 G&uuml;n", q);
        sb.Append("<form method='get' action='/periyot' class='dateform'>")
          .Append("<input type='date' name='from' value='").Append(from == DateTime.MinValue ? "" : from.ToString("yyyy-MM-dd")).Append("'>")
          .Append("<input type='date' name='to' value='").Append(to.ToString("yyyy-MM-dd")).Append("'>")
          .Append("<button type='submit'>Filtrele</button></form>")
          .Append("<a class='exp' style='margin:0 0 0 8px' href='/export/periyot.csv?from=")
          .Append(from == DateTime.MinValue ? "" : from.ToString("yyyy-MM-dd"))
          .Append("&to=").Append(to.ToString("yyyy-MM-dd"))
          .Append("'>&#11015; Excel</a></div>");

        var costsP = LoadCosts();
        sb.Append("<div class='cards'>");
        Card(sb, range.Count.ToString(), "&Ccedil;&#305;kt&#305;");
        Card(sb, range.Sum(s => s.PageN).ToString(), "Sayfa");
        Card(sb, range.Select(s => MachineKey(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(), "Makine");
        Card(sb, range.Select(s => s.User).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(), "Kullan&#305;c&#305;");
        if (costsP.Count > 0) Card(sb, Para(ToplamMaliyet(range, costsP)), "D&ouml;nem Maliyeti");
        sb.Append("</div>");

        // Gunluk grafik
        var byDay = range.GroupBy(s => s.Time.Date).OrderBy(g => g.Key)
                         .Select(g => new { Day = g.Key, N = g.Count(), P = g.Sum(s => s.PageN) }).ToList();
        int max = byDay.Count > 0 ? byDay.Max(x => x.N) : 1;
        sb.Append("<h2>G&uuml;nl&uuml;k Da&#287;&#305;l&#305;m</h2><div class='chart'>");
        foreach (var d in byDay)
        {
            int w = Math.Max(2, (int)(100.0 * d.N / max));
            sb.Append("<div class='crow'><div class='clabel'>").Append(d.Day.ToString("dd.MM ddd", new CultureInfo("tr-TR")))
              .Append("</div><div class='cbarwrap'><div class='cbar' style='width:").Append(w)
              .Append("%'></div></div><div class='cval'>").Append(d.N).Append(" &ccedil;&#305;kt&#305; / ").Append(d.P).Append(" sf</div></div>");
        }
        if (byDay.Count == 0) sb.Append("<div class='mut'>Bu aral&#305;kta kay&#305;t yok.</div>");
        sb.Append("</div>");

        // Saatlik dagilim (mesai yogunlugu)
        var byHour = range.GroupBy(s => s.Time.Hour).OrderBy(g => g.Key)
                          .Select(g => new { H = g.Key, N = g.Count() }).ToList();
        int hmax = byHour.Count > 0 ? byHour.Max(x => x.N) : 1;
        sb.Append("<h2>Saatlik Da&#287;&#305;l&#305;m</h2><div class='chart'>");
        foreach (var h in byHour)
        {
            int w = Math.Max(2, (int)(100.0 * h.N / hmax));
            sb.Append("<div class='crow'><div class='clabel'>").Append(h.H.ToString("00")).Append(":00</div>")
              .Append("<div class='cbarwrap'><div class='cbar' style='width:").Append(w)
              .Append("%'></div></div><div class='cval'>").Append(h.N).Append(" &ccedil;&#305;kt&#305;</div></div>");
        }
        if (byHour.Count == 0) sb.Append("<div class='mut'>Bu aral&#305;kta kay&#305;t yok.</div>");
        sb.Append("</div>");

        sb.Append("<div class='cols'><div>");
        // Donem icinde makine bazli
        bool mP = costsP.Count > 0;
        sb.Append("<h2>D&ouml;nem &#304;&ccedil;i Makine Bazl&#305;</h2><table><tr><th>Makine</th><th>&Ccedil;&#305;kt&#305;</th><th>Sayfa</th>")
          .Append(mP ? "<th>Maliyet</th>" : "").Append("</tr>");
        foreach (var g in range.GroupBy(s => MachineKey(s), StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Count()))
        {
            sb.Append("<tr><td>").Append(H(g.Key)).Append("</td><td>").Append(g.Count()).Append("</td><td>").Append(g.Sum(s => s.PageN)).Append("</td>");
            if (mP) sb.Append("<td>").Append(Para(ToplamMaliyet(g, costsP))).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        sb.Append("</div><div>");
        // Donem icinde kagit turu
        sb.Append("<h2>D&ouml;nem &#304;&ccedil;i Ka&#287;&#305;t T&uuml;r&uuml;</h2><table><tr><th>Ka&#287;&#305;t</th><th>&Ccedil;&#305;kt&#305;</th><th>Sayfa</th></tr>");
        foreach (var g in range.GroupBy(s => s.Paper).OrderByDescending(g => g.Count()))
            sb.Append("<tr><td>").Append(H(g.Key)).Append("</td><td>").Append(g.Count())
              .Append("</td><td>").Append(g.Sum(s => s.PageN)).Append("</td></tr>");
        sb.Append("</table>");
        sb.Append("</div></div>");
        return sb.ToString();
    }

    static string PageJobs(HttpListenerRequest req)
    {
        var sent = LoadSent();
        var printed = LoadPrinted();
        string f = (req.QueryString["q"] ?? "").Trim();
        var list = Enumerable.Reverse(sent);
        if (f.Length > 0)
            list = list.Where(s => (s.User + " " + s.Machine + " " + s.Doc).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);

        var sb = new StringBuilder();
        sb.Append("<a class='exp' href='/export/isler.csv'>&#11015; Excel'e Aktar</a>");
        sb.Append("<form method='get' action='/isler' class='dateform' style='margin-bottom:14px'>")
          .Append("<input type='text' name='q' placeholder='Makine / kullan&#305;c&#305; / belge ara...' value='").Append(H(f))
          .Append("' style='width:280px'><button type='submit'>Ara</button></form>");
        JobTable(sb, list.Take(300), printed);
        return sb.ToString();
    }

    static void JobTable(StringBuilder sb, IEnumerable<Sent> jobs, Dictionary<string, string[]> printed)
    {
        var failed = LoadFailed();
        sb.Append("<table><tr><th>Tarih</th><th>Belge</th><th>Kullan&#305;c&#305;</th><th>RDP Makine</th><th>Sayfa</th><th>Ka&#287;&#305;t</th><th>Boyut</th><th>Lokal PC &#92; Yaz&#305;c&#305;</th><th>Durum</th><th>PDF</th></tr>");
        foreach (var s in jobs)
        {
            bool ok = printed.ContainsKey(s.File);
            bool fail = !ok && failed.ContainsKey(s.File);
            string prn = ok && printed[s.File].Length > 3 ? printed[s.File][3]
                       : (fail && failed[s.File].Length > 3 ? failed[s.File][3] : "");
            string durum = s.Status != "OK"
                ? "<span style='color:#c0392b;font-weight:600'>" + H(s.Status.Replace("ENGEL:", "Engellendi:")) + "</span>"
                : (ok ? "<span class='ok'>Bas&#305;ld&#305; &#10003;</span>"
                : fail ? "<span style='color:#c0392b;font-weight:600' title='" + H(failed[s.File][4]) + "'>"
                         + (failed[s.File][4] == "IPTAL" ? "&#304;ptal edildi" : "Bas&#305;lamad&#305; &#10007;") + "</span>"
                : "<span class='wait'>G&ouml;nderildi</span>");
            sb.Append("<tr><td>").Append(s.Time == DateTime.MinValue ? "" : s.Time.ToString("yyyy-MM-dd HH:mm:ss"))
              .Append("</td><td>").Append(H(s.Doc.Length > 0 ? s.Doc : s.File))
              .Append("</td><td>").Append(H(s.User)).Append("</td><td>").Append(H(s.Machine))
              .Append("</td><td>").Append(H(s.Pages))
              .Append("</td><td>").Append(H(s.Paper))
              .Append("</td><td>").Append(s.KbN > 0 ? (s.KbN >= 1024 ? Math.Round(s.KbN / 1024.0, 1) + " MB" : s.KbN + " KB") : "")
              .Append("</td><td>").Append(H(prn))
              .Append("</td><td>").Append(durum)
              .Append("</td><td>")
              .Append(s.File.Length > 1 && s.File != "-"
                  ? "<a href='/pdf?f=" + WebUtility.UrlEncode(s.File) + "' target='_blank' title='PDF a&ccedil;/indir'>&#128196;</a>" : "")
              .Append("</td></tr>");
        }
        sb.Append("</table>");
    }

    // ---------------- Sablon ----------------

    static string Page(string active, string content)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang='tr'><head><meta charset='utf-8'>");
        if (active != "periyot" && active != "isler")
            sb.Append("<meta http-equiv='refresh' content='15'>");
        sb.Append("<title>Print360 Panel</title><style>");
        // ---- MODERN TASARIM SISTEMI (2026) ----
        // Renk degiskenleri: logoyla uyumlu mavi-indigo; yumusak golgeler,
        // daha genis bosluk, okunakli tipografi. Sinif adlari DEGISMEDI.
        sb.Append(":root{--bg:#f6f8fc;--yzy:#ffffff;--mrk:#4f46e5;--mrk2:#6366f1;--acik:#eef2ff;");
        sb.Append("--metin:#0f172a;--soluk:#64748b;--cizgi:#e2e8f0;--yesil:#059669;--turuncu:#d97706;--kirmizi:#dc2626;");
        sb.Append("--golge:0 1px 2px rgba(15,23,42,.04),0 4px 12px rgba(15,23,42,.06);");
        sb.Append("--golge2:0 2px 4px rgba(15,23,42,.05),0 12px 28px rgba(15,23,42,.09)}");
        sb.Append("*{box-sizing:border-box}");
        sb.Append("body{font-family:'Segoe UI Variable','Segoe UI',system-ui,sans-serif;margin:0;background:var(--bg);");
        sb.Append("color:var(--metin);display:flex;min-height:100vh;-webkit-font-smoothing:antialiased}");
        // --- Kenar menu ---
        sb.Append("nav{width:246px;background:linear-gradient(180deg,#1e1b4b 0%,#312e81 55%,#3730a3 100%);");
        sb.Append("color:#c7d2fe;flex-shrink:0;padding:18px 12px;display:flex;flex-direction:column;gap:2px}");
        sb.Append("nav .brand{font-size:20px;font-weight:700;color:#fff;padding:6px 12px 4px;letter-spacing:-.3px;display:flex;align-items:center;gap:9px}");
        sb.Append("nav .brand small{display:block;font-size:11px;font-weight:400;color:#a5b4fc;letter-spacing:0}");
        sb.Append("nav .sep{height:1px;background:rgba(255,255,255,.12);margin:14px 8px}");
        sb.Append("nav a{display:flex;align-items:center;gap:10px;padding:10px 13px;color:#c7d2fe;text-decoration:none;");
        sb.Append("font-size:13.5px;border-radius:9px;transition:background .15s,color .15s}");
        sb.Append("nav a:hover{background:rgba(255,255,255,.09);color:#fff}");
        sb.Append("nav a.act{background:rgba(255,255,255,.16);color:#fff;font-weight:600;box-shadow:inset 3px 0 0 #a5b4fc}");
        // --- Icerik ---
        sb.Append("main{flex:1;padding:30px 36px;max-width:1280px}");
        sb.Append("h1{font-size:24px;font-weight:700;margin:0 0 22px;letter-spacing:-.5px}");
        sb.Append("h2{font-size:14px;font-weight:600;margin:30px 0 10px;color:var(--soluk);");
        sb.Append("text-transform:uppercase;letter-spacing:.7px}");
        // --- Kartlar ---
        sb.Append(".cards{display:flex;gap:14px;flex-wrap:wrap;margin-bottom:10px}");
        sb.Append(".cols{display:grid;grid-template-columns:1fr 1fr;gap:0 26px;align-items:start}");
        sb.Append("@media(max-width:980px){.cols{grid-template-columns:1fr}nav{width:200px}main{padding:20px}}");
        sb.Append(".card{background:var(--yzy);border:1px solid var(--cizgi);border-radius:14px;padding:16px 22px;");
        sb.Append("box-shadow:var(--golge);min-width:142px;transition:transform .15s,box-shadow .15s}");
        sb.Append(".card:hover{transform:translateY(-2px);box-shadow:var(--golge2)}");
        sb.Append(".card .n{font-size:30px;font-weight:700;color:var(--mrk);letter-spacing:-1px;line-height:1.15}");
        sb.Append(".card .t{font-size:11.5px;color:var(--soluk);margin-top:3px;font-weight:500}");
        // --- Tablolar ---
        sb.Append("table{width:100%;border-collapse:separate;border-spacing:0;background:var(--yzy);");
        sb.Append("border:1px solid var(--cizgi);border-radius:14px;overflow:hidden;box-shadow:var(--golge)}");
        sb.Append("th{background:#f8fafc;text-align:left;padding:11px 14px;font-size:11px;font-weight:600;");
        sb.Append("text-transform:uppercase;letter-spacing:.5px;color:var(--soluk);border-bottom:1px solid var(--cizgi)}");
        sb.Append("td{padding:10px 14px;border-top:1px solid #f1f5f9;font-size:13.5px}");
        sb.Append("tbody tr:hover td,tr:hover td{background:#fafbff}");
        // --- Durum renkleri ---
        sb.Append(".ok{color:var(--yesil);font-weight:600}.wait{color:var(--turuncu);font-weight:600}");
        sb.Append(".mut{color:#94a3b8}");
        // --- Baglanti rozeti: yazici AKTIF mi (ajan canli + yazici hazir) ---
        sb.Append(".rz{display:inline-flex;align-items:center;gap:6px;padding:3px 10px 3px 8px;border-radius:999px;");
        sb.Append("font-size:12px;font-weight:600;white-space:nowrap;border:1px solid transparent}");
        sb.Append(".rz i{width:8px;height:8px;border-radius:50%;display:inline-block;flex:none}");
        sb.Append(".rz-ak{background:#dcfce7;color:#15803d;border-color:#86efac}");
        sb.Append(".rz-ak i{background:#16a34a;box-shadow:0 0 0 3px rgba(22,163,74,.18);animation:nb 2s ease-in-out infinite}");
        sb.Append(".rz-uy{background:#fef3c7;color:#b45309;border-color:#fcd34d}.rz-uy i{background:#f59e0b}");
        sb.Append(".rz-kp{background:#fee2e2;color:#b91c1c;border-color:#fca5a5}.rz-kp i{background:#dc2626}");
        sb.Append(".rz-ps{background:#f1f5f9;color:#64748b;border-color:#e2e8f0}.rz-ps i{background:#94a3b8}");
        sb.Append("@keyframes nb{0%,100%{opacity:1}50%{opacity:.35}}");
        // --- Bilgi / uyari kutulari ---
        sb.Append(".info{background:var(--acik);border:1px solid #c7d2fe;border-left:4px solid var(--mrk);");
        sb.Append("border-radius:11px;padding:13px 16px;font-size:13.5px;margin-bottom:16px;line-height:1.55}");
        sb.Append(".warn{background:#fffbeb;border:1px solid #fde68a;border-left:4px solid var(--turuncu);");
        sb.Append("border-radius:11px;padding:13px 16px;font-size:13.5px;margin-bottom:16px;line-height:1.55}");
        // --- Sekmeler / formlar ---
        sb.Append(".tabs{display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-bottom:18px}");
        sb.Append(".tabs a{background:var(--yzy);border:1px solid var(--cizgi);border-radius:999px;padding:7px 17px;");
        sb.Append("font-size:13px;color:#475569;text-decoration:none;transition:all .15s}");
        sb.Append(".tabs a:hover{border-color:var(--mrk);color:var(--mrk)}");
        sb.Append(".tabs a.act{background:var(--mrk);color:#fff;border-color:var(--mrk);box-shadow:0 2px 8px rgba(79,70,229,.3)}");
        sb.Append(".dateform{display:flex;gap:7px;margin-left:auto}");
        sb.Append("input,button,select{font-family:inherit}");
        sb.Append(".dateform input,.dateform button{padding:7px 12px;border:1px solid var(--cizgi);border-radius:9px;font-size:13px}");
        sb.Append(".dateform button,button[type=submit]{background:var(--mrk);color:#fff;border:0;cursor:pointer;");
        sb.Append("border-radius:9px;padding:9px 18px;font-size:13px;font-weight:600;transition:background .15s}");
        sb.Append(".dateform button:hover,button[type=submit]:hover{background:#4338ca}");
        sb.Append("input[type=text],input[type=password],input:not([type]){padding:8px 12px;border:1px solid var(--cizgi);");
        sb.Append("border-radius:9px;font-size:13px;outline:none;transition:border-color .15s,box-shadow .15s}");
        sb.Append("input:focus{border-color:var(--mrk);box-shadow:0 0 0 3px rgba(79,70,229,.13)}");
        sb.Append(".exp{display:inline-block;background:var(--yesil);color:#fff !important;border-radius:9px;");
        sb.Append("padding:8px 18px;font-size:13px;font-weight:600;text-decoration:none;margin:0 0 16px 0;");
        sb.Append("box-shadow:0 2px 8px rgba(5,150,105,.25);transition:background .15s}");
        sb.Append(".exp:hover{background:#047857}");
        // --- Grafik ---
        sb.Append(".chart{background:var(--yzy);border:1px solid var(--cizgi);border-radius:14px;padding:18px 20px;box-shadow:var(--golge)}");
        sb.Append(".crow{display:flex;align-items:center;gap:12px;margin:6px 0}");
        sb.Append(".clabel{width:92px;font-size:12px;color:var(--soluk);text-align:right;font-weight:500}");
        sb.Append(".cbarwrap{flex:1;background:#f1f5f9;border-radius:999px;overflow:hidden}");
        sb.Append(".cbar{height:15px;background:linear-gradient(90deg,var(--mrk),#818cf8);border-radius:999px;min-width:2px}");
        sb.Append(".cval{width:132px;font-size:12px;color:#475569}");
        sb.Append("pre{border:1px solid var(--cizgi);border-radius:11px}");
        sb.Append("code{background:#f1f5f9;padding:2px 6px;border-radius:5px;font-size:12.5px}");
        sb.Append("a{color:var(--mrk)}");
        sb.Append("footer{font-size:11.5px;color:#94a3b8;margin-top:30px;padding-top:18px;border-top:1px solid var(--cizgi);line-height:1.7}");
        sb.Append("</style></head><body><nav><div class='brand'>&#128424; <span>Print360")
          .Append("<small>Yazd&#305;rma Y&ouml;netimi</small></span></div><div class='sep'></div>");
        NavLink(sb, "/", "genel", active, "&#128200; Genel Bak&#305;&#351;");
        NavLink(sb, "/makineler", "makineler", active, "&#128421; Makineler (AD)");
        NavLink(sb, "/yazicilar", "yazicilar", active, "&#128424; Yaz&#305;c&#305;lar");
        NavLink(sb, "/kullanicilar", "kullanicilar", active, "&#128101; Kullan&#305;c&#305;lar");
        NavLink(sb, "/periyot", "periyot", active, "&#128197; Periyotlar");
        NavLink(sb, "/isler", "isler", active, "&#128196; &#304;&#351;ler");
        NavLink(sb, "/yetki", "yetki", active, "&#128683; Yetkiler");
        NavLink(sb, "/tani", "tani", active, "&#129513; Tan&#305; (sorun giderme)");
        string vtRozet = Db.Motor == Db.DbMotor.MsSql ? ""
            : " <span style='background:" + (Db.Motor == Db.DbMotor.Sqlite ? "#0d9488" : "#d97706")
              + ";color:#fff;border-radius:10px;padding:1px 7px;font-size:10px'>"
              + (Db.Motor == Db.DbMotor.Sqlite ? "SQLite" : "CSV") + "</span>";
        NavLink(sb, "/veritabani", "veritabani", active, "&#128451; Veritaban&#305;" + vtRozet);
        object okn = Db.Scalar("SELECT COUNT(*) FROM Alerts WHERE Okundu=0");
        if (okn == null) okn = Db.OkunmamisUyari();   // SQL yoksa dosyadan
        string rozet = okn != null && Convert.ToInt32(okn) > 0
            ? " <span style='background:#e05555;color:#fff;border-radius:10px;padding:1px 8px;font-size:11px'>" + Convert.ToInt32(okn) + "</span>" : "";
        NavLink(sb, "/uyarilar", "uyarilar", active, "&#128276; Uyar&#305;lar" + rozet);
        if (AuthAktif())
            sb.Append("<a href='/cikis' style='margin-top:18px;opacity:.75'>&#128274; &Ccedil;&#305;k&#305;&#351;</a>");
        sb.Append("</nav><main><h1>")
          .Append(active == "makineler" ? "Makineler &mdash; Active Directory Takibi"
                : active == "yazicilar" ? "Yaz&#305;c&#305; Sa&#287;l&#305;&#287;&#305;"
                : active == "kullanicilar" ? "Kullan&#305;c&#305;lar Raporu"
                : active == "periyot" ? "Yazd&#305;rma Periyotlar&#305;"
                : active == "isler" ? "T&uuml;m &#304;&#351;ler"
                : active == "yetki" ? "Yetkiler &mdash; Engelleme ve Kotalar"
                : active == "tani" ? "Tan&#305; &mdash; Yazd&#305;rma Sorun Giderme"
                : active == "veritabani" ? "Veritaban&#305; &mdash; MSSQL Ayarlar&#305;"
                : active == "uyarilar" ? "Uyar&#305;lar" : "Genel Bak&#305;&#351;")
          .Append("</h1>");
        sb.Append(content);
        sb.Append("<footer>Print360 v").Append(Surum.Etiket).Append(" &bull; ")
          .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("<br>")
          .Append("Geli&#351;tirici: <b>&Ouml;mer &Ccedil;ARNA&Ccedil;AR</b> &bull; ")
          .Append("<a href='mailto:omer.carnacar@outlook.com.tr?subject=Print360%20v").Append(Surum.V)
          .Append("' style='color:#3f77c9;text-decoration:underline' title='E-posta g&ouml;nder'>")
          .Append("omer.carnacar@outlook.com.tr</a> &bull; ")
          .Append("<a href='https://www.linkedin.com/in/omercarnacar/' target='_blank' rel='noopener' ")
          .Append("style='color:#3f77c9;text-decoration:underline' title='LinkedIn profili'>LinkedIn</a><br>")
          .Append("&Uuml;cretsiz s&uuml;r&uuml;m &mdash; s&#305;n&#305;rs&#305;z kullan&#305;m, <b>para ile sat&#305;lamaz</b>.")
          .Append("</footer></main></body></html>");
        return sb.ToString();
    }

    static void NavLink(StringBuilder sb, string href, string key, string active, string label)
    {
        sb.Append("<a href='").Append(href).Append("'").Append(key == active ? " class='act'" : "").Append(">").Append(label).Append("</a>");
    }

    static void Tab(StringBuilder sb, string key, string label, string active)
    {
        sb.Append("<a href='/periyot?aralik=").Append(key).Append("'").Append(key == active ? " class='act'" : "").Append(">").Append(label).Append("</a>");
    }

    static void Card(StringBuilder sb, string n, string t)
    {
        sb.Append("<div class='card'><div class='n'>").Append(n).Append("</div><div class='t'>").Append(t).Append("</div></div>");
    }

    static string MachineKey(Sent s)
    {
        return s.Machine.Length > 0 ? s.Machine : ("(" + s.User + ")");
    }

    static string H(string s)
    {
        return WebUtility.HtmlEncode(s ?? "");
    }

    // Arsivdeki isin PDF'ini ac/indir (sunucu ajani her isi C:\Print360\archive'a gzipli kaydeder)
    static string ServePdf(HttpListenerContext ctx)
    {
        string f = Q(ctx, "f") ?? "";
        foreach (char c in Path.GetInvalidFileNameChars()) f = f.Replace(c.ToString(), "");
        string yol = null;
        string root = @"C:\Print360\archive";
        if (f.Length > 0 && Directory.Exists(root))
            foreach (var d in Directory.GetDirectories(root))
            {
                var p = Path.Combine(d, f + ".gz");
                if (File.Exists(p)) { yol = p; break; }
            }
        if (yol == null)
        {
            byte[] msg = Encoding.UTF8.GetBytes("PDF arsivde bulunamadi (90 gunden eski isler otomatik silinir).");
            ctx.Response.StatusCode = 404;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.ContentLength64 = msg.Length;
            ctx.Response.OutputStream.Write(msg, 0, msg.Length);
            ctx.Response.Close();
            return null;
        }
        using (var ms = new MemoryStream())
        {
            using (var fs = File.OpenRead(yol))
            using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
                gz.CopyTo(ms);
            byte[] veri = ms.ToArray();
            ctx.Response.ContentType = "application/pdf";
            ctx.Response.AddHeader("Content-Disposition", "inline; filename=" + f);
            ctx.Response.ContentLength64 = veri.Length;
            ctx.Response.OutputStream.Write(veri, 0, veri.Length);
            ctx.Response.Close();
        }
        return null;
    }

    // ---------------- Excel'e aktar (CSV; Turkce Excel icin ';' ayirici + UTF-8 BOM) ----------------

    static string CsvAlan(string s)
    {
        s = s ?? "";
        if (s.Contains(";") || s.Contains("\"") || s.Contains("\n"))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    static string CsvOut(HttpListenerContext ctx, string dosyaAdi, StringBuilder icerik)
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        byte[] veri = Encoding.UTF8.GetBytes(icerik.ToString());
        ctx.Response.ContentType = "text/csv; charset=utf-8";
        ctx.Response.AddHeader("Content-Disposition", "attachment; filename=" + dosyaAdi);
        ctx.Response.ContentLength64 = bom.Length + veri.Length;
        ctx.Response.OutputStream.Write(bom, 0, bom.Length);
        ctx.Response.OutputStream.Write(veri, 0, veri.Length);
        ctx.Response.Close();
        return null;
    }

    static string CsvJobs(HttpListenerContext ctx)
    {
        var sent = LoadSent();
        var printed = LoadPrinted();
        var costs = LoadCosts();
        var sb = new StringBuilder("Tarih;Kullanici;Makine;Belge;Sayfa;Kagit;BoyutKB;Yazici;Durum;MaliyetTL\r\n");
        foreach (var s in sent)
        {
            string prn = printed.ContainsKey(s.File) && printed[s.File].Length > 3 ? printed[s.File][3] : "";
            string durum = s.Status != "OK" ? s.Status : (printed.ContainsKey(s.File) ? "Basildi" : "Gonderildi");
            sb.Append(s.Time == DateTime.MinValue ? "" : s.Time.ToString("yyyy-MM-dd HH:mm:ss")).Append(';')
              .Append(CsvAlan(s.User)).Append(';').Append(CsvAlan(s.Machine)).Append(';')
              .Append(CsvAlan(s.Doc)).Append(';').Append(s.PageN).Append(';')
              .Append(CsvAlan(s.Paper)).Append(';').Append(s.KbN).Append(';')
              .Append(CsvAlan(prn)).Append(';').Append(CsvAlan(durum)).Append(';')
              .Append((s.Status == "OK" ? IsMaliyet(s, costs) : 0).ToString("0.##", CultureInfo.InvariantCulture).Replace(".", ","))
              .Append("\r\n");
        }
        return CsvOut(ctx, "print360-isler.csv", sb);
    }

    static string CsvMachines(HttpListenerContext ctx)
    {
        var sent = LoadSent().Where(s => s.Status == "OK").ToList();
        var printed = LoadPrinted();
        var hb = LoadHb();
        var sb = new StringBuilder("Makine;Cikti;Basilan;Sayfa;SonYazdirma;IstemciKullanicisi;Baglanti\r\n");
        foreach (var g in sent.GroupBy(s => MachineKey(s), StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
        {
            bool onl; HbDurum(hb, g.Key, out onl);
            string ku = hb.ContainsKey(g.Key) && hb[g.Key].Length > 4 ? hb[g.Key][4] : "";
            sb.Append(CsvAlan(g.Key)).Append(';').Append(g.Count()).Append(';')
              .Append(g.Count(s => printed.ContainsKey(s.File))).Append(';')
              .Append(g.Sum(s => s.PageN)).Append(';')
              .Append(g.Max(s => s.Time).ToString("yyyy-MM-dd HH:mm")).Append(';')
              .Append(CsvAlan(ku)).Append(';')
              .Append(onl ? "Cevrimici" : "Cevrimdisi").Append("\r\n");
        }
        return CsvOut(ctx, "print360-makineler.csv", sb);
    }

    static string CsvUsers(HttpListenerContext ctx)
    {
        var all = LoadSent();
        var adUsers = LoadAdUsers();
        var sb = new StringBuilder("Kullanici;ADKaydi;AdSoyad;Hesap;Cikti;Sayfa;VeriMB;Engellenen;SonYazdirma\r\n");
        var stats = all.Where(s => s.Status == "OK").GroupBy(s => s.User, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var engel = all.Where(s => s.Status != "OK").GroupBy(s => s.User, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var n in adUsers.Select(u => u.Sam).Union(stats.Keys, StringComparer.OrdinalIgnoreCase)
                                 .Where(x => x.Length > 0).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var au = adUsers.FirstOrDefault(u => string.Equals(u.Sam, n, StringComparison.OrdinalIgnoreCase));
            List<Sent> st; stats.TryGetValue(n, out st);
            int e; engel.TryGetValue(n, out e);
            sb.Append(CsvAlan(n)).Append(';').Append(au != null ? "Evet" : "Hayir").Append(';')
              .Append(CsvAlan(au != null ? au.Ad : "")).Append(';')
              .Append(au == null ? "" : (au.Aktif ? "Aktif" : "DevreDisi")).Append(';')
              .Append(st != null ? st.Count : 0).Append(';')
              .Append(st != null ? st.Sum(s => s.PageN) : 0).Append(';')
              .Append(st != null ? Math.Round(st.Sum(s => (double)s.KbN) / 1024.0, 1).ToString(CultureInfo.InvariantCulture) : "0").Append(';')
              .Append(e).Append(';')
              .Append(st != null ? st.Max(s => s.Time).ToString("yyyy-MM-dd HH:mm") : "").Append("\r\n");
        }
        return CsvOut(ctx, "print360-kullanicilar.csv", sb);
    }

    // Kisi bazli maliyet dokumu: kullanici x kagit turu (sayfa + TL)
    static string CsvUserCosts(HttpListenerContext ctx)
    {
        var sent = LoadSent().Where(s => s.Status == "OK").ToList();
        var costs = LoadCosts();
        var kagitlar = sent.GroupBy(s => s.Paper).OrderByDescending(g => g.Sum(x => x.PageN))
                           .Select(g => g.Key).ToList();
        var sb = new StringBuilder("Kullanici");
        foreach (var k in kagitlar) sb.Append(';').Append(CsvAlan(k + " Sayfa")).Append(';').Append(CsvAlan(k + " TL"));
        sb.Append(";ToplamSayfa;ToplamTL\r\n");
        foreach (var u in sent.GroupBy(s => s.User, StringComparer.OrdinalIgnoreCase)
                              .OrderByDescending(g => ToplamMaliyet(g, costs)))
        {
            sb.Append(CsvAlan(u.Key));
            foreach (var k in kagitlar)
            {
                var alt = u.Where(s => s.Paper.Equals(k, StringComparison.OrdinalIgnoreCase)).ToList();
                sb.Append(';').Append(alt.Sum(s => s.PageN)).Append(';')
                  .Append(ToplamMaliyet(alt, costs).ToString("0.##", CultureInfo.InvariantCulture).Replace(".", ","));
            }
            sb.Append(';').Append(u.Sum(s => s.PageN)).Append(';')
              .Append(ToplamMaliyet(u, costs).ToString("0.##", CultureInfo.InvariantCulture).Replace(".", ",")).Append("\r\n");
        }
        return CsvOut(ctx, "print360-kisi-maliyet.csv", sb);
    }

    static string CsvPeriod(HttpListenerContext ctx)
    {
        var sent = LoadSent().Where(s => s.Status == "OK").ToList();
        DateTime from = DateTime.Today.AddDays(-29), to = DateTime.Now;
        DateTime tmp;
        if (DateTime.TryParse(Q(ctx, "from"), out tmp)) from = tmp;
        if (DateTime.TryParse(Q(ctx, "to"), out tmp)) to = tmp.Date.AddDays(1).AddSeconds(-1);
        var range = sent.Where(s => s.Time >= from && s.Time <= to).ToList();
        var sb = new StringBuilder("Gun;Cikti;Sayfa;Makine;Kullanici\r\n");
        foreach (var g in range.GroupBy(s => s.Time.Date).OrderBy(g => g.Key))
            sb.Append(g.Key.ToString("yyyy-MM-dd")).Append(';').Append(g.Count()).Append(';')
              .Append(g.Sum(s => s.PageN)).Append(';')
              .Append(g.Select(s => MachineKey(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count()).Append(';')
              .Append(g.Select(s => s.User).Distinct(StringComparer.OrdinalIgnoreCase).Count()).Append("\r\n");
        return CsvOut(ctx, "print360-periyot.csv", sb);
    }

    static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var cur = new StringBuilder();
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

    // Basit CSV okuyucu (tirnakli alan destekli)
    static List<string[]> ReadCsv(string path)
    {
        var rows = new List<string[]>();
        if (!File.Exists(path)) return rows;
        string text;
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
                text = sr.ReadToEnd();
        }
        catch { return rows; }
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = new List<string>();
            var cur = new StringBuilder();
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
            rows.Add(fields.ToArray());
        }
        return rows;
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
