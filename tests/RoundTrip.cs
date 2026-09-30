// Is kimliginin ve dosya adinin sunucu -> HTTP basligi -> istemci -> sorgu
// dizesi -> sunucu yolculugunda BOZULMADIGINI dogrular (ag katmani olmadan,
// hizli birim testi). Ag katmaniyla birlikte sinama: tests\QsKodlama.cs.
//
// URETIM KODUNU SINAR: server\Print360.Ortak.cs ile birlikte derlenir.
using System;

class RoundTrip
{
    static int Main()
    {
        string[] adlar = {
            "20260821_193128_109_Administrator~Belgeyi Yazdır.pdf.gz",   // bosluk + Turkce i
            "20260821_120000_000_admin~Ocak Bordro ÇĞİÖŞÜ.pdf.gz",       // tum Turkce buyuk
            "20260821_120000_000_admin~fiyat %50 indirim.pdf.gz",        // yuzde isareti
            "20260821_120000_000_admin~a&b=c+d.pdf.gz",                  // & = + (sorgu ayiricilari)
            "20260821_120000_000_admin~duz.pdf.gz"                       // sade ASCII
        };

        int gecti = 0, toplam = 0;
        foreach (string gercekAd in adlar)
        {
            // 1) SUNUCU: kimlik ve ad basliklarini uretir (Ortak.B64Kodla)
            string kimlik = "F:" + Ortak.B64Kodla(gercekAd);
            string adBasligi = Ortak.B64Kodla(gercekAd.Substring(0, gercekAd.Length - 3));

            bool ascii = true;
            foreach (char c in kimlik + adBasligi) if (c > 126 || c < 32) ascii = false;

            // 2) ISTEMCI: kimligi AYNEN sorgu dizesine koyar
            string url = "/api/jobs/done?machine=PC&key=&id=" + kimlik;

            // 3) SUNUCU: sorgu dizesini cozer, kimligi acar (HandleJobDone ile ayni)
            string alinan = Ortak.SorguDegeri(url, "id") ?? "";
            string kod = alinan.StartsWith("F:") ? alinan.Substring(2) : alinan;
            string dosyaAdi = Ortak.B64Coz(kod) ?? kod;

            // 4) ISTEMCI: yerel dosya adini baslktan cozer
            string yerelAd = Ortak.B64Coz(adBasligi);

            // 5) GERIYE UYUMLULUK: eski sunucu adi yuzde-kodlu gonderir
            string eskiYol = Ortak.YuzdeCoz(Uri.EscapeDataString(gercekAd));

            toplam++;
            bool ok = ascii && dosyaAdi == gercekAd && yerelAd + ".gz" == gercekAd && eskiYol == gercekAd;
            if (ok) gecti++;
            Console.WriteLine("{0} {1}", ok ? "GECTI " : "KALDI ", gercekAd);
            if (!ok) Console.WriteLine("        ascii={0} sunucu='{1}' yerel='{2}' eski='{3}'", ascii, dosyaAdi, yerelAd, eskiYol);
        }

        // Base64Url olmayan (eski istemcinin gonderdigi duz) deger null donmeli
        toplam++;
        bool duzOk = Ortak.B64Coz("Belgeyi Yazdır.pdf.gz") == null && Ortak.B64Coz("") == null;
        if (duzOk) gecti++;
        Console.WriteLine((duzOk ? "GECTI " : "KALDI ") + "Base64Url olmayan deger null donuyor (eski istemci uyumu)");

        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        return gecti == toplam ? 0 : 1;
    }
}
