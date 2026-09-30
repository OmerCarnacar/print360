// ============================================================
//  Print360 - ORTAK YARDIMCILAR
//  Sunucu ajani, web paneli, masaustu paneli VE istemci ajani bu dosyayla
//  birlikte derlenir. Testler de (tests\) AYNI dosyayi derler: boylece test
//  edilen sey uretimdeki kodun KENDISIDIR, bir kopyasi degil. Kopya testler,
//  uretim kodu degistiginde de gecmeye devam eder ve hicbir seyi kanitlamaz.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

static class Ortak
{
    // ------------------------------------------------------------------
    // ASCII IS KIMLIGI (Base64Url)
    // Turkce harfler HTTP basliklarinda (ASCII tasir), sorgu dizesinde (kod
    // sayfasina gore cozulur) ve araya giren her katmanda bozulabiliyordu.
    // Kimlik olarak ADIN KENDISI tasinmaz: UTF-8 baytlarinin Base64Url'u
    // tasinir; icinde yalnizca A-Z a-z 0-9 - _ bulunur.
    // ------------------------------------------------------------------
    public static string B64Kodla(string s)
    {
        string b = Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? ""));
        return b.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    // Base64Url degilse null doner (eski surum adi duz gonderebilir).
    public static string B64Coz(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool gecerli = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
                        || (c >= '0' && c <= '9') || c == '-' || c == '_';
            if (!gecerli) return null;
        }
        string b = s.Replace('-', '+').Replace('_', '/');
        int kalan = b.Length % 4;
        if (kalan == 1) return null;
        if (kalan == 2) b += "==";
        else if (kalan == 3) b += "=";
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b)); }
        catch { return null; }
    }

    // Yuzde-kodlu adi coz (eski surum sunucular). "%" yoksa oldugu gibi birakir.
    public static string YuzdeCoz(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('%') < 0) return s;
        try { return Uri.UnescapeDataString(s); } catch { return s; }
    }

    // ------------------------------------------------------------------
    // SORGU DIZESI COZUMU (UTF-8)
    // HttpListener.QueryString degerleri ISLETIM SISTEMININ ANSI kod
    // sayfasiyla cozer (Turkce sunucuda windows-1254); istemci UTF-8 kodlar.
    // "%C4%B1" (Turkce i) sunucuda bozuluyor, onaylanan is kuyrukta
    // bulunamiyor ve ayni is sonsuza kadar yeniden veriliyordu.
    // Ham URL'yi (RawUrl) kendimiz UTF-8 ile cozuyoruz.
    // ------------------------------------------------------------------
    public static string SorguDegeri(string rawUrl, string ad)
    {
        try
        {
            if (rawUrl == null) return null;
            int s = rawUrl.IndexOf('?');
            if (s < 0) return null;
            string[] parcalar = rawUrl.Substring(s + 1).Split('&');
            for (int i = 0; i < parcalar.Length; i++)
            {
                int e = parcalar[i].IndexOf('=');
                if (e < 0) continue;
                if (!string.Equals(parcalar[i].Substring(0, e), ad, StringComparison.OrdinalIgnoreCase)) continue;
                string deger = parcalar[i].Substring(e + 1).Replace("+", "%20");
                try { return Uri.UnescapeDataString(deger); } catch { return deger; }
            }
            return null;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------
    // CSV ALANI (Excel formul enjeksiyonuna karsi)
    // Belge ve kullanici adlari disaridan gelir. "=", "+", "-", "@" ile
    // baslayan alan Excel'de FORMUL olarak yorumlanir (=cmd|'/c calc'!A1).
    // Basa tek tirnak koyunca Excel alani metin sayar.
    // ------------------------------------------------------------------
    public static string CsvAlan(string s)
    {
        s = s ?? "";
        if (s.Length > 0 && "=+-@".IndexOf(s[0]) >= 0) s = "'" + s;
        if (s.Contains(";") || s.Contains("\"") || s.Contains("\n"))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    // ------------------------------------------------------------------
    // GUNLUK YAZIMI (kilitli)
    // Sunucuda istekler ThreadPool'da, istemcide yedi ayri thread ayni dosyaya
    // yazar. Kilitsiz es zamanli AppendAllText cagrilari "dosya kullanimda"
    // hatasi verir ve catch { } bunu SESSIZCE yutar: olcumde 7 thread x 200
    // satirin %56,8'i hic yazilmadi. Tum yazimlar tek kilitten gecer; virus
    // tarayici gibi disaridan kilitler icin kisa yeniden deneme yapilir.
    // ------------------------------------------------------------------
    static readonly object dosyaKilit = new object();
    public const long GunlukSiniri = 5 * 1024 * 1024;

    // Basariliysa null, degilse son hata mesajini doner.
    public static string DosyayaEkle(string dosya, string satir)
    {
        string hata = null;
        lock (dosyaKilit)
        {
            for (int i = 0; i < 5; i++)
            {
                try { File.AppendAllText(dosya, satir); return null; }
                catch (Exception ex) { hata = ex.Message; Thread.Sleep(50); }
            }
        }
        return hata;
    }

    // Gunluk 5 MB'i gecince ".1" olarak devredilir (tek kusak) - disk dolmaz.
    public static void Gunluk(string dosya, string mesaj)
    {
        lock (dosyaKilit)
        {
            try
            {
                var fi = new FileInfo(dosya);
                if (fi.Exists && fi.Length >= GunlukSiniri)
                {
                    string eski = dosya + ".1";
                    if (File.Exists(eski)) File.Delete(eski);
                    File.Move(dosya, eski);
                }
            }
            catch { }
        }
        DosyayaEkle(dosya, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + mesaj + "\r\n");
    }
}

// ------------------------------------------------------------------
// PANEL OTURUMLARI (kilitli)
// Dinleyici cok is parcacikli oldugundan oturumlara ayni anda birden fazla
// thread dokunur. Kilitsiz Dictionary'ye es zamanli yazma ic yapiyi bozar;
// klasik sonucu %100 CPU'da sonsuz dongudur.
// ------------------------------------------------------------------
class OturumDeposu
{
    readonly object kilit = new object();
    readonly Dictionary<string, DateTime> oturumlar = new Dictionary<string, DateTime>();

    // Yeni oturum acar; suresi dolmuslari temizler. Yeni jetonu doner.
    public string Ac(TimeSpan sure)
    {
        string jeton = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        lock (kilit)
        {
            foreach (var k in oturumlar.Where(x => x.Value < DateTime.Now).Select(x => x.Key).ToList())
                oturumlar.Remove(k);
            oturumlar[jeton] = DateTime.Now.Add(sure);
        }
        return jeton;
    }

    public bool Gecerli(string jeton)
    {
        if (string.IsNullOrEmpty(jeton)) return false;
        lock (kilit)
        {
            DateTime bitis;
            return oturumlar.TryGetValue(jeton, out bitis) && bitis > DateTime.Now;
        }
    }

    public void Kapat(string jeton)
    {
        if (string.IsNullOrEmpty(jeton)) return;
        lock (kilit) oturumlar.Remove(jeton);
    }

    public int Sayi { get { lock (kilit) return oturumlar.Count; } }
}
