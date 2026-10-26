// ============================================================
//  UZMAN TESTI - GERCEK SUNUCU + GERCEK ISTEMCI + ARIZA ENJEKSIYONU
// ============================================================
// Diger testler parcalari tek tek sinar. Bu test URETIMDEKI panel sunucusu
// kodunu (Dashboard.IstegiIsle) yerel bir portta, URETIMDEKI istemci kodunu
// (ClientAgent.FetchOneJob) ona bagli calistirir. Araya bir TCP vekili girer
// ve gercek ag arizalari uretir:
//   - indirmenin ORTASINDA baglanti kopmasi
//   - onayin (ACK) yolda kaybolmasi
//   - sunucunun kapali olmasi (baglanti reddi)
//   - guvenlik duvarinin paketi SESSIZCE dusurmesi (yanitsiz adres)
//   - yanlis istemci sifresi
//   - ayni makinede iki ajanin ayni anda yoklamasi
//   - istemcinin isi diske yazamamasi (zehirli is -> karantina)
//   - buyuk dosya (20 MB), Turkce ve ozel karakterli adlar
// Her senaryoda hem SONUC (dosya, kuyruk) hem GUNLUK (ne yazildi) denetlenir.
// Hicbir sey C:\Print360'a dokunmaz: tum yollar gecici klasore yonlendirilir.
//
//   derleme: tests\calistir.ps1 (UzmanTest girdisi)
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

partial class UzmanTest
{
    static int gecti, toplam;
    static void Kontrol(bool ok, string ad, string ayrinti = null)
    {
        toplam++; if (ok) gecti++;
        Console.WriteLine((ok ? "GECTI " : "KALDI ") + ad);
        if (!ok && ayrinti != null) Console.WriteLine("       -> " + ayrinti);
    }

    static string kok, sKuyruk, iJobs, iDone, iLog, sLog, iIni;
    static string makine = Environment.MachineName;
    static int sunucuPort;
    static Vekil vekil;
    static MethodInfo fetch, yoklanmali;
    const BindingFlags OZEL = BindingFlags.NonPublic | BindingFlags.Static;

