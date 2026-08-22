// Is kimliginin sunucu -> HTTP basligi -> istemci -> sorgu dizesi -> sunucu
// yolculugunda BOZULMADIGINI dogrular. Sahada bozulan tam olarak buydu.
using System;
using System.Collections.Specialized;
using System.Text;

class RoundTrip
{
    static string CozKodlu(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('%') < 0) return s;
        try { return Uri.UnescapeDataString(s); } catch { return s; }
    }

    static int Main()
    {
        string[] adlar = {
            "20260821_193128_109_Administrator~Belgeyi Yazdır.pdf.gz",   // bosluk + Turkce i
            "20260821_120000_000_admin~Ocak Bordro ÇĞİÖŞÜ.pdf.gz",       // tum Turkce buyuk
            "20260821_120000_000_admin~fiyat %50 indirim.pdf.gz",        // yuzde isareti
            "20260821_120000_000_admin~a&b=c.pdf.gz",                    // & ve =
            "20260821_120000_000_admin~duz.pdf.gz"                       // sade ASCII
        };

        int hata = 0;
        foreach (string gercekAd in adlar)
        {
            // 1) SUNUCU: baslik degerini uretir
            string baslik = "F:" + Uri.EscapeDataString(gercekAd);

            // baslik ASCII olmali (HTTP siniri)
            bool asciiMi = true;
            foreach (char c in baslik) if (c > 126 || c < 32) asciiMi = false;

            // 2) ISTEMCI: kimligi AYNEN sorgu dizesine koyar
            string url = "https://s:8443/api/jobs/done?machine=PC&key=&id=" + baslik;

            // 3) SUNUCU: sorgu dizesini cozumler
            NameValueCollection q = System.Web.HttpUtility.ParseQueryString(
                url.Substring(url.IndexOf('?') + 1), Encoding.UTF8);
            string alinan = q["id"] ?? "";
            string dosyaAdi = alinan.StartsWith("F:") ? alinan.Substring(2) : alinan;

            bool ok = (dosyaAdi == gercekAd) && asciiMi;
            if (!ok) hata++;
            Console.WriteLine("{0}  ascii={1}  {2}", ok ? "GECTI " : "KALDI ", asciiMi ? "evet" : "HAYIR", gercekAd);
            if (!ok) Console.WriteLine("        donen: " + dosyaAdi);

            // 4) Istemcinin dosya adi cozumu de dogru olmali
            string yerelAd = CozKodlu(Uri.EscapeDataString(gercekAd));
            if (yerelAd != gercekAd) { hata++; Console.WriteLine("        YEREL AD BOZUK: " + yerelAd); }
        }
        Console.WriteLine(hata == 0 ? "\nTUM SENARYOLAR GECTI" : "\n" + hata + " SENARYO BASARISIZ");
        return hata == 0 ? 0 : 1;
    }
}
