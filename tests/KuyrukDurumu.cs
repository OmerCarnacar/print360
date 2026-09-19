// REGRESYON TESTI: "Gonderildi" tek kelimesi uc farkli durumu gizliyordu.
//
// Bu test URETIMDEKI Kuyruk sinifini (server/Print360.Db.cs) DOGRUDAN derleyip
// calistirir - mantigin kopyasini degil. Kuyruk klasoru gecici bir dizine,
// kalp atisi kaynagi sahte bir tabloya yonlendirilir; veritabanina dokunulmaz.
//
//   derleme: csc /r:System.Data.dll /r:<System.Data.SQLite.dll>
//            tests\KuyrukDurumu.cs server\Print360.Db.cs
using System;
using System.Collections.Generic;
using System.IO;

class KuyrukDurumu
{
    static int gecti, toplam;
    static void Kontrol(bool ok, string ad, string ayrinti)
    {
        toplam++; if (ok) gecti++;
        Console.WriteLine((ok ? "GECTI " : "KALDI ") + ad);
        Console.WriteLine("       -> " + (ayrinti ?? "(null)"));
    }

    static void Main()
    {
        string kok = Path.Combine(Path.GetTempPath(), "p360-test-kuyrukdurumu");
        if (Directory.Exists(kok)) Directory.Delete(kok, true);
        Kuyruk.Kok = kok;

        var kalp = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        kalp["EVOPC"]           = DateTime.Now.AddSeconds(-20);      // cevrimici
        kalp["DESKTOP-7FRHC7B"] = DateTime.Now.AddMinutes(-47);      // cevrimdisi
        Kuyruk.KalpAtisiKaynagi = delegate(string m)
        {
            DateTime t; return kalp.TryGetValue(m, out t) ? t : DateTime.MinValue;
        };

        // --- sahadaki durum: is CEVRIMDISI makinenin kuyrugunda 40 dakikadir duruyor
        string is1 = "20260919_164250_935_FS.pdf";
        Directory.CreateDirectory(Path.Combine(kok, "DESKTOP-7FRHC7B"));
        string y1 = Path.Combine(Path.Combine(kok, "DESKTOP-7FRHC7B"), is1 + ".gz");
        File.WriteAllText(y1, "x");
        File.SetCreationTime(y1, DateTime.Now.AddMinutes(-40));
        string d1 = Kuyruk.DurumMetni("DESKTOP-7FRHC7B", is1);
        Kontrol(d1 != null && d1.StartsWith("BEKLIYOR") && d1.Contains("40 dk") && d1.Contains("cevrimdisi"),
                "cevrimdisi makinenin kuyrugundaki is ACIKCA bildiriliyor", d1);

        // --- is cevrimici makinenin kuyrugunda, az once yazildi: normal, alarm yok
        string is2 = "20260919_170000_001_FS~Rapor.pdf";
        Directory.CreateDirectory(Path.Combine(kok, "EVOPC"));
        File.WriteAllText(Path.Combine(Path.Combine(kok, "EVOPC"), is2 + ".gz"), "x");
        string d2 = Kuyruk.DurumMetni("EVOPC", is2);
        Kontrol(d2 != null && d2.StartsWith("Kuyrukta") && !d2.Contains("BEKLIYOR"),
                "cevrimici makinede yeni is alarm vermiyor", d2);

        // --- istemci isi almis (dosya kuyrukta yok): null -> panel "Teslim edildi" der
        string d3 = Kuyruk.DurumMetni("EVOPC", "20260919_162609_555_FS~Alinmis.pdf");
        Kontrol(d3 == null, "istemcinin aldigi is 'kuyrukta' gorunmuyor", d3 ?? "null (dogru: panel 'Teslim edildi' gosterir)");

        // --- sunucuya HIC baglanmamis makine (yanlis ad / istemci kurulu degil)
        string is4 = "20260919_171500_002_FS.pdf";
        Directory.CreateDirectory(Path.Combine(kok, "YANLIS-PC"));
        File.WriteAllText(Path.Combine(Path.Combine(kok, "YANLIS-PC"), is4 + ".gz"), "x");
        string d4 = Kuyruk.DurumMetni("YANLIS-PC", is4);
        Kontrol(d4 != null && d4.StartsWith("BEKLIYOR") && d4.Contains("HIC baglanmadi"),
                "hic baglanmamis makine ayri bildiriliyor", d4);

        // --- uzun bekleme saat olarak yazilir
        string is5 = "20260919_090000_003_FS.pdf";
        string y5 = Path.Combine(Path.Combine(kok, "DESKTOP-7FRHC7B"), is5 + ".gz");
        File.WriteAllText(y5, "x");
        File.SetCreationTime(y5, DateTime.Now.AddMinutes(-135));
        string d5 = Kuyruk.DurumMetni("DESKTOP-7FRHC7B", is5);
        Kontrol(d5 != null && d5.Contains("2 sa 15 dk"), "uzun bekleme saat+dakika olarak yaziliyor", d5);

        // --- makine cevrimiciye donunce durum da degisir (onbellek temizlenince)
        kalp["DESKTOP-7FRHC7B"] = DateTime.Now;
        Kuyruk.OnbellegiTemizle();
        string d6 = Kuyruk.DurumMetni("DESKTOP-7FRHC7B", is1);
        Kontrol(d6 != null && d6.StartsWith("Kuyrukta"), "makine geri gelince alarm kalkiyor", d6);

        // --- bos/gecersiz girdiler cokertmiyor
        Kontrol(Kuyruk.DurumMetni("", is1) == null && Kuyruk.DurumMetni(null, null) == null,
                "bos makine/dosya adi guvenle null donuyor", "null");

        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        try { Directory.Delete(kok, true); } catch { }
        Environment.Exit(gecti == toplam ? 0 : 1);
    }
}
