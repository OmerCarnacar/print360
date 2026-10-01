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
using System.Net;
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

    // GUNLUGUN KENDISI YAZILAMAZSA (logs klasorune izin yok, disk dolu, dosya
    // kilitli) eskiden HICBIR iz kalmiyordu: sorunun kaydi tam da sorun yuzunden
    // yok oluyordu. Bu durumda %TEMP%\Print360-<ad> yedek gunlugune yazilir;
    // yedege gecis ve sebebi ilk satirda belirtilir.
    public static string YedekKlasor = Path.GetTempPath();
    static readonly HashSet<string> yedegeGecildi = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static string YedekGunlukYolu(string dosya)
    {
        return Path.Combine(YedekKlasor, "Print360-" + Path.GetFileName(dosya));
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
        string satir = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + mesaj + "\r\n";
        string hata = DosyayaEkle(dosya, satir);
        if (hata == null) return;
        string yedek = YedekGunlukYolu(dosya);
        bool ilk;
        lock (yedegeGecildi) ilk = yedegeGecildi.Add(dosya);
        if (ilk)
            DosyayaEkle(yedek, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  GUNLUK YAZILAMIYOR: " + dosya
                + " | hata: " + hata + " | kayitlar bu yedek dosyaya yaziliyor\r\n");
        DosyayaEkle(yedek, satir);
    }

    // ------------------------------------------------------------------
    // KLASOR DENETIMI (baslangicta). Klasor yoksa olusturulur; yazma izni,
    // disk doluluğu ayri ayri sinanir. Sorun yoksa bos liste doner.
    // Eskiden izin sorunu ilk iste, belirsiz bir "Erisim engellendi" ile
    // ortaya cikiyordu - hangi klasor, hangi kullanici belli degildi.
    // ------------------------------------------------------------------
    public const long AzBosAlanBayt = 500L * 1024 * 1024;

    public static List<string> KlasorDenetle(IEnumerable<string> klasorler)
    {
        var sorunlar = new List<string>();
        string kim = Environment.UserDomainName + "\\" + Environment.UserName;
        foreach (var k in klasorler)
        {
            try { Directory.CreateDirectory(k); }
            catch (Exception ex) { sorunlar.Add("KLASOR OLUSTURULAMADI: " + k + " | kullanici: " + kim + " | " + ex.Message); continue; }
            string deneme = Path.Combine(k, ".yazma-denemesi-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try { File.WriteAllText(deneme, "x"); File.Delete(deneme); }
            catch (Exception ex) { sorunlar.Add("YAZMA IZNI YOK: " + k + " | kullanici: " + kim + " | " + ex.Message); continue; }
            try
            {
                string kok = Path.GetPathRoot(Path.GetFullPath(k));
                if (!string.IsNullOrEmpty(kok) && !kok.StartsWith("\\\\"))
                {
                    var d = new DriveInfo(kok);
                    if (d.IsReady && d.AvailableFreeSpace < AzBosAlanBayt)
                        sorunlar.Add("DISK DOLMAK UZERE: " + kok + " bos alan " + (d.AvailableFreeSpace / (1024 * 1024)) + " MB ("
                                   + k + ") - yeni isler yazilamayabilir");
                }
            }
            catch { }
        }
        return sorunlar;
    }

    // ------------------------------------------------------------------
    // BAGLANTI HATASINI INSAN DILINE CEVIR. "Unable to connect to the remote
    // server" kimseye sorunun nerede oldugunu soylemiyordu. Her durum icin
    // OLASI SEBEP ve NEREYE BAKILACAGI yazilir.
    // ------------------------------------------------------------------
    public static string BaglantiHatasiAcikla(WebExceptionStatus durum, int httpKodu, string ham, string makine)
    {
        if (httpKodu == 403)
            return "ISTEMCI SIFRESI REDDEDILDI (403): bu makine sunucuda BASKA bir sifreyle kayitli. "
                 + "Print360.ini icindeki ClientKey'i sunucudakiyle ayni yapin ya da sunucu panelinden '"
                 + makine + "' kaydini silip istemciyi yeniden baslatin.";
        if (httpKodu == 404)
            return "SUNUCU ADRESI YANLIS ya da sunucu eski surumde (404): adres Print360 paneline mi gidiyor? | " + ham;
        if (httpKodu >= 500)
            return "SUNUCU HATASI (" + httpKodu + "): sunucu istegi isleyemedi - sunucudaki C:\\Print360\\logs\\dashboard.log'a bakin | " + ham;
        switch (durum)
        {
            case WebExceptionStatus.NameResolutionFailure:
                return "SUNUCU ADI COZULEMEDI (DNS): ad yanlis yazilmis ya da bu agdan gorunmuyor - IP adresiyle deneyin | " + ham;
            case WebExceptionStatus.ConnectFailure:
                return "SUNUCUYA BAGLANILAMADI: sunucu kapali, panel calismiyor ya da guvenlik duvari portu engelliyor | " + ham;
            case WebExceptionStatus.Timeout:
                return "ZAMAN ASIMI: sunucu yanit vermedi - ag cok yavas ya da sunucu asiri mesgul | " + ham;
            case WebExceptionStatus.TrustFailure:
            case WebExceptionStatus.SecureChannelFailure:
                return "GUVENLI BAGLANTI KURULAMADI: sunucu sertifikasi degismis ya da Print360.ini'deki CertHash eslesmiyor | " + ham;
            case WebExceptionStatus.ReceiveFailure:
            case WebExceptionStatus.ConnectionClosed:
            case WebExceptionStatus.KeepAliveFailure:
                return "BAGLANTI AKTARIM SIRASINDA KOPTU: is sunucuda kuyrukta kalir ve yeniden indirilir | " + ham;
            case WebExceptionStatus.ProxyNameResolutionFailure:
                return "VEKIL SUNUCU (PROXY) BULUNAMADI: Windows proxy ayarlarini denetleyin | " + ham;
            default:
                return ham + " (" + durum + ")";
        }
    }

    public static string BaglantiHatasiAcikla(Exception ex, string makine)
    {
        var wex = ex as WebException;
        if (wex == null) return ex.Message;
        var r = wex.Response as HttpWebResponse;
        int kod = 0;
        try { if (r != null) kod = (int)r.StatusCode; } catch { }
        return BaglantiHatasiAcikla(wex.Status, kod, wex.Message, makine);
    }

    // ------------------------------------------------------------------
    // YARIM DOSYA DENETIMI. Aktarim yarida kesilirse (RDP koptu, ag dustu)
    // dosya eksik kalir. Eskiden yaziciya gonderiliyor, PDF motoru anlamsiz bir
    // cikis koduyla dusuyor ya da yazicidan bozuk sayfa cikiyordu.
    // PDF: "%PDF" ile baslar, son 2 KB icinde "%%EOF" bulunur. XPS: ZIP (PK).
    // ------------------------------------------------------------------
    public static bool DosyaButunMu(string yol, out string neden)
    {
        neden = null;
        try
        {
            var fi = new FileInfo(yol);
            if (!fi.Exists) { neden = "dosya yok"; return false; }
            if (fi.Length == 0) { neden = "dosya BOS (0 bayt) - aktarim hic tamamlanmamis"; return false; }
            string uz = fi.Extension.ToLowerInvariant();
            using (var fs = new FileStream(yol, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var bas = new byte[4];
                int n = fs.Read(bas, 0, 4);
                if (uz == ".pdf")
                {
                    if (n < 4 || bas[0] != '%' || bas[1] != 'P' || bas[2] != 'D' || bas[3] != 'F')
                    { neden = "PDF basligi yok (%PDF) - dosya bozuk ya da PDF degil"; return false; }
                    int sonBoy = (int)Math.Min(2048, fi.Length);
                    fs.Seek(-sonBoy, SeekOrigin.End);
                    var son = new byte[sonBoy];
                    int okunan = 0;
                    while (okunan < sonBoy) { int r = fs.Read(son, okunan, sonBoy - okunan); if (r <= 0) break; okunan += r; }
                    string kuyruk = Encoding.ASCII.GetString(son, 0, okunan);
                    if (kuyruk.IndexOf("%%EOF", StringComparison.Ordinal) < 0)
                    { neden = "PDF YARIM: dosya sonu (%%EOF) yok, " + (fi.Length / 1024) + " KB - aktarim yarida kesilmis olabilir"; return false; }
                }
                else if (uz == ".xps" || uz == ".oxps")
                {
                    if (n < 2 || bas[0] != 'P' || bas[1] != 'K')
                    { neden = "XPS dosyasi bozuk (ZIP basligi yok)"; return false; }
                }
            }
            return true;
        }
        catch (Exception ex) { neden = "dosya okunamadi: " + ex.Message; return false; }
    }
}

// ------------------------------------------------------------------
// TEKRAR FILTRESI: "anlik ama gurultusuz" gunluk.
// Ilk hata HEMEN yazilir; ayni hata tekrarlanirsa bastirilir ve sayilir;
// hata DEGISINCE ya da duzelince onceki hatanin kac kez tekrarlandigi yazilir.
// Eskiden ayni hata 2 dakikada bir yaziliyor, araya giren FARKLI hatalar
// da bu sure yuzunden kayboluyordu.
// ------------------------------------------------------------------
class TekrarFiltresi
{
    readonly object kilit = new object();
    string sonMesaj; int tekrar; DateTime ilkZaman, sonOzet;
    public TimeSpan OzetAraligi = TimeSpan.FromMinutes(5);
    public Func<DateTime> Simdi = delegate { return DateTime.Now; };

    // Yazilmasi gereken satirlari doner (0, 1 ya da 2 satir).
    public List<string> Hata(string mesaj)
    {
        var cik = new List<string>();
        lock (kilit)
        {
            DateTime t = Simdi();
            if (mesaj == sonMesaj)
            {
                tekrar++;
                if (t - sonOzet >= OzetAraligi)
                {
                    cik.Add("(devam ediyor) " + mesaj + " | " + ilkZaman.ToString("HH:mm:ss") + "'den beri " + (tekrar + 1) + " kez");
                    sonOzet = t;
                }
                return cik;
            }
            if (sonMesaj != null && tekrar > 0)
                cik.Add("(onceki hata " + (tekrar + 1) + " kez tekrarlandi, " + ilkZaman.ToString("HH:mm:ss") + " - " + t.ToString("HH:mm:ss") + ")");
            cik.Add(mesaj);
            sonMesaj = mesaj; tekrar = 0; ilkZaman = t; sonOzet = t;
        }
        return cik;
    }

    // Hata duzeldiginde cagrilir; bir hata vardiysa kapanis satirini doner.
    public string Duzeldi(string neYapildi)
    {
        lock (kilit)
        {
            if (sonMesaj == null) return null;
            string m = "DUZELDI: " + neYapildi + " | onceki hata " + (tekrar + 1) + " kez, "
                     + ilkZaman.ToString("HH:mm:ss") + " - " + Simdi().ToString("HH:mm:ss") + ": " + sonMesaj;
            sonMesaj = null; tekrar = 0;
            return m;
        }
    }
}

// ------------------------------------------------------------------
// TAKILMA IZLEYICI: bir dosya surekli "hazir degil" (kilitli, boyutu
// degisiyor) sayilip atlaniyorsa eskiden HICBIR iz kalmiyordu - is sonsuza
// kadar bekliyordu. Esik asilinca BIR KEZ, sebebiyle bildirilir.
// ------------------------------------------------------------------
class TakilmaIzleyici
{
    readonly Dictionary<string, DateTime> ilk = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> bildirildi = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public TimeSpan Esik = TimeSpan.FromMinutes(2);
    public Func<DateTime> Simdi = delegate { return DateTime.Now; };

    // Dosya hazir degil: esik asildiysa bildirilecek mesaji doner (bir kez).
    public string HazirDegil(string yol, string sebep)
    {
        lock (ilk)
        {
            DateTime t = Simdi(), bas;
            if (!ilk.TryGetValue(yol, out bas)) { ilk[yol] = t; return null; }
            if (t - bas < Esik || !bildirildi.Add(yol)) return null;
            return "TAKILDI: " + Path.GetFileName(yol) + " " + (int)(t - bas).TotalMinutes + " dakikadir islenemiyor | sebep: " + sebep
                 + " | klasor: " + Path.GetDirectoryName(yol);
        }
    }

    // Dosya islendi ya da kayboldu: kaydi temizle. Takildi bildirildiyse kapanis mesaji doner.
    public string Hazir(string yol)
    {
        lock (ilk)
        {
            ilk.Remove(yol);
            return bildirildi.Remove(yol) ? "TAKILMA COZULDU: " + Path.GetFileName(yol) + " islenebiliyor" : null;
        }
    }

    public void Temizle(IEnumerable<string> mevcut)
    {
        lock (ilk)
        {
            var kume = new HashSet<string>(mevcut, StringComparer.OrdinalIgnoreCase);
            foreach (var k in ilk.Keys.Where(x => !kume.Contains(x)).ToList()) { ilk.Remove(k); bildirildi.Remove(k); }
        }
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
