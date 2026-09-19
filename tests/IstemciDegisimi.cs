// REGRESYON TESTI: ayni RDP oturumuna BASKA makineden baglanilmasi
//
// Sunucu ajani istemci adini yalnizca baslarken okuyor, sonra hic
// tazelemiyordu. Kullanici (veya sunucuyu yeniden baslatan yonetici) once A
// makinesinden oturum acip sonra ayni oturuma B'den baglaninca isler hala A'nin
// kuyruguna yaziliyordu: panelde "Gonderildi" gorunuyor, cikti ALINMIYORDU.
//
// Duzeltme iki parcadir: (1) ad her turda ve her isten once tazelenir,
// (2) yanlis kuyrukta kalan isler yeni makineye tasinir. Bu test (2)'yi
// gercek dosyalarla dogrular: yalnizca BU kullanicinin ve son 24 saatin
// isleri tasinmali; baskasinin isi ve eski isler yerinde kalmali.
using System;
using System.IO;

class IstemciDegisimi
{
    static string kok;
    static string user = "FS";
    static string Sanitize(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    // server/Print360.ServerAgent.cs icindeki BekleyenleriTasi ile AYNI mantik
    static int BekleyenleriTasi(string eskiMakine, string yeniMakine)
    {
        string kaynak = Path.Combine(kok, Sanitize(eskiMakine));
        if (!Directory.Exists(kaynak)) return 0;
        string hedef = Path.Combine(kok, Sanitize(yeniMakine));
        int tasinan = 0;
        foreach (var f in new DirectoryInfo(kaynak).GetFiles("*.gz"))
        {
            if (f.CreationTime < DateTime.Now.AddHours(-24)) continue;
            string[] b = f.Name.Split(new[] { '_' }, 4);
            if (b.Length < 4) continue;
            string kalan = b[3];
            bool benim = kalan.StartsWith(user + "__", StringComparison.OrdinalIgnoreCase)
                      || kalan.StartsWith(user + "~",  StringComparison.OrdinalIgnoreCase)
                      || kalan.StartsWith(user + ".",  StringComparison.OrdinalIgnoreCase);
            if (!benim) continue;
            Directory.CreateDirectory(hedef);
            string yeniYol = Path.Combine(hedef, f.Name);
            if (File.Exists(yeniYol)) continue;
            File.Move(f.FullName, yeniYol);
            tasinan++;
        }
        return tasinan;
    }

    static int gecti, toplam;
    static void Kontrol(bool ok, string ad)
    {
        toplam++; if (ok) gecti++;
        Console.WriteLine((ok ? "GECTI " : "KALDI ") + ad);
    }

    static void Main()
    {
        kok = Path.Combine(Path.GetTempPath(), "p360-test-istemci");
        if (Directory.Exists(kok)) Directory.Delete(kok, true);
        string a = Path.Combine(kok, "DESKTOP-7FRHC7B");
        Directory.CreateDirectory(a);

        string benim1  = "20260919_164250_935_FS.pdf.gz";                   // belge adsiz
        string benim2  = "20260919_164323_120_FS~Print Document.pdf.gz";    // belge adli
        string benim3  = "20260919_164518_001_FS__SEC~Rapor.pdf.gz";        // is turu
        string baskasi = "20260919_164600_555_FSADMIN~Bordro.pdf.gz";       // FS ile BASLAYAN baska kullanici
        string digeri  = "20260919_164601_777_Administrator~Fatura.pdf.gz";
        string eskiIs  = "20260821_193128_109_FS~Eski Belge.pdf.gz";        // 24 saatten eski
        foreach (string f in new[] { benim1, benim2, benim3, baskasi, digeri, eskiIs })
            File.WriteAllText(Path.Combine(a, f), "x");
        File.SetCreationTime(Path.Combine(a, eskiIs), DateTime.Now.AddDays(-29));

        int n = BekleyenleriTasi("DESKTOP-7FRHC7B", "EVOPC");
        string b = Path.Combine(kok, "EVOPC");

        Kontrol(n == 3, "kullanicinin 3 isi tasindi (tasinan: " + n + ")");
        Kontrol(File.Exists(Path.Combine(b, benim1)) && File.Exists(Path.Combine(b, benim2))
             && File.Exists(Path.Combine(b, benim3)), "isler yeni makinenin kuyrugunda");
        Kontrol(File.Exists(Path.Combine(a, baskasi)), "adi 'FS' ile BASLAYAN baska kullanicinin isine dokunulmadi");
        Kontrol(File.Exists(Path.Combine(a, digeri)),  "baska kullanicinin isine dokunulmadi");
        Kontrol(File.Exists(Path.Combine(a, eskiIs)),  "24 saatten eski is tasinmadi (surpriz cikti yok)");
        Kontrol(BekleyenleriTasi("DESKTOP-7FRHC7B", "EVOPC") == 0, "ikinci cagri hicbir sey tasimiyor");

        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        try { Directory.Delete(kok, true); } catch { }
        Environment.Exit(gecti == toplam ? 0 : 1);
    }
}
