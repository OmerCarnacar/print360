// ============================================================
//  Print360 - Lisans durumu
//
//  UCRETSIZ SURUM: Yazilim bedelsizdir, kullanim SINIRSIZDIR ve
//  lisans anahtari GEREKMEZ. Cikti limiti yoktur.
//  PARA ILE SATILAMAZ - ayrintilar icin bkz. LICENSE dosyasi.
//
//  Gelistirici : Omer CARNACAR
//  Iletisim    : omer.carnacar@outlook.com.tr
//
//  Not: RSA imza dogrulama altyapisi (Dogrula/Kaydet) geriye donuk
//  uyumluluk icin korunmustur; ucretsiz surumde hicbir kisitlama uygulanmaz.
// ============================================================
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

static class Lisans
{
    // Ucretsiz surum: cikti limiti yok (deneme kisitlamasi kaldirildi)
    public const int DenemeLimiti = int.MaxValue;
    public const string Gelistirici = "Omer CARNACAR";
    public const string Eposta = "omer.carnacar@outlook.com.tr";
    public const string LinkedIn = "https://www.linkedin.com/in/omercarnacar/";
    const string PubKey = "<RSAKeyValue><Modulus>sbr7qriwcbwqR408s3jt1Mf6rVBLoWIHmo7IsNVunnHua3Lh2NjeA1g73UhdHJTYYJvx4GCI1P6OZRqcWQzv6lSIZ1L6DUarKNittS9/F2YFyIPlR5w0+EmKhdcXoQUtGZRR5t/PGeo/6CVDBFt39Lnz6l59PaieEjdPT2nHjoPiQ3lt+pqNJVQjrFm1s5Elak4ioTBKKiqnhP8iUyrjW4C1aPb3T+RP2td09tO9A8Hd65jfYkSIqcuw3IALCrt9i1t8wKOwpmhGqWlpSGqXrbpu07YCXdRVL8IYwnFl2dW03FdVn2mlYR3Ay1MEggX9BanEP0LKpCdfOcWV4YHcAQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
    const string LisansDosya = @"C:\Print360\license.key";

    // Ucretsiz surumde her zaman gecerli - hicbir kisitlama yok
    public static bool Gecerli = true;
    public static string Musteri = "Ucretsiz surum", Bitis = "SINIRSIZ";

    public static void Yukle(bool zorla = false)
    {
        // Ucretsiz surum: lisans dosyasi aranmaz, kisitlama uygulanmaz.
        Gecerli = true;
        if (Musteri.Length == 0) Musteri = "Ucretsiz surum";
        Bitis = "SINIRSIZ";
    }

    public static bool Dogrula(string anahtar, out string musteri, out string bitis)
    {
        musteri = ""; bitis = "";
        try
        {
            var parca = anahtar.Trim().Replace("\r", "").Replace("\n", "").Split('.');
            if (parca.Length != 2) return false;
            byte[] veri = Convert.FromBase64String(parca[0]);
            byte[] imza = Convert.FromBase64String(parca[1]);
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(PubKey);
                if (!rsa.VerifyData(veri, "SHA256", imza)) return false;
            }
            var alan = Encoding.UTF8.GetString(veri).Split('|');
            if (alan.Length < 3 || alan[0] != "PRINT360") return false;
            musteri = alan[1]; bitis = alan[2];
            if (bitis != "SINIRSIZ")
            {
                DateTime b;
                if (!DateTime.TryParseExact(bitis, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out b)) return false;
                if (b.Date < DateTime.Today) { musteri += " (SURESI DOLDU)"; return false; }
            }
            return true;
        }
        catch { return false; }
    }

    public static bool Kaydet(string anahtar)
    {
        string m, b;
        if (!Dogrula(anahtar, out m, out b)) return false;
        File.WriteAllText(LisansDosya, anahtar.Trim());
        Yukle(true);
        return true;
    }

    public static string DurumMetni()
    {
        return "UCRETSIZ SURUM - sinirsiz kullanim, cikti limiti yok. "
             + "Para ile satilamaz. Gelistirici: " + Gelistirici + " (" + Eposta + ")";
    }
}
