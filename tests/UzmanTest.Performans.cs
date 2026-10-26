// ============================================================
//  PERFORMANS TESTI (uzman testi duzenegi uzerinde)
// ============================================================
// Gercek panel sunucusu + gercek istemci kodu, yerel portta. Olculenler:
//   P1 teslim hizi        : tek makine, 300 is
//   P2 cok istemci        : 50 makine x 4 is, 10 paralel yoklayici
//   P3 kalp atisi yuku    : 300 istemci ayni anda, iki tur (gecikme, p95)
//   P4 buyuk gecmis       : 50.000 is + 40.000 'basildi' kaydiyla panel sayfalari
//   P5 hata ayiklama      : 500 bos yoklamanin gunluge yazdigi satir sayisi
//   P6 istemci dongusu    : 20 is dosyasinin hazirlik denetimi suresi
// Esikler URUN HEDEFIDIR (olcumden turetilmedi): asilirsa test kalir.
//   derleme: csc /main:PerformansTest tests\UzmanTest.cs tests\UzmanTest.Performans.cs ...
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;

class PerformansTest
{
    static int Main() { return UzmanTest.Performans(); }
}

partial class UzmanTest
{
    internal static int Performans()
    {
        kok = Path.Combine(Path.GetTempPath(), "p360-perf-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        string uretimOnce = UretimDamgasi();
        try
        {
            Hazirla();
            Kuyruk.AnlikSureMs = 3000;   // URETIM AYARI (uzman testi kapatir; performans olcumu uretimdeki gibi olmali)
            P1TeslimHizi();
            P2CokIstemci();
            P3KalpAtisi();
            P4BuyukGecmis();
            P5HataAyiklamaGunlugu();
            P6IstemciDongusu();
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

    // SURE HEDEFI. Gelistirme makinesinde ve yerel yapimda ZORUNLUDUR. GitHub CI'nin
    // paylasimli (2 cekirdekli, komsulari degisken) makinesinde sureler 5-10 kat
    // oynar: orada hedef asimi UYARI olarak raporlanir, yapimi kirmaz. Dogruluk
    // denetimleri (her is bir kez geldi, hata yok) her ortamda zorunludur.
    static readonly bool PaylasimliCi =
        string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

    static void Hedef(bool ok, string ad, string ayrinti)
    {
        if (ok || !PaylasimliCi) { Kontrol(ok, ad, ayrinti); return; }
        toplam++; gecti++;
        Console.WriteLine("UYARI " + ad + " - paylasimli CI makinesinde asildi (yerel yapimda zorunlu)" + (ayrinti != null ? " | " + ayrinti : ""));
    }

    static string Dogrudan { get { return "http://localhost:" + sunucuPort; } }

    static double Yuzdelik(List<double> l, double p)
    {
        if (l.Count == 0) return 0;
        var s = l.OrderBy(x => x).ToList();
        return s[Math.Min(s.Count - 1, (int)Math.Ceiling(p * s.Count) - 1)];
    }

    static void P1TeslimHizi()
    {
        Console.WriteLine("--- P1. Teslim hizi: tek makine, 300 is (20 KB)");
        for (int i = 0; i < 300; i++) KuyrugaKoy("20261021_100000_" + i.ToString("000") + "_FS~Perf " + i + ".pdf", Pdf(20 * 1024, 1000 + i));
        var sw = Stopwatch.StartNew();
        int n = 0;
        while (n < 400 && Al(Dogrudan)) n++;
        double sn = sw.Elapsed.TotalSeconds;
        Console.WriteLine("       300 is " + sn.ToString("0.00") + " sn -> " + (n / sn).ToString("0") + " is/sn");
        Kontrol(n == 300 && KuyrukSayisi() == 0, "300 isin hepsi teslim edildi", n + " / kuyruk " + KuyrukSayisi());
        Hedef(n / sn >= 20, "hedef: saniyede en az 20 is (" + (n / sn).ToString("0") + ")", null);
        JobsTemizle();
    }

    static void P2CokIstemci()
    {
        Console.WriteLine("--- P2. 50 makine x 4 is, 10 paralel yoklayici (ham HTTP)");
        var makineler = Enumerable.Range(1, 50).Select(i => "PERF-PC-" + i.ToString("00")).ToList();
        foreach (var m in makineler)
            for (int j = 0; j < 4; j++) KuyrugaKoy("20261021_110000_" + j.ToString("000") + "_u~" + m + " is " + j + ".pdf", Pdf(8 * 1024, j), m);
        int alinan = 0, hata = 0;
        var gecikme = new List<double>();
        var sw = Stopwatch.StartNew();
        var isler = new List<Thread>();
        for (int t = 0; t < 10; t++)
        {
            var benim = makineler.Skip(t * 5).Take(5).ToList();
            var th = new Thread(delegate()
            {
                foreach (var m in benim)
                    for (int k = 0; k < 10; k++)
                    {
                        try
                        {
                            var g = Stopwatch.StartNew();
                            string id;
                            using (var wc = new WebClient())
                            {
                                byte[] b = wc.DownloadData(Dogrudan + "/api/jobs?machine=" + m + "&key=k-" + m);
                                id = wc.ResponseHeaders["X-Job-Id"];
                                if (id == null) break;
                                wc.UploadString(Dogrudan + "/api/jobs/done?machine=" + m + "&key=k-" + m + "&id=" + id, "POST", "");
                            }
                            lock (gecikme) gecikme.Add(g.Elapsed.TotalMilliseconds);
                            Interlocked.Increment(ref alinan);
                        }
                        catch { Interlocked.Increment(ref hata); break; }
                    }
            });
            isler.Add(th); th.Start();
        }
        isler.ForEach(x => x.Join());
        double sn = sw.Elapsed.TotalSeconds;
        int kalan = makineler.Sum(m => { string d = Path.Combine(sKuyruk, m); return Directory.Exists(d) ? Directory.GetFiles(d, "*.gz").Length : 0; });
        Console.WriteLine("       200 is " + sn.ToString("0.00") + " sn | is basina ortalama " + gecikme.Average().ToString("0") + " ms, p95 "
                          + Yuzdelik(gecikme, 0.95).ToString("0") + " ms");
        Kontrol(alinan == 200 && kalan == 0 && hata == 0, "200 isin hepsi bir kez teslim edildi, hata yok", alinan + " alindi, " + kalan + " kaldi, " + hata + " hata");
        Hedef(Yuzdelik(gecikme, 0.95) < 500, "hedef: is basina p95 < 500 ms", Yuzdelik(gecikme, 0.95).ToString("0") + " ms");
    }

    static void P3KalpAtisi()
    {
        Console.WriteLine("--- P3. Kalp atisi yuku: 300 istemci ayni anda, 2 tur");
        for (int tur = 1; tur <= 2; tur++)
        {
            var gecikme = new List<double>();
            int hata = 0;
            var sw = Stopwatch.StartNew();
            var isler = new List<Thread>();
            for (int t = 0; t < 20; t++)
            {
                int bas = t * 15;
                var th = new Thread(delegate()
                {
                    for (int i = bas; i < bas + 15; i++)
                    {
                        var g = Stopwatch.StartNew();
                        try
                        {
                            using (var wc = new WebClient())
                                wc.UploadString(Dogrudan + "/api/heartbeat?machine=HB-PC-" + i.ToString("000") + "&key=hb" + i
                                                + "&printer=HP&user=u" + i + "&iid=" + i.ToString("x8"), "POST", "");
                            lock (gecikme) gecikme.Add(g.Elapsed.TotalMilliseconds);
                        }
                        catch { Interlocked.Increment(ref hata); }
                    }
                });
                isler.Add(th); th.Start();
            }
            isler.ForEach(x => x.Join());
            Console.WriteLine("       tur " + tur + ": " + sw.Elapsed.TotalSeconds.ToString("0.00") + " sn | ortalama " + gecikme.Average().ToString("0")
                              + " ms, p95 " + Yuzdelik(gecikme, 0.95).ToString("0") + " ms, en kotu " + gecikme.Max().ToString("0") + " ms, hata " + hata);
            if (tur == 2)
            {
                Kontrol(hata == 0, "300 kalp atisinin hepsi kabul edildi", hata + " hata");
                Hedef(Yuzdelik(gecikme, 0.95) < 1000, "hedef: 300 es zamanli istemcide kalp atisi p95 < 1 sn", Yuzdelik(gecikme, 0.95).ToString("0") + " ms");
            }
        }
        Kuyruk.OnbellegiTemizle();
        Kontrol(Kuyruk.Cevrimici("HB-PC-000") && Kuyruk.Cevrimici("HB-PC-299"), "istemciler cevrimici gorunuyor", null);
    }

    static double Getir(string yol, out int boyut)
    {
        var sw = Stopwatch.StartNew();
        using (var wc = new WebClient()) { wc.Encoding = Encoding.UTF8; boyut = wc.DownloadData(Dogrudan + yol).Length; }
        return sw.Elapsed.TotalMilliseconds;
    }

    static void P4BuyukGecmis()
    {
        Console.WriteLine("--- P4. Buyuk gecmis: 50.000 is + 40.000 'basildi' kaydiyla panel");
        var sw = Stopwatch.StartNew();
        using (var c = new System.Data.SQLite.SQLiteConnection("Data Source=" + Db.SqliteDosya + ";Version=3;"))
        {
            c.Open();
            using (var tx = c.BeginTransaction())
            {
                var j = c.CreateCommand(); j.Transaction = tx;
                j.CommandText = "INSERT INTO Jobs(Tarih,Kullanici,Makine,Belge,Sayfa,Dosya,Kagit,KB,Durum) VALUES(@t,@u,@m,@b,@s,@f,'A4',@k,'OK')";
                var pt = c.CreateCommand(); pt.Transaction = tx;
                pt.CommandText = "INSERT INTO Printed(Tarih,Makine,Dosya,Yazici,Durum) VALUES(@t,@m,@f,'HP','OK')";
                var t0 = DateTime.Now.AddDays(-365);
                for (int i = 0; i < 50000; i++)
                {
                    string f = "2025" + (i % 12 + 1).ToString("00") + "01_120000_" + i.ToString("00000") + "_kullanici" + (i % 40) + "~Belge " + i + ".pdf";
                    string t = t0.AddMinutes(i * 10).ToString("yyyy-MM-dd HH:mm:ss"), m = "PC-" + (i % 60).ToString("00");
                    j.Parameters.Clear();
                    j.Parameters.AddWithValue("@t", t); j.Parameters.AddWithValue("@u", "kullanici" + (i % 40));
                    j.Parameters.AddWithValue("@m", m); j.Parameters.AddWithValue("@b", "Belge " + i);
                    j.Parameters.AddWithValue("@s", 1 + i % 9); j.Parameters.AddWithValue("@f", f); j.Parameters.AddWithValue("@k", 50 + i % 500);
                    j.ExecuteNonQuery();
                    if (i % 5 != 0)
                    {
                        pt.Parameters.Clear();
                        pt.Parameters.AddWithValue("@t", t); pt.Parameters.AddWithValue("@m", m); pt.Parameters.AddWithValue("@f", f);
                        pt.ExecuteNonQuery();
                    }
                }
                tx.Commit();
            }
        }
        Console.WriteLine("       veri hazirlandi (" + sw.Elapsed.TotalSeconds.ToString("0.0") + " sn)");
        foreach (var yol in new[] { "/", "/isler", "/export/isler.csv", "/kuyruk", "/tani", "/makineler" })
        {
            int boyut; double ilk = Getir(yol, out boyut); double ikinci = Getir(yol, out boyut);
            Console.WriteLine("       " + yol.PadRight(20) + " ilk " + ilk.ToString("0").PadLeft(6) + " ms | tekrar " + ikinci.ToString("0").PadLeft(6)
                              + " ms | " + (boyut / 1024) + " KB");
            Hedef(ikinci < 3000, "hedef: " + yol + " 50.000 islik gecmisle 3 sn icinde", ikinci.ToString("0") + " ms");
        }
    }

    static void P5HataAyiklamaGunlugu()
    {
        Console.WriteLine("--- P5. Hata ayiklama modunda 500 bos yoklama -> gunluk satiri");
        int once = Satir(SunucuGunlugu(), "/api/jobs ");
        using (var wc = new WebClient())
            for (int i = 0; i < 500; i++) wc.DownloadData(Dogrudan + "/api/jobs?machine=BOS-PC&key=bos");
        Thread.Sleep(300);
        int eklenen = Satir(SunucuGunlugu(), "/api/jobs ") - once;
        Console.WriteLine("       500 bos yoklama -> " + eklenen + " gunluk satiri");
        Kontrol(eklenen <= 5, "hedef: bos yoklamalar ozetlenir (300 istemcide gunde ~8,6 milyon satir olmasin)", eklenen + " satir");
    }

    static void P6IstemciDongusu()
    {
        Console.WriteLine("--- P6. Istemci: 20 is dosyasinin hazirlik denetimi");
        for (int i = 0; i < 20; i++) File.WriteAllBytes(Path.Combine(iJobs, "20261021_120000_" + i.ToString("000") + "_FS~Hazir " + i + ".pdf"), Pdf(10 * 1024, i));
        var dosyalar = Directory.GetFiles(iJobs).ToList();
        var m = typeof(ClientAgent).GetMethod("HazirOlanlar", OZEL);
        var sw = Stopwatch.StartNew();
        int hazir;
        if (m != null) hazir = ((List<string>)m.Invoke(null, new object[] { dosyalar, null })).Count;
        else
        {
            var tek = typeof(ClientAgent).GetMethod("HazirMi", OZEL);
            hazir = 0;
            foreach (var f in dosyalar) { var a = new object[] { f, null }; if ((bool)tek.Invoke(null, a)) hazir++; }
        }
        double ms = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine("       20 dosya -> " + hazir + " hazir, " + ms.ToString("0") + " ms");
        Kontrol(hazir == 20, "20 dosyanin hepsi hazir bulundu", hazir.ToString());
        Hedef(ms < 2000, "hedef: 20 dosyalik toplu is 2 sn icinde basmaya hazir (dosya basina 500 ms beklenmez)", ms.ToString("0") + " ms");
        JobsTemizle();
    }
}
