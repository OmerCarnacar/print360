// REGRESYON TESTI: arka arkaya yazdirilan isler birbirinin ciktisini SILMEMELI.
//
// Sanal yazicinin portu kullanici basina tek dosyadir; Windows her isi onun uzerine yazar.
// OLCULDU: kapi yokken uc isten yalnizca sonuncusu kaliyordu.
// URETIM KODUNU SINAR: server\Print360.SpoolKapisi.cs
//   1) karar kurallari (saf)
//   2) GERCEK spooler: "Microsoft Print to PDF" yazicisina ayni dosyaya 3 is gonderilir,
//      ajan gibi davranan tuketici dosyayi alir; kapi ile 3 ciktinin 3'u de alinmali.
//      Yazici yoksa ya da paylasimli CI'daysa bu bolum atlanir.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

class SpoolKapisiTest
{
    static int gecti, toplam;
    static void Kontrol(bool ok, string ad, string ayrinti = null)
    {
        toplam++; if (ok) gecti++;
        Console.WriteLine((ok ? "GECTI " : "KALDI ") + ad);
        if (!ok && ayrinti != null) Console.WriteLine("       -> " + ayrinti);
    }

    static SpoolKapisi.Is I(uint id, uint d) { return new SpoolKapisi.Is { Id = id, Durum = d }; }
    static string K(IList<SpoolKapisi.Is> isler, bool dosya, uint[] bizim)
    {
        var d = new List<uint>(); var s = new List<uint>();
        SpoolKapisi.Karar(isler, dosya, new HashSet<uint>(bizim), d, s);
        return "D:" + string.Join(",", d) + " S:" + string.Join(",", s);
    }

    static int Main()
    {
        const uint P = SpoolKapisi.PAUSED, B = SpoolKapisi.PRINTING, BIT = SpoolKapisi.PRINTED;
        Console.WriteLine("--- 1. Karar kurallari");
        Kontrol(K(new[] { I(5, 0) }, false, new uint[0]) == "D: S:", "bos kuyrukta ilk is serbest", K(new[] { I(5, 0) }, false, new uint[0]));
        Kontrol(K(new[] { I(5, B), I(6, 0), I(7, 0) }, false, new uint[0]) == "D:6,7 S:", "biri basilirken gelenler bekletilir");
        Kontrol(K(new[] { I(5, BIT), I(6, P), I(7, P) }, true, new uint[] { 6, 7 }) == "D: S:", "onceki cikti alinmadan hicbiri serbest kalmaz");
        Kontrol(K(new[] { I(6, P), I(7, P) }, false, new uint[] { 6, 7 }) == "D: S:6", "cikti alininca EN ESKI is serbest birakilir");
        Kontrol(K(new[] { I(6, 0), I(7, P) }, false, new uint[] { 7 }) == "D: S:", "serbest is beklerken ikincisi serbest kalmaz");
        Kontrol(K(new[] { I(6, 0), I(7, 0), I(8, 0) }, false, new uint[0]) == "D:7,8 S:", "bostayken ayni anda gelen uc isten yalnizca ilki gecer");
        Kontrol(K(new[] { I(6, P) }, false, new uint[0]) == "D: S:", "kullanicinin kendi duraklattigi ise dokunulmaz");
        Kontrol(K(new[] { I(6, 0) }, true, new uint[0]) == "D:6 S:", "eski cikti alinmamisken yeni is bekletilir");

        Console.WriteLine("--- 2. Gercek spooler: ayni dosyaya arka arkaya 3 is");
        const string YAZICI = "Microsoft Print to PDF";
        bool var_ = PrinterSettings.InstalledPrinters.Cast<string>().Any(x => x == YAZICI);
        if (!var_ || string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine("ATLA   '" + YAZICI + "' yok ya da paylasimli CI");
        else
        {
            string kok = Path.Combine(Path.GetTempPath(), "p360-kapi-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(kok);
            string hedef = Path.Combine(kok, "kullanici.pdf");
            var alinan = new List<int>();
            bool bitir = false;
            // Ajan gibi: dosya kapaninca alip tasir (gercek ajan 2 sn'de bir bakar)
            var tuketici = new Thread(delegate ()
            {
                while (!bitir)
                {
                    Thread.Sleep(2000);
                    if (!File.Exists(hedef)) continue;
                    try
                    {
                        byte[] b;
                        using (var fs = new FileStream(hedef, FileMode.Open, FileAccess.Read, FileShare.None)) { b = new byte[fs.Length]; fs.Read(b, 0, b.Length); }
                        string ic = Encoding.GetEncoding(28591).GetString(b);
                        if (!ic.TrimEnd().EndsWith("%%EOF")) continue;
                        File.Delete(hedef);
                        lock (alinan) alinan.Add(Regex.Matches(ic, @"/Type\s*/Page[^s]").Count);
                    }
                    catch { }
                }
            }) { IsBackground = true };
            tuketici.Start();
            using (var kapi = new SpoolKapisi(YAZICI, hedef, m => Console.WriteLine("       " + m)))
            {
                kapi.Baslat();
                Thread.Sleep(500);
                for (int n = 1; n <= 3; n++)
                {
                    int sayfa = n, basilan = 0;
                    var d = new PrintDocument();
                    d.PrinterSettings.PrinterName = YAZICI;
                    d.PrinterSettings.PrintToFile = true;
                    d.PrinterSettings.PrintFileName = hedef;
                    d.DocumentName = "Print360 kapi testi " + n;
                    d.PrintController = new StandardPrintController();
                    d.PrintPage += (s, e) => { e.Graphics.DrawString("Is " + sayfa, new Font("Arial", 30), Brushes.Black, 100, 100); e.HasMorePages = ++basilan < sayfa; };
                    d.Print();
                }
                var t0 = DateTime.Now;
                while ((DateTime.Now - t0).TotalSeconds < 40) { lock (alinan) if (alinan.Count >= 3) break; Thread.Sleep(300); }
            }
            bitir = true;
            List<int> sonuc; lock (alinan) sonuc = alinan.ToList();
            Kontrol(sonuc.Count == 3, "3 isin 3'u de alindi (kapi yokken yalnizca sonuncusu kaliyordu)", "alinan: " + sonuc.Count);
            Kontrol(sonuc.SequenceEqual(new[] { 1, 2, 3 }), "isler sirasi bozulmadan alindi (1, 2, 3 sayfa)", string.Join(",", sonuc));
            var kalan = new List<string>();
            try { foreach (var j in new System.Management.ManagementObjectSearcher("SELECT Document FROM Win32_PrintJob WHERE Name LIKE 'Microsoft Print to PDF,%'").Get()) kalan.Add(Convert.ToString(j["Document"])); } catch { }
            Kontrol(!kalan.Any(x => x.StartsWith("Print360 kapi testi")), "kuyrukta bekleyen ya da duraklatilmis test isi kalmadi", string.Join(" | ", kalan));
            try { Directory.Delete(kok, true); } catch { }
        }
        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        return gecti == toplam ? 0 : 1;
    }
}
