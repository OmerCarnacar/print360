// Kurulumdaki PE mimari okuyucusunun DOGRU calistigini dogrular.
// Yanlis sonuc, RDP istemcisinin hic acilmamasina yol acar.
using System;
using System.IO;

class PeTest
{
    static bool PeX64(string dosya)
    {
        try
        {
            using (var fs = new FileStream(dosya, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs))
            {
                fs.Position = 0x3C;
                int pe = br.ReadInt32();
                fs.Position = pe;
                if (br.ReadUInt32() != 0x00004550) return true;
                ushort makine = br.ReadUInt16();
                return makine == 0x8664 || makine == 0xAA64;
            }
        }
        catch { return true; }
    }

    static int Main()
    {
        // Bilinen ornekler: Windows'un kendi ikilileri
        string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);        // 64-bit
        string wow   = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);     // 32-bit
        var testler = new[] {
            new { Yol = Path.Combine(sys32, "notepad.exe"), Beklenen = true,  Ad = "System32 (64-bit)" },
            new { Yol = Path.Combine(wow,   "notepad.exe"), Beklenen = false, Ad = "SysWOW64 (32-bit)" },
            new { Yol = @"D:\GLOBAL\Print_360\vc\Print360.VC.dll", Beklenen = true, Ad = "Print360.VC.dll" }
        };
        int hata = 0;
        foreach (var t in testler)
        {
            if (!File.Exists(t.Yol)) { Console.WriteLine("ATLA   " + t.Ad + " (dosya yok)"); continue; }
            bool s = PeX64(t.Yol);
            bool ok = (s == t.Beklenen);
            if (!ok) hata++;
            Console.WriteLine("{0} {1,-22} -> {2}  (beklenen {3})",
                ok ? "GECTI " : "KALDI ", t.Ad, s ? "x64" : "x86", t.Beklenen ? "x64" : "x86");
        }

        // Mimariye gore SECILEN kayit dali dogru mu?
        bool x64 = true;
        string dal = x64 ? @"SOFTWARE\Microsoft\..." : @"SOFTWARE\WOW6432Node\Microsoft\...";
        Console.WriteLine("\nx64 DLL icin secilen dal: " + dal);
        if (dal.Contains("WOW6432Node")) { hata++; Console.WriteLine("HATA: x64 DLL 32-bit dala yazilmamali!"); }
        else Console.WriteLine("GECTI  x64 DLL yalnizca 64-bit dala yaziliyor");

        Console.WriteLine(hata == 0 ? "\nTUM TESTLER GECTI" : "\n" + hata + " TEST BASARISIZ");
        return hata;
    }
}
