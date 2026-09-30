// REGRESYON TESTI: es zamanlilik ve CSV formul enjeksiyonu
//
// URETIM KODUNU SINAR: server\Print360.Ortak.cs ile birlikte derlenir
// (Ortak.DosyayaEkle, OturumDeposu, Ortak.CsvAlan). Eskiden bu test mantigin
// bir KOPYASINI sinardi; uretim kodu degisse de gecmeye devam ederdi.
//
// 1) GUNLUK KAYBI - kilitsiz es zamanli AppendAllText cagrilari "dosya
//    kullanimda" hatasi veriyor, catch { } bunu yutuyordu: 7 thread x 200
//    satirin %56,8'i hic yazilmadi.
// 2) OTURUM DEPOSU - kilitsiz Dictionary'ye es zamanli yazma ic yapiyi bozar.
// 3) CSV FORMUL ENJEKSIYONU - "=" ile baslayan belge adi Excel'de formul olur.
using System;
using System.IO;
using System.Threading;

class EsZamanli
{
    static int gecti, toplam;
    static void Kontrol(bool ok, string ad, string ayrinti)
    {
        toplam++; if (ok) gecti++;
        Console.WriteLine((ok ? "GECTI " : "KALDI ") + ad);
        Console.WriteLine("   " + ayrinti);
    }

    static void GunlukTesti()
    {
        string dosya = Path.Combine(Path.GetTempPath(), "p360-eszamanli.log");
        if (File.Exists(dosya)) File.Delete(dosya);
        const int T = 7, N = 200;
        var th = new Thread[T];
        for (int i = 0; i < T; i++)
        {
            int no = i;
            th[i] = new Thread(delegate() { for (int j = 0; j < N; j++) Ortak.Gunluk(dosya, "t" + no + " s" + j); });
        }
        foreach (var x in th) x.Start();
        foreach (var x in th) x.Join();
        int yazilan = File.ReadAllLines(dosya).Length;
        File.Delete(dosya);
        Kontrol(yazilan == T * N, "gunluk satiri kaybolmuyor (7 thread x 200 satir, Ortak.Gunluk)",
                "beklenen " + (T * N) + ", yazilan " + yazilan + ", kayip " + (T * N - yazilan));
    }

    static void DevirTesti()
    {
        string dosya = Path.Combine(Path.GetTempPath(), "p360-devir.log");
        try { File.Delete(dosya); File.Delete(dosya + ".1"); } catch { }
        using (var fs = new FileStream(dosya, FileMode.Create)) fs.SetLength(Ortak.GunlukSiniri + 10);
        Ortak.Gunluk(dosya, "devirden sonraki ilk satir");
        bool ok = File.Exists(dosya + ".1") && new FileInfo(dosya).Length < 1024
               && File.ReadAllText(dosya).Contains("devirden sonraki ilk satir");
        try { File.Delete(dosya); File.Delete(dosya + ".1"); } catch { }
        Kontrol(ok, "5 MB'i gecen gunluk .1 olarak devrediliyor", ok ? "yeni gunluk basladi, eskisi .1" : "DEVREDILMEDI");
    }

    static void OturumTesti()
    {
        var depo = new OturumDeposu();
        const int T = 8, N = 400;
        var th = new Thread[T];
        int hata = 0;
        for (int i = 0; i < T; i++)
        {
            th[i] = new Thread(delegate()
            {
                for (int j = 0; j < N; j++)
                {
                    try
                    {
                        string jeton = depo.Ac(TimeSpan.FromHours(12));
                        if (!depo.Gecerli(jeton)) Interlocked.Increment(ref hata);
                        depo.Kapat(jeton);
                        if (depo.Gecerli(jeton)) Interlocked.Increment(ref hata);
                    }
                    catch { Interlocked.Increment(ref hata); }
                }
            });
        }
        foreach (var x in th) x.Start();
        foreach (var x in th) x.Join();
        Kontrol(hata == 0 && depo.Sayi == 0, "oturum deposu es zamanli erisimde bozulmuyor",
                (T * N) + " ac/dogrula/kapat dongusu, hata " + hata + ", kalan oturum " + depo.Sayi);

        var d2 = new OturumDeposu();
        string eski = d2.Ac(TimeSpan.FromMilliseconds(-1));   // aninda suresi dolmus
        Kontrol(!d2.Gecerli(eski) && !d2.Gecerli("") && !d2.Gecerli(null),
                "suresi dolmus / bos jeton gecersiz", "dogru");
    }

    static void CsvTesti()
    {
        string[][] durum = new string[][] {
            new string[] { "=cmd|'/c calc'!A1",   "'" },
            new string[] { "+1+1",                "'" },
            new string[] { "-2+3",                "'" },
            new string[] { "@SUM(A1:A9)",         "'" },
            new string[] { "Normal Belge.pdf",    "N" },
            new string[] { "2026-09-01 Rapor",    "2" }
        };
        int ok = 0;
        foreach (var d in durum)
        {
            string cikti = Ortak.CsvAlan(d[0]);
            string ic = cikti.StartsWith("\"") ? cikti.Substring(1) : cikti;
            if (ic.StartsWith(d[1])) ok++;
            else Console.WriteLine("   BEKLENMEDIK: " + d[0] + " -> " + cikti);
        }
        Kontrol(ok == durum.Length, "CSV alanlari Excel'de formul olarak calismiyor",
                ok + "/" + durum.Length + " (=, +, -, @ ile baslayanlar metne cevrildi)");
        Kontrol(Ortak.CsvAlan("a;b") == "\"a;b\"" && Ortak.CsvAlan("x\"y") == "\"x\"\"y\"",
                "ayirici ve tirnak iceren alanlar dogru tirnaklaniyor", Ortak.CsvAlan("a;b") + "  " + Ortak.CsvAlan("x\"y"));
    }

    static int Main()
    {
        GunlukTesti();
        DevirTesti();
        OturumTesti();
        CsvTesti();
        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        return gecti == toplam ? 0 : 1;
    }
}
