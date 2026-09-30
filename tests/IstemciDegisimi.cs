// REGRESYON TESTI: ayni RDP oturumuna BASKA makineden baglanilmasi
//
// Sunucu ajani istemci adini yalnizca baslarken okuyordu. Kullanici once A
// makinesinden oturum acip sonra ayni oturuma B'den baglaninca isler hala
// A'nin kuyruguna yaziliyordu: panelde "Gonderildi", cikti YOK.
// Duzeltmenin ikinci parcasi: yanlis kuyrukta kalan isler yeni makineye
// tasinir. Yalnizca BU kullanicinin ve son 24 saatin isleri.
//
// URETIM KODUNU SINAR: server\Print360.Db.cs ile birlikte derlenir
// (Kuyruk.KullaniciIsleriniTasi).
using System;
using System.IO;

class IstemciDegisimi
{
    static int gecti, toplam;
    static void Kontrol(bool ok, string ad)
    {
        toplam++; if (ok) gecti++;
        Console.WriteLine((ok ? "GECTI " : "KALDI ") + ad);
    }

    static int Main()
    {
        string kok = Path.Combine(Path.GetTempPath(), "p360-test-istemci");
        if (Directory.Exists(kok)) Directory.Delete(kok, true);
        Kuyruk.Kok = kok;
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

        int n = Kuyruk.KullaniciIsleriniTasi("DESKTOP-7FRHC7B", "EVOPC", "FS");
        string b = Path.Combine(kok, "EVOPC");

        Kontrol(n == 3, "kullanicinin 3 isi tasindi (tasinan: " + n + ")");
        Kontrol(File.Exists(Path.Combine(b, benim1)) && File.Exists(Path.Combine(b, benim2))
             && File.Exists(Path.Combine(b, benim3)), "isler yeni makinenin kuyrugunda");
        Kontrol(File.Exists(Path.Combine(a, baskasi)), "adi 'FS' ile BASLAYAN baska kullanicinin isine dokunulmadi");
        Kontrol(File.Exists(Path.Combine(a, digeri)),  "baska kullanicinin isine dokunulmadi");
        Kontrol(File.Exists(Path.Combine(a, eskiIs)),  "24 saatten eski is tasinmadi (surpriz cikti yok)");
        Kontrol(Kuyruk.KullaniciIsleriniTasi("DESKTOP-7FRHC7B", "EVOPC", "FS") == 0, "ikinci cagri hicbir sey tasimiyor");
        Kontrol(Kuyruk.KullaniciIsleriniTasi("YOK-PC", "EVOPC", "FS") == 0
             && Kuyruk.KullaniciIsleriniTasi("DESKTOP-7FRHC7B", "EVOPC", "") == 0, "olmayan klasor / bos kullanici guvenli");

        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        try { Directory.Delete(kok, true); } catch { }
        return gecti == toplam ? 0 : 1;
    }
}
