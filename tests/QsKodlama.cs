// KOK SEBEP + REGRESYON TESTI  (Turkce karakter / kimlik tasima)
//
// Bir baski isinin adi sunucudan istemciye ve geri donerken UC katmandan gecer:
//   1) HTTP yanit basligi   - ASCII tasir, Turkce harf bozulur
//   2) HTTP sorgu dizesi    - HttpListener bunu OS ANSI kod sayfasiyla cozer
//   3) disk uzerindeki ad   - UTF-16
// Katmanlardan biri bile adi degistirirse sunucu isi kuyrukta BULAMAZ, "dosya
// yok = zaten silinmis" sanip OK doner ve AYNI IS SONSUZA KADAR yeniden verilir.
//
// Kokten cozum: kimlik olarak ADIN KENDISI tasinmaz. Ad, UTF-8 baytlarinin
// Base64Url'u olarak tasinir; kimlikte yalnizca A-Z a-z 0-9 - _ bulunur.
// Bu test, GERCEK bir HttpListener ve GERCEK dosyalar uzerinde tum zinciri
// bastan sona dogrular.
//
// URETIM KODUNU SINAR: server\Print360.Ortak.cs ile birlikte derlenir
// (Ortak.B64Kodla / B64Coz / SorguDegeri). Eskiden yardimcilarin bir KOPYASINI sinardi.
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

class QsKodlama
{
    static string kuyruk;
    static HttpListener dinleyici;

    static void Main()
    {
        kuyruk = Path.Combine(Path.GetTempPath(), "p360-test-kuyruk");
        if (Directory.Exists(kuyruk)) Directory.Delete(kuyruk, true);
        Directory.CreateDirectory(kuyruk);

        string[] adlar = new string[] {
            "20260821_193128_109_Administrator~Belgeyi Yazdır.pdf.gz",   // sahada takilan is
            "Şirket Özet Ğider Çizelgesi İstanbul.pdf.gz",
            "yuzde %100 zam & indirim = kâr.pdf.gz",
            "duz-ascii-belge.pdf.gz"
        };

        dinleyici = new HttpListener();
        dinleyici.Prefixes.Add("http://localhost:18099/");
        dinleyici.Start();
        var t = new Thread(Sunucu); t.IsBackground = true; t.Start();

        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("Sunucu ANSI kod sayfasi : " + Encoding.Default.WebName);
        Console.WriteLine();

        int gecti = 0, toplam = 0;
        foreach (string ad in adlar)
        {
            toplam++;
            File.WriteAllText(Path.Combine(kuyruk, ad), "x");
            string kimlik, cozulenAd, yanit;
            using (var wc = new WebClient())
            {
                wc.DownloadString("http://localhost:18099/api/jobs?machine=TEST");
                kimlik    = wc.ResponseHeaders["X-Job-Id"];
                cozulenAd = Ortak.B64Coz(wc.ResponseHeaders["X-File-Name-B64"]);
                yanit     = wc.UploadString("http://localhost:18099/api/jobs/done?machine=TEST&id="
                                            + kimlik, "POST", "");
            }
            bool asciiKimlik = true;
            foreach (char c in kimlik) if (c > 126) asciiKimlik = false;
            bool silindi = !File.Exists(Path.Combine(kuyruk, ad));
            bool adDogru = (cozulenAd + ".gz") == ad;
            bool ok = asciiKimlik && silindi && adDogru && yanit == "OK";
            if (ok) gecti++;
            Console.WriteLine((ok ? "GECTI " : "KALDI ") + ad);
            Console.WriteLine("   kimlik saf ASCII      : " + asciiKimlik + "   (" + kimlik + ")");
            Console.WriteLine("   istemcide cozulen ad  : " + (adDogru ? "dogru" : "YANLIS -> " + cozulenAd));
            Console.WriteLine("   kuyruktan dusuruldu   : " + silindi + "  (sunucu yaniti: " + yanit + ")");
        }

        // --- GERIYE UYUMLULUK: eski surum istemci adi yuzde-kodlu gonderir ---
        toplam++;
        string eskiAd = "Eski Surum Yazdır.pdf.gz";
        File.WriteAllText(Path.Combine(kuyruk, eskiAd), "x");
        string y2 = "";
        try
        {
            using (var wc = new WebClient())
                y2 = wc.UploadString("http://localhost:18099/api/jobs/done?machine=TEST&id=F:"
                                     + Uri.EscapeDataString(eskiAd), "POST", "");
        }
        catch (WebException ex) { y2 = "HATA: " + ex.Message; }
        bool eskiOk = !File.Exists(Path.Combine(kuyruk, eskiAd)) && y2 == "OK";
        if (eskiOk) gecti++;
        Console.WriteLine((eskiOk ? "GECTI " : "KALDI ") + "geriye uyumluluk (eski surum istemci, yuzde-kodlu)");

        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        try { Directory.Delete(kuyruk, true); } catch { }
        Environment.Exit(gecti == toplam ? 0 : 1);
    }

    // Sunucunun ilgili iki ucunu birebir taklit eder.
    static void Sunucu()
    {
        while (true)
        {
            HttpListenerContext ctx = dinleyici.GetContext();
            string yol = ctx.Request.Url.AbsolutePath;
            byte[] govde = Encoding.UTF8.GetBytes("OK");
            if (yol == "/api/jobs")
            {
                var f = new DirectoryInfo(kuyruk).GetFiles("*.gz");
                if (f.Length == 0) { ctx.Response.StatusCode = 204; ctx.Response.Close(); continue; }
                string ad = f[0].Name;
                string dosyaAd = ad.Substring(0, ad.Length - 3);
                ctx.Response.AddHeader("X-Job-Id", "F:" + Ortak.B64Kodla(ad));
                ctx.Response.AddHeader("X-File-Name-B64", Ortak.B64Kodla(dosyaAd));
                ctx.Response.AddHeader("X-File-Name", Uri.EscapeDataString(dosyaAd));
                govde = new byte[] { 1 };
            }
            else if (yol == "/api/jobs/done")
            {
                string ham = (Ortak.SorguDegeri(ctx.Request.RawUrl, "id") ?? "").Trim();
                if (ham.StartsWith("F:"))
                {
                    string kimlik = ham.Substring(2);
                    string coz = Ortak.B64Coz(kimlik);
                    string ad = Path.GetFileName(coz != null ? coz : kimlik);
                    string p = Path.Combine(kuyruk, ad);
                    if (File.Exists(p)) File.Delete(p);
                    else { ctx.Response.StatusCode = 500; govde = Encoding.UTF8.GetBytes("SILINEMEDI"); }
                }
            }
            ctx.Response.ContentLength64 = govde.Length;
            ctx.Response.OutputStream.Write(govde, 0, govde.Length);
            ctx.Response.Close();
        }
    }
}