    static int Main()
    {
        kok = Path.Combine(Path.GetTempPath(), "p360-uzman-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        string uretimOnce = UretimDamgasi();
        try
        {
            Hazirla();
            Normal();
            BuyukDosya();
            IndirmeOrtasindaKopma();
            OnayYoldaKaybolur();
            IkiAjanAyniAnda();
            YanlisSifre();
            SunucuKapali();
            GuvenlikDuvariSessiz();
            ZehirliIsKarantina();
            BasildiBildirimi();
            BildirimBekletme();
            KalpAtisiVeAdDegisimi();
            CiftOnay();
            IkiSunucuGunlukSismesi();
            YanlisSifreVeVekil();
            RdpOturumDegisimi();
            Gunlukler();
        }
        catch (Exception ex) { Kontrol(false, "BEKLENMEYEN ISTISNA", ex.ToString()); }
        finally
        {
            try { if (vekil != null) vekil.Durdur(); } catch { }
            try { Directory.Delete(kok, true); } catch { }
        }
        UretimeDokunulmadi(uretimOnce);
        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        return gecti == toplam ? 0 : 1;
    }

    // ------------------------------------------------------------------ ortam
    // Test, bu makinedeki GERCEK Print360 verisine dokunmamali. Bilinen veri
    // dosyalarinin varlik + son yazma zamani, test oncesi ve sonrasi karsilastirilir.
    static readonly string[] UretimDosyalari = {
        @"C:\Print360\stats\kimlikler.csv", @"C:\Print360\stats\heartbeat.csv", @"C:\Print360\stats\alerts.csv",
        @"C:\Print360\stats\jobs.csv", @"C:\Print360\stats\printers.csv", @"C:\Print360\print360.db",
        @"C:\Print360\logs\dashboard.log", @"C:\Print360\logs\connections.log", @"C:\Print360\logs\mail-last.txt" };

    static string UretimDamgasi()
    {
        var sb = new StringBuilder();
        foreach (var f in UretimDosyalari)
            sb.Append(f).Append('=').Append(File.Exists(f) ? File.GetLastWriteTimeUtc(f).Ticks.ToString() : "-").Append('|');
        return sb.ToString();
    }

    static void UretimeDokunulmadi(string once)
    {
        string sonra = UretimDamgasi();
        var degisen = new List<string>();
        var a = once.Split('|'); var b = sonra.Split('|');
        for (int i = 0; i < a.Length && i < b.Length; i++) if (a[i] != b[i]) degisen.Add(b[i].Split('=')[0]);
        Kontrol(degisen.Count == 0, @"test bu makinedeki gercek C:\Print360 verisine DOKUNMADI", string.Join(", ", degisen));
    }

    static void Ayarla(Type t, string alan, object deger)
    {
        var f = t.GetField(alan, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (f == null) throw new Exception(t.Name + "." + alan + " bulunamadi");
        f.SetValue(null, deger);
    }

    static void Hazirla()
    {
        string s = Path.Combine(kok, "sunucu"), i = Path.Combine(kok, "istemci");
        foreach (var d in new[] { "queue", "logs", "stats\\clients" }) Directory.CreateDirectory(Path.Combine(s, d));
        foreach (var d in new[] { "jobs", "done", "failed", "logs", "stats", "vc-outbox" }) Directory.CreateDirectory(Path.Combine(i, d));
        sKuyruk = Path.Combine(s, "queue"); sLog = Path.Combine(s, "logs\\dashboard.log");
        iJobs = Path.Combine(i, "jobs"); iDone = Path.Combine(i, "done"); iLog = Path.Combine(i, "logs\\client.log");
        iIni = Path.Combine(i, "Print360.ini");

        // --- SUNUCU: tum yollar gecici klasore
        Kuyruk.Kok = sKuyruk;
        Kuyruk.SuresiDolanKok = Path.Combine(s, "queue-suresi-dolan");
        Kuyruk.AnlikSureMs = 0;
        Db.DbIniYolu = Path.Combine(s, "db.ini");            // yok: MSSQL denenmez (sa duzeltmesi)
        Db.SqliteDosya = Path.Combine(s, "print360.db");
        Db.UyariCsv = Path.Combine(s, "stats\\alerts.csv");
        Ortak.HataAyiklamaDosyasi = Path.Combine(kok, "hata-ayiklama.txt");
        Ortak.HataAyiklamaAyarla(true, TimeSpan.FromHours(1));   // ayrintili gunluk yollari da calissin
        var D = typeof(Dashboard);
        Ayarla(D, "logFile", sLog);
        Ayarla(D, "hbFile", Path.Combine(s, "stats\\heartbeat.csv"));
        Ayarla(D, "clientsDir", Path.Combine(s, "stats\\clients"));
        Ayarla(D, "jobsCsv", Path.Combine(s, "stats\\jobs.csv"));
        Ayarla(D, "connLog", Path.Combine(s, "logs\\connections.log"));
        Ayarla(D, "pwdFile", Path.Combine(s, "panel.pwd"));
        Ayarla(D, "rulesCsv", Path.Combine(s, "rules.csv"));
        Ayarla(D, "mailIni", Path.Combine(s, "mail.ini"));
        Ayarla(D, "mailLastFile", Path.Combine(s, "logs\\mail-last.txt"));
        // Kimlik defteri ILK kalp atisindan once yonlendirilmeli: eskiden yalnizca
        // senaryo 13'te yonlendiriliyordu ve onceki kalp atislari (performans testinin
        // 300 makinesi dahil) GERCEK C:\Print360\stats\kimlikler.csv'ye yaziliyordu.
        Ayarla(D, "kimlikDefteri", new KimlikDefteri(Path.Combine(s, "stats\\kimlikler.csv")));
        Kontrol(Db.EnsureSchema() && Db.MotorAdi.StartsWith("SQLite"),
                "sunucu veritabani: db.ini yokken MSSQL'e 'sa' ile DENENMEDEN dogrudan SQLite", Db.MotorAdi + " | " + Db.Err);

        // Gercek panel istek isleyicisi, yerel portta
        var isle = D.GetMethod("IstegiIsleGunluklu", OZEL);
        var dinleyici = new HttpListener();
        for (int dene = 0; ; dene++)
        {
            sunucuPort = 18500 + new Random().Next(0, 900);
            dinleyici.Prefixes.Clear();
            dinleyici.Prefixes.Add("http://localhost:" + sunucuPort + "/");
            try { dinleyici.Start(); break; } catch { if (dene > 20) throw; dinleyici = new HttpListener(); }
        }
        new Thread(delegate()
        {
            while (true)
            {
                HttpListenerContext c;
                try { c = dinleyici.GetContext(); } catch { return; }
                ThreadPool.QueueUserWorkItem(delegate(object o) { try { isle.Invoke(null, new[] { o }); } catch { } }, c);
            }
        }) { IsBackground = true }.Start();
        vekil = new Vekil(sunucuPort);

        // --- ISTEMCI: tum yollar gecici klasore, sunucu = vekil
        var C = typeof(ClientAgent);
        Ayarla(C, "baseDir", i); Ayarla(C, "jobsDir", iJobs); Ayarla(C, "doneDir", iDone);
        Ayarla(C, "failedDir", Path.Combine(i, "failed")); Ayarla(C, "logFile", iLog);
        Ayarla(C, "iniFile", iIni); Ayarla(C, "vcOutbox", Path.Combine(i, "vc-outbox"));
        IniYaz("testkey", "localhost:" + vekil.Port);
        fetch = C.GetMethod("FetchOneJob", OZEL);
        yoklanmali = C.GetMethod("YoklanmaliMi", OZEL);
        C.GetMethod("TlsHazirla", OZEL).Invoke(null, null);
    }

    static void IniYaz(string anahtar, string sunucu)
    {
        File.WriteAllText(iIni, "Server=" + sunucu + "\r\nUseHttps=0\r\nClientKey=" + anahtar + "\r\n");
    }

    static string Url { get { return "http://localhost:" + vekil.Port; } }

    static bool Al(string url = null, string anahtar = "testkey")
    {
        try { return (bool)fetch.Invoke(null, new object[] { url ?? Url, anahtar }); }
        catch (TargetInvocationException) { return false; }   // uretimde JobPollLoop bunu yakalar
    }

    static byte[] Pdf(int boyut, int tohum)
    {
        var r = new Random(tohum);
        var b = new byte[boyut];
        r.NextBytes(b);                                   // sikistirilamaz: aktarim gercekten buyuk olsun
        var bas = Encoding.ASCII.GetBytes("%PDF-1.7\n");
        var son = Encoding.ASCII.GetBytes("\n%%EOF\n");
        Buffer.BlockCopy(bas, 0, b, 0, bas.Length);
        Buffer.BlockCopy(son, 0, b, b.Length - son.Length, son.Length);
        return b;
    }

    static void KuyrugaKoy(string ad, byte[] icerik, string mak = null)
    {
        string d = Path.Combine(sKuyruk, Kuyruk.Sanitize(mak ?? makine));
        Directory.CreateDirectory(d);
        string yol = Path.Combine(d, ad + ".gz");
        using (var fs = File.Create(yol))
        using (var gz = new GZipStream(fs, CompressionMode.Compress)) gz.Write(icerik, 0, icerik.Length);
        Thread.Sleep(15);   // olusturma zamanlari sirali olsun (kuyruk en eskiden verir)
    }

    static int KuyrukSayisi() { string d = Path.Combine(sKuyruk, Kuyruk.Sanitize(makine)); return Directory.Exists(d) ? Directory.GetFiles(d, "*.gz").Length : 0; }
    static string[] Jobs() { return Directory.GetFiles(iJobs).Select(Path.GetFileName).ToArray(); }
    static void JobsTemizle() { foreach (var f in Directory.GetFiles(iJobs)) File.Delete(f); }
    static string IstemciGunlugu() { try { using (var fs = new FileStream(iLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var r = new StreamReader(fs)) return r.ReadToEnd(); } catch { return ""; } }
    static string SunucuGunlugu() { try { using (var fs = new FileStream(sLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var r = new StreamReader(fs)) return r.ReadToEnd(); } catch { return ""; } }

    // ------------------------------------------------------------------ senaryolar
    static void Normal()
    {
        Console.WriteLine("--- 1. Normal teslim (Turkce ve ozel karakterli adlar)");
        var adlar = new[] { "20261012_090000_001_ayse~Şubat Faturası İçin Özet.pdf", "20261012_090001_002_ali~fiyat %50 & kâr = #1.pdf",
                            "20261012_090002_003_FS.pdf" };
        var icerik = new Dictionary<string, byte[]>();
        int t = 1;
        foreach (var a in adlar) { icerik[a] = Pdf(50 * 1024, t++); KuyrugaKoy(a, icerik[a]); }
        int n = 0;
        while (n < 10 && Al()) n++;
        bool hepsi = true;
        foreach (var a in adlar)
        {
            string yerel = a.Replace('?', '_');   // istemci gecersiz dosya adi karakterini degistirir
            string y = Path.Combine(iJobs, yerel);
            if (!File.Exists(y) || !File.ReadAllBytes(y).SequenceEqual(icerik[a])) { hepsi = false; Console.WriteLine("       eksik/bozuk: " + yerel); }
        }
        Kontrol(n == 3 && hepsi, "3 is indirildi, icerikler BAYT BAYT ayni (Turkce, %, &, =, # iceren adlar)", string.Join(" | ", Jobs()));
        Kontrol(KuyrukSayisi() == 0, "sunucu kuyrugu bosaldi (onaylar eslesti)", KuyrukSayisi() + " is kaldi");
        Kontrol(!Al(), "kuyruk bosken istemci 'is yok' aliyor", null);
        JobsTemizle();
    }

    static void BuyukDosya()
    {
        Console.WriteLine("--- 2. Buyuk dosya (20 MB, sikistirilamaz)");
        var b = Pdf(20 * 1024 * 1024, 42);
        KuyrugaKoy("20261012_091000_001_FS~Buyuk Rapor.pdf", b);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool ok = Al();
        string y = Path.Combine(iJobs, "20261012_091000_001_FS~Buyuk Rapor.pdf");
        Kontrol(ok && File.Exists(y) && File.ReadAllBytes(y).SequenceEqual(b) && KuyrukSayisi() == 0,
                "20 MB is butun geldi ve onaylandi (" + sw.ElapsedMilliseconds + " ms)", null);
        JobsTemizle();
    }

    static void IndirmeOrtasindaKopma()
    {
        Console.WriteLine("--- 3. Indirmenin ORTASINDA baglanti kopuyor");
        var b = Pdf(3 * 1024 * 1024, 7);
        string ad = "20261012_092000_001_FS~Kopan Indirme.pdf";
        KuyrugaKoy(ad, b);
        vekil.Mod = "indirmeyi-kes"; vekil.KesBayt = 256 * 1024;
        bool ok = Al();
        vekil.Mod = "normal";
        Kontrol(!ok && !File.Exists(Path.Combine(iJobs, ad)), "yarim dosya yaziciya gidecek yere (jobs) BIRAKILMADI", string.Join(" | ", Jobs()));
        Kontrol(Directory.GetFiles(iJobs, "*.tmp").Length == 0, "yarim gecici dosya (.tmp) temizlendi", string.Join(" | ", Jobs()));
        Kontrol(KuyrukSayisi() == 1, "is sunucuda kuyrukta KALDI (onay gitmedi)", KuyrukSayisi().ToString());
        string g = IstemciGunlugu();
        Kontrol(g.Contains("IS INDIRILEMEDI") && g.Contains("AKTARIM YARIDA KESILDI") && g.Contains("yeniden indirilecek"),
                "gunlukte: 'IS INDIRILEMEDI ... AKTARIM YARIDA KESILDI ... yeniden indirilecek' (eskiden HIC iz kalmiyordu)",
                g.Split('\n').LastOrDefault(l => l.Contains("INDIR")));
        ok = Al();
        string y = Path.Combine(iJobs, ad);
        Kontrol(ok && File.Exists(y) && File.ReadAllBytes(y).SequenceEqual(b) && KuyrukSayisi() == 0,
                "baglanti duzelince is BUTUN olarak geldi ve kuyruktan dustu", null);
        JobsTemizle();
    }

    static void OnayYoldaKaybolur()
    {
        Console.WriteLine("--- 4. Onay (ACK) sunucuya giderken kayboluyor");
        var b = Pdf(80 * 1024, 9);
        string ad = "20261012_093000_001_FS~Onayi Kaybolan.pdf";
        KuyrugaKoy(ad, b);
        vekil.Mod = "onayi-dusur";
        Al();
        vekil.Mod = "normal";
        Kontrol(File.Exists(Path.Combine(iJobs, ad)) && KuyrukSayisi() == 1, "is indirildi ama onay gitmedi: is sunucuda duruyor", KuyrukSayisi().ToString());
        string g = IstemciGunlugu();
        Kontrol(g.Contains("ONAY GONDERILEMEDI (baglanti)") && !g.Contains("ONAY REDDEDILDI"),
                "gunlukte 'ONAY GONDERILEMEDI (baglanti)' - 'REDDEDILDI' denmiyor (sunucu reddetmedi, yanit gelmedi)",
                g.Split('\n').LastOrDefault(l => l.Contains("ONAY")));
        Al();   // sunucu ayni isi yeniden verir
        var jobs = Jobs();
        Kontrol(jobs.Length == 1 && KuyrukSayisi() == 0,
                "sunucu isi yeniden verdi: istemci YENIDEN INDIRMEDI, yalnizca onayladi -> CIFT BASKI YOK",
                string.Join(" | ", jobs) + " / kuyruk " + KuyrukSayisi());
        JobsTemizle();
    }

    static void IkiAjanAyniAnda()
    {
        Console.WriteLine("--- 5. Ayni makinede iki ajan ayni anda yokluyor (20 is)");
        var icerik = new Dictionary<string, byte[]>();
        for (int i = 0; i < 20; i++)
        {
            string ad = "20261012_094000_" + i.ToString("000") + "_FS~Es Zamanli " + i + ".pdf";
            icerik[ad] = Pdf(120 * 1024, 100 + i); KuyrugaKoy(ad, icerik[ad]);
        }
        int hata = 0;
        ThreadStart ajan = delegate { try { for (int k = 0; k < 60; k++) { if (!Al()) { if (KuyrukSayisi() == 0) break; } } } catch { Interlocked.Increment(ref hata); } };
        var t1 = new Thread(ajan); var t2 = new Thread(ajan);
        t1.Start(); t2.Start(); t1.Join(); t2.Join();
        bool butun = icerik.All(kv => File.Exists(Path.Combine(iJobs, kv.Key)) && File.ReadAllBytes(Path.Combine(iJobs, kv.Key)).SequenceEqual(kv.Value));
        var jobs = Jobs();
        Kontrol(hata == 0 && butun && jobs.Length == 20 && KuyrukSayisi() == 0,
                "20 isin hepsi BIR kez ve butun geldi; bozuk/fazla dosya yok; kuyruk bos",
                "jobs " + jobs.Length + " | kuyruk " + KuyrukSayisi() + " | hata " + hata + " | " + string.Join(",", jobs.Where(j => j.EndsWith(".tmp")).ToArray()));
        JobsTemizle();
    }

    static void YanlisSifre()
    {
        Console.WriteLine("--- 6. Yanlis istemci sifresi");
        KuyrugaKoy("20261012_095000_001_FS~Sifre Testi.pdf", Pdf(10 * 1024, 11));
        bool ok = Al(null, "YANLIS-ANAHTAR");
        string g = IstemciGunlugu();
        Kontrol(!ok && KuyrukSayisi() == 1 && Jobs().Length == 0, "is VERILMEDI, sunucuda kaldi", null);
        Kontrol(g.Contains("ISTEMCI SIFRESI REDDEDILDI (403)") && g.Contains("ClientKey"),
                "gunlukte sebep ve cozum: 'ISTEMCI SIFRESI REDDEDILDI (403) ... ClientKey'", g.Split('\n').LastOrDefault(l => l.Contains("403")));
        Kontrol(SunucuGunlugu().Contains("hatali istemci sifresi"), "sunucu gunlugunde de yaziyor", null);
        Al();   // dogru anahtarla
        Kontrol(KuyrukSayisi() == 0, "dogru sifreyle is alindi", null);
        JobsTemizle();
    }

    static void SunucuKapali()
    {
        Console.WriteLine("--- 7. Sunucu kapali (baglanti reddediliyor)");
        int bos;
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); bos = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool ok = Al("http://localhost:" + bos);
        long ms = sw.ElapsedMilliseconds;
        string g = IstemciGunlugu();
        Kontrol(!ok && ms < 5000, "hizla vazgecildi (" + ms + " ms)", null);
        Kontrol(g.Contains("SUNUCUYA BAGLANILAMADI"), "gunlukte 'SUNUCUYA BAGLANILAMADI: sunucu kapali, panel calismiyor ya da guvenlik duvari...'",
                g.Split('\n').LastOrDefault(l2 => l2.Contains(bos.ToString())));
    }

    static void GuvenlikDuvariSessiz()
    {
        Console.WriteLine("--- 8. Guvenlik duvari paketi SESSIZCE dusuruyor (yanitsiz adres)");
        string kara = "http://10.255.255.1:8360";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool y1 = (bool)yoklanmali.Invoke(null, new object[] { kara });
        long ms1 = sw.ElapsedMilliseconds; sw.Restart();
        bool y2 = (bool)yoklanmali.Invoke(null, new object[] { kara });
        long ms2 = sw.ElapsedMilliseconds;
        Kontrol(!y1 && ms1 < 3500, "ilk denemede 2 sn'lik port denetimiyle elendi (" + ms1 + " ms; eskiden HTTP 20-60 sn beklerdi)", null);
        Kontrol(!y2 && ms2 < 200, "ikinci turda HIC beklemeden atlandi (" + ms2 + " ms) - gercek sunucu gecikmez", null);
        Kontrol(IstemciGunlugu().Contains("SUNUCU ERISILEMIYOR: " + kara), "gunlukte 'SUNUCU ERISILEMIYOR ... sonra yeniden denenecek'", null);
        Kontrol((bool)yoklanmali.Invoke(null, new object[] { Url }), "calisan sunucu etkilenmedi", null);
    }

    static void ZehirliIsKarantina()
    {
        Console.WriteLine("--- 9. Istemci isi diske yazamiyor (zehirli is) -> karantina, arkadaki is akar");
        var saat = DateTime.Now;
        Kuyruk.Simdi = delegate { return saat; };
        string zehir = "20261012_096000_001_FS~Yazilamayan.pdf", saglam = "20261012_096001_002_FS~Arkadaki.pdf";
        KuyrugaKoy(zehir, Pdf(20 * 1024, 21));
        KuyrugaKoy(saglam, Pdf(20 * 1024, 22));
        // Yalnizca BU isin adiyla klasor olusturulur: istemci o adla dosya yazamaz (ad bir klasore ait)
        Directory.CreateDirectory(Path.Combine(iJobs, zehir));
        for (int i = 0; i < 9; i++) { Al(); saat = saat.AddSeconds(30); }   // 4,5 dk boyunca hep ayni is
        bool karantinada = File.Exists(Path.Combine(Path.Combine(Path.Combine(sKuyruk, Kuyruk.Sanitize(makine)), "_sorunlu"), zehir + ".gz"));
        Al();
        Kontrol(karantinada, "8+ deneme ve 3+ dk sonra zehirli is KARANTINAYA alindi (silinmedi)", null);
        Kontrol(File.Exists(Path.Combine(iJobs, saglam)) && KuyrukSayisi() == 0, "ARKADAKI is beklemeden teslim edildi - kuyruk tikanmadi", string.Join(" | ", Jobs()));
        string g = IstemciGunlugu();
        Kontrol(g.Contains("IS INDIRILEMEDI") && g.Contains(zehir), "istemci gunlugunde her basarisizligin sebebi var", null);
        Kontrol(SunucuGunlugu().Contains("kenara alindi"), "sunucu gunlugunde karantina kaydi var", null);
        Kuyruk.Simdi = delegate { return DateTime.Now; };
        Directory.Delete(Path.Combine(iJobs, zehir));
        JobsTemizle();
    }

    // ---------------------------------------------------------------- son testler
    static object Cagir(Type t, string ad, params object[] a)
    {
        var m = t.GetMethod(ad, OZEL);
        if (m == null) throw new Exception(t.Name + "." + ad + " yok");
        try { return m.Invoke(null, a); } catch (TargetInvocationException ex) { throw ex.InnerException; }
    }

    static Dictionary<string, string[]> Basilanlar()
    {
        return (Dictionary<string, string[]>)Cagir(typeof(Dashboard), "LoadPrinted");
    }

    static int Satir(string metin, string aranan)
    {
        int n = 0, i = 0;
        while ((i = metin.IndexOf(aranan, i, StringComparison.Ordinal)) >= 0) { n++; i += aranan.Length; }
        return n;
    }

    static void BasildiBildirimi()
    {
        Console.WriteLine("--- 11. 'Basildi' bildirimi uctan uca (istemci -> sunucu -> panel)");
        Ayarla(typeof(ClientAgent), "printedCsv", Path.Combine(kok, "istemci\\stats\\printed.csv"));
        string ad = "20261015_090000_001_FS~Şirket İçi Özet.pdf";
        Cagir(typeof(ClientAgent), "RecordPrinted", ad, makine + " \\ Yazıcı Ğ-1", "OK");
        string[] kayit;
        bool var = Basilanlar().TryGetValue(ad, out kayit);
        Kontrol(var && kayit[3] == makine + " \\ Yazıcı Ğ-1", "panel isi 'Basildi' goruyor; Turkce belge ve yazici adi bozulmadi",
                var ? string.Join(" | ", kayit) : "panelde yok | istemci: " + IstemciGunlugu().Split('\n').LastOrDefault(x => x.Contains("BILDIRIM"))
                    + " | sunucu: " + SunucuGunlugu().Split('\n').LastOrDefault(x => x.Contains("printed") || x.Contains("Ingest") || x.Contains("hatasi"))
                    + " | panelin gordugu: " + string.Join(" ; ", Basilanlar().Keys.ToArray()) + " | Db.Err: " + Db.Err);
    }

    static void BildirimBekletme()
    {
        Console.WriteLine("--- 12. Sunucu kapaliyken 'basildi' bildirimi kaybolmuyor");
        int bos; var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); bos = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        IniYaz("testkey", "localhost:" + bos);
        string ad = "20261015_091000_001_FS~Bekleyen Bildirim.pdf";
        Cagir(typeof(ClientAgent), "RecordPrinted", ad, makine + " \\ HP", "OK");
        string bekleyen = Path.Combine(kok, "istemci\\stats\\gonderilemeyen-bildirimler.csv");
        Kontrol(File.Exists(bekleyen) && File.ReadAllText(bekleyen).Contains(ad) && !Basilanlar().ContainsKey(ad),
                "sunucu kapali: bildirim kalici dosyada bekliyor (eskiden kayboluyordu)", null);
        IniYaz("testkey", "localhost:" + vekil.Port);
        Cagir(typeof(ClientAgent), "BekleyenBildirimleriGonder", Url, "testkey");
        Kontrol(Basilanlar().ContainsKey(ad) && !File.Exists(bekleyen), "sunucu gelince gonderildi, bekleme dosyasi temizlendi", null);
        Kontrol(IstemciGunlugu().Contains("BEKLEYEN BILDIRIMLER GONDERILDI"), "gunlukte gonderim kaydi", null);
    }

    static string Post(string yol)
    {
        try { using (var wc = new WebClient()) return wc.UploadString("http://localhost:" + sunucuPort + yol, "POST", ""); }
        catch (WebException ex) { var r = ex.Response as HttpWebResponse; return r != null ? "HTTP " + (int)r.StatusCode : ex.Status.ToString(); }
    }

    static void KalpAtisiVeAdDegisimi()
    {
        Console.WriteLine("--- 13. Kalp atisi ve bilgisayar ADI DEGISIKLIGI (HTTP uzerinden)");
        var defter = new KimlikDefteri(Path.Combine(kok, "sunucu\\stats\\kimlikler.csv"));
        defter.Bekleme = TimeSpan.FromSeconds(2);
        Ayarla(typeof(Dashboard), "kimlikDefteri", defter);
        Post("/api/heartbeat?machine=MUHASEBE-ESKI&key=k1&iid=AAAA1111");
        Kuyruk.OnbellegiTemizle();
        Kontrol(Kuyruk.Cevrimici("MUHASEBE-ESKI"), "kalp atisindan sonra makine cevrimici", null);
        KuyrugaKoy("20261015_092000_001_ayse~Eski Adla Bekleyen.pdf", Pdf(5 * 1024, 31), "MUHASEBE-ESKI");
        KuyrugaKoy("20261015_092001_002_ali~Eski Adla Bekleyen 2.pdf", Pdf(5 * 1024, 32), "MUHASEBE-ESKI");
        Post("/api/heartbeat?machine=MUHASEBE-YENI&key=k1&iid=AAAA1111");   // ayni kurulum, yeni ad
        Thread.Sleep(2300);
        Post("/api/heartbeat?machine=MUHASEBE-YENI&key=k1&iid=AAAA1111");
        string yeniQ = Path.Combine(sKuyruk, "MUHASEBE-YENI");
        int tasinan = Directory.Exists(yeniQ) ? Directory.GetFiles(yeniQ, "*.gz").Length : 0;
        int kalan = Directory.GetFiles(Path.Combine(sKuyruk, "MUHASEBE-ESKI"), "*.gz").Length;
        Kontrol(tasinan == 2 && kalan == 0, "eski adla bekleyen 2 is yeni ada TASINDI", "yeni " + tasinan + " / eski " + kalan);
        Kontrol(SunucuGunlugu().Contains("BILGISAYAR ADI DEGISTI: 'MUHASEBE-ESKI' -> 'MUHASEBE-YENI'"), "sunucu gunlugunde ad degisikligi", null);
    }

    static void CiftOnay()
    {
        Console.WriteLine("--- 14. Ayni is iki kez onaylaniyor (iki ajan ayni isi indirdi)");
        KuyrugaKoy("20261015_093000_001_FS~Cift Onay A.pdf", Pdf(5 * 1024, 41));
        KuyrugaKoy("20261015_093001_002_FS~Cift Onay B.pdf", Pdf(5 * 1024, 42));
        string id;
        using (var wc = new WebClient())
        {
            wc.DownloadData("http://localhost:" + sunucuPort + "/api/jobs?machine=" + Uri.EscapeDataString(makine) + "&key=testkey");
            id = wc.ResponseHeaders["X-Job-Id"];
        }
        string yol = "/api/jobs/done?machine=" + Uri.EscapeDataString(makine) + "&key=testkey&id=" + id;
        string r1 = Post(yol), r2 = Post(yol);
        Kontrol(r1 == "OK", "ilk onay kabul", r1);
        Kontrol(r2 == "OK", "IKINCI onay da kabul (is zaten dusuruldu) - istemciye yanlis 'ONAY REDDEDILDI (500)' gitmiyor", r2);
        Kontrol(KuyrukSayisi() == 1, "diger is yerinde duruyor", KuyrukSayisi().ToString());
        while (Al()) { }
        JobsTemizle();
    }

    static void IkiSunucuGunlukSismesi()
    {
        Console.WriteLine("--- 15. Iki sunucu SIRAYLA hata veriyor: gunluk sisiyor mu?");
        string a = Url, b = "http://localhost:" + sunucuPort;   // ayni sunucu, iki farkli adres
        int once = Satir(IstemciGunlugu(), "IS ALINAMADI");
        for (int i = 0; i < 20; i++) { Al(a, "YANLIS"); Al(b, "YANLIS"); }   // 40 basarisiz yoklama (~2 dk'lik gercek yoklama)
        int eklenen = Satir(IstemciGunlugu(), "IS ALINAMADI") - once;
        Kontrol(eklenen <= 4, "40 basarisiz yoklamada en fazla 4 satir (sunucu basina ilk hata); eskiden her yoklama yaziliyordu",
                eklenen + " satir eklendi");
    }

    static void YanlisSifreVeVekil()
    {
        Console.WriteLine("--- 16. Yanlis sifre + Windows vekil ayari tanimli");
        var eskiBul = Ortak.VekilBul;
        Ortak.VekilBul = delegate(Uri u) { return new Uri("http://vekil.firma.local:8080/"); };
        Ortak.DogrudanBaglan = false;
        int once = Satir(IstemciGunlugu(), "BAGLANTI YOLU DEGISTI");
        for (int i = 0; i < 6; i++) Al(null, "YANLIS");
        int degisim = Satir(IstemciGunlugu(), "BAGLANTI YOLU DEGISTI") - once;
        Kontrol(degisim == 0 && !Ortak.DogrudanBaglan,
                "Print360'in kendi 403'u (sifre reddi) baglanti yolunu DEGISTIRMIYOR (vekil sorunu degil)", degisim + " degisim");
        Ortak.VekilBul = eskiBul; Ortak.DogrudanBaglan = false;
    }

    static void Gunlukler()
    {
        Console.WriteLine("--- 10. Hata ayiklama gunlukleri");
        string s = SunucuGunlugu(), i = IstemciGunlugu();
        Kontrol(s.Contains("[AYRINTI] HTTP GET /api/jobs") && s.Contains("[AYRINTI] HTTP POST /api/jobs/done"),
                "sunucu her istegi yazdi (yontem, yol, makine, IP, sonuc, sure)", null);
        Kontrol(i.Contains("[AYRINTI] HTTP GET") && i.Contains("ms"), "istemci her yoklamayi sureyle yazdi", null);
        Kontrol(!s.Contains("testkey") && !i.Contains("key=testkey"), "gunluklerde istemci sifresi GORUNMUYOR", null);
    }

    static void RdpOturumDegisimi()
    {
        Console.WriteLine("--- 17. Birden fazla RDP oturumu: acilan / kapanan / sira degisimi");
        List<string> ac, kap;
        var bir = new List<string> { "10.2.0.61" };
        var iki = new List<string> { "10.2.0.61", "10.2.0.70" };
        Kontrol(ClientAgent.RdpDegisimi(bir, iki, out ac, out kap) && ac.Count == 1 && ac[0] == "10.2.0.70" && kap.Count == 0,
                "ikinci sunucuya acilan oturum algilandi (eskiden yalnizca ilk oturum izleniyordu)", string.Join(",", ac));
        Kontrol(!ClientAgent.RdpDegisimi(iki, new List<string> { "10.2.0.70", "10.2.0.61" }, out ac, out kap),
                "baglanti sirasi degisince sahte ACILDI yok", null);
        Kontrol(ClientAgent.RdpDegisimi(iki, new List<string> { "10.2.0.70" }, out ac, out kap) && kap.Count == 1 && kap[0] == "10.2.0.61" && ac.Count == 0,
                "oturumlardan biri kapaninca o oturum KAPANDI olarak algilandi", string.Join(",", kap));
        Kontrol(ClientAgent.RdpDegisimi(bir, new List<string>(), out ac, out kap) && kap.Count == 1,
                "son oturum kapandi", null);
    }
}

// ------------------------------------------------------------------
// ARIZA ENJEKTE EDEN TCP VEKILI (istemci <-> vekil <-> sunucu)
//   normal         : baytlari aynen aktarir
//   indirmeyi-kes  : /api/jobs yanitinda KesBayt'tan sonra baglantiyi koparir
//   onayi-dusur    : POST /api/jobs/done istegini sunucuya iletmeden baglantiyi koparir
// Host basligi sunucu portuna cevrilir (HttpListener ana bilgisayar eslesmesi).
// ------------------------------------------------------------------
class Vekil
{
    static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
    public volatile string Mod = "normal";
    public volatile int KesBayt = 64 * 1024;
    public int Port;
    readonly int hedef;
    readonly TcpListener l;

    public Vekil(int hedefPort)
    {
        hedef = hedefPort;
        l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        Port = ((IPEndPoint)l.LocalEndpoint).Port;
        new Thread(Kabul) { IsBackground = true }.Start();
    }

    public void Durdur() { try { l.Stop(); } catch { } }

    void Kabul()
    {
        while (true)
        {
            TcpClient c;
            try { c = l.AcceptTcpClient(); } catch { return; }
            new Thread(delegate() { Isle(c); }) { IsBackground = true }.Start();
        }
    }

    void Isle(TcpClient istemci)
    {
        TcpClient sunucu = null;
        try
        {
            sunucu = new TcpClient("localhost", hedef);
            var ia = istemci.GetStream(); var sa = sunucu.GetStream();
            var durum = new int[] { 0 };   // 1 = son istek is indirme
            long gonderilen = 0;
            var kapat = new Action(delegate { try { istemci.Close(); } catch { } try { sunucu.Close(); } catch { } });

            var c2s = new Thread(delegate()
            {
                var b = new byte[65536]; int n;
                try
                {
                    while ((n = ia.Read(b, 0, b.Length)) > 0)
                    {
                        // Latin-1 her bayti tek karaktere birebir esler: Host basligi
                        // degistirilirken govdedeki UTF-8 baytlari (Turkce harfler) BOZULMAZ.
                        // (Ilk surumde ASCII kullanilmisti; Turkce govdeler '?' oluyordu -
                        // urun degil test vekili hatasiydi.)
                        string m = Latin1.GetString(b, 0, n);
                        if (m.Contains("GET /api/jobs?")) { durum[0] = 1; Interlocked.Exchange(ref gonderilen, 0); }
                        else if (m.StartsWith("GET ") || m.StartsWith("POST ")) durum[0] = 0;
                        if (Mod == "onayi-dusur" && m.Contains("POST /api/jobs/done")) { kapat(); return; }
                        m = m.Replace("Host: localhost:" + Port, "Host: localhost:" + hedef);
                        var o = Latin1.GetBytes(m);
                        sa.Write(o, 0, o.Length);
                    }
                }
                catch { }
                kapat();
            }) { IsBackground = true };
            c2s.Start();

            var buf = new byte[65536]; int k;
            while ((k = sa.Read(buf, 0, buf.Length)) > 0)
            {
                if (Mod == "indirmeyi-kes" && durum[0] == 1)
                {
                    long once = Interlocked.Add(ref gonderilen, k) - k;
                    if (once + k > KesBayt)
                    {
                        int kalan = (int)Math.Max(0, KesBayt - once);
                        if (kalan > 0) ia.Write(buf, 0, kalan);
                        kapat(); return;
                    }
                }
                ia.Write(buf, 0, k);
            }
        }
        catch { }
        finally { try { istemci.Close(); } catch { } try { if (sunucu != null) sunucu.Close(); } catch { } }
    }
}
