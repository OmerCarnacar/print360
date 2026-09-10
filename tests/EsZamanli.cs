// REGRESYON TESTI: es zamanlilik ve CSV formul enjeksiyonu
//
// 1) GUNLUK KAYBI - Log() kilitsizken es zamanli AppendAllText cagrilari
//    "dosya kullanimda" hatasi veriyor ve catch { } bunu yutuyordu. Olcumde
//    7 thread x 200 satirin %56,8'i hic yazilmadi. Gunlugun en cok gerektigi
//    an (sistem yogunken) tam da satirlarin kayboldugu andi.
// 2) OTURUM SOZLUGU - dinleyici cok is parcacikli oldugundan kilitsiz
//    Dictionary'ye es zamanli yazma ic yapiyi bozar (%100 CPU / istisna).
// 3) CSV FORMUL ENJEKSIYONU - "=" ile baslayan belge adi, rapor Excel'de
//    acildiginda formul olarak yorumlanir.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    // ---- 1) Gunluk: uretimdeki kilitli yazim ----
    static readonly object logKilit = new object();
    static string logFile;
    static void Log(string msg)
    {
        lock (logKilit)
        {
            for (int i = 0; i < 5; i++)
            {
                try { File.AppendAllText(logFile, msg + "\r\n"); return; }
                catch { Thread.Sleep(50); }
            }
        }
    }

    static void GunlukTesti()
    {
        logFile = Path.Combine(Path.GetTempPath(), "p360-eszamanli.log");
        if (File.Exists(logFile)) File.Delete(logFile);
        const int T = 7, N = 200;
        var th = new Thread[T];
        for (int i = 0; i < T; i++)
        {
            int no = i;
            th[i] = new Thread(delegate() { for (int j = 0; j < N; j++) Log("t" + no + " s" + j); });
        }
        foreach (var x in th) x.Start();
        foreach (var x in th) x.Join();
        int yazilan = File.ReadAllLines(logFile).Length;
        File.Delete(logFile);
        Kontrol(yazilan == T * N, "gunluk satiri kaybolmuyor (7 thread x 200 satir)",
                "beklenen " + (T * N) + ", yazilan " + yazilan + ", kayip " + (T * N - yazilan));
    }

    // ---- 2) Oturum sozlugu: uretimdeki kilitli erisim ----
    static readonly object durumKilit = new object();
    static Dictionary<string, DateTime> sessions = new Dictionary<string, DateTime>();

    static void OturumTesti()
    {
        const int T = 8, N = 400;
        var th = new Thread[T];
        int hata = 0;
        for (int i = 0; i < T; i++)
        {
            int no = i;
            th[i] = new Thread(delegate()
            {
                for (int j = 0; j < N; j++)
                {
                    string tok = "t" + no + "-" + j;
                    try
                    {
                        lock (durumKilit)
                        {
                            foreach (var k in sessions.Where(x => x.Value < DateTime.Now).Select(x => x.Key).ToList())
                                sessions.Remove(k);
                            sessions[tok] = DateTime.Now.AddHours(12);
                        }
                        DateTime bitis;
                        bool auth;
                        lock (durumKilit) auth = sessions.TryGetValue(tok, out bitis) && bitis > DateTime.Now;
                        if (!auth) Interlocked.Increment(ref hata);
                        lock (durumKilit) sessions.Remove(tok);
                    }
                    catch { Interlocked.Increment(ref hata); }
                }
            });
        }
        foreach (var x in th) x.Start();
        foreach (var x in th) x.Join();
        Kontrol(hata == 0 && sessions.Count == 0, "oturum sozlugu es zamanli erisimde bozulmuyor",
                (T * N) + " giris/cikis dongusu, hata " + hata + ", kalan oturum " + sessions.Count);
    }

    // ---- 3) CSV formul enjeksiyonu: uretimdeki CsvAlan ----
    static string CsvAlan(string s)
    {
        s = s ?? "";
        if (s.Length > 0 && "=+-@".IndexOf(s[0]) >= 0) s = "'" + s;
        if (s.Contains(";") || s.Contains("\"") || s.Contains("\n"))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
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
            string cikti = CsvAlan(d[0]);
            string ic = cikti.StartsWith("\"") ? cikti.Substring(1) : cikti;
            if (ic.StartsWith(d[1])) ok++;
            else Console.WriteLine("   BEKLENMEDIK: " + d[0] + " -> " + cikti);
        }
        Kontrol(ok == durum.Length, "CSV alanlari Excel'de formul olarak calismiyor",
                ok + "/" + durum.Length + " (=, +, -, @ ile baslayanlar metne cevrildi)");
    }

    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        GunlukTesti();
        OturumTesti();
        CsvTesti();
        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        Environment.Exit(gecti == toplam ? 0 : 1);
    }
}
