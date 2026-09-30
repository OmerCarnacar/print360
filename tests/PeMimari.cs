// Kurulumdaki PE mimari okuyucusunun ve RDP eklentisi kayit dali seciminin
// DOGRU calistigini dogrular. Yanlis sonuc, RDP istemcisinin hic acilmamasina
// yol acar: 64-bit DLL 32-bit dala yazilirsa mstsc onu yukleyemez.
//
// URETIM KODUNU SINAR: setup\Print360.Setup.cs ile birlikte derlenir
// (Kurulum.PeX64, Kurulum.EklentiKoku).
//   derleme: csc /main:PeTest tests\PeMimari.cs setup\Print360.Setup.cs ...
using System;
using System.IO;

class PeTest
{
    static int Main()
    {
        // Bilinen ornekler: Windows'un kendi ikilileri + projenin kendi DLL'i
        string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);        // 64-bit
        string wow   = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);     // 32-bit
        var testler = new[] {
            new { Yol = Path.Combine(sys32, "notepad.exe"), Beklenen = true,  Ad = "System32 (64-bit)" },
            new { Yol = Path.Combine(wow,   "notepad.exe"), Beklenen = false, Ad = "SysWOW64 (32-bit)" },
            new { Yol = Path.Combine("vc", "Print360.VC.dll"), Beklenen = true, Ad = "Print360.VC.dll" }
        };
        int gecti = 0, toplam = 0;
        foreach (var t in testler)
        {
            if (!File.Exists(t.Yol)) { Console.WriteLine("ATLA   " + t.Ad + " (dosya yok)"); continue; }
            toplam++;
            bool s = Kurulum.PeX64(t.Yol);
            bool ok = (s == t.Beklenen);
            if (ok) gecti++;
            Console.WriteLine("{0} {1,-22} -> {2}  (beklenen {3})",
                ok ? "GECTI " : "KALDI ", t.Ad, s ? "x64" : "x86", t.Beklenen ? "x64" : "x86");
        }

        // Kurulumun GERCEKTEN kullandigi dal secimi
        toplam++;
        string x64Dal = Kurulum.EklentiKoku(true), x86Dal = Kurulum.EklentiKoku(false);
        bool dalOk = !x64Dal.Contains("WOW6432Node") && x86Dal.Contains("WOW6432Node")
                  && x64Dal.EndsWith(@"\AddIns\Print360") && x86Dal.EndsWith(@"\AddIns\Print360");
        if (dalOk) gecti++;
        Console.WriteLine((dalOk ? "GECTI " : "KALDI ") + "x64 DLL 64-bit dala, x86 DLL WOW6432Node dalina yaziliyor");

        // Okunamayan dosya x64 varsayilir (bugunku istemcilerin cogu 64-bit)
        toplam++;
        bool yokOk = Kurulum.PeX64(Path.Combine(Path.GetTempPath(), "p360-olmayan-dosya.dll"));
        if (yokOk) gecti++;
        Console.WriteLine((yokOk ? "GECTI " : "KALDI ") + "okunamayan dosya x64 varsayiliyor");

        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        return gecti == toplam ? 0 : 1;
    }
}
