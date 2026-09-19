// REGRESYON TESTI: kuyruk korumalari (zehirli is karantinasi + sure siniri)
//
// URETIMDEKI Kuyruk sinifini (server/Print360.Db.cs) dogrudan derler.
//
// ZEHIRLI IS: bir is ust uste verilip hic onaylanmiyorsa kuyrugun basini tikar
// ve arkasindaki butun isler bekler ("ilk cikti geliyor, devami gelmiyor").
// Sahada bu belirti bir ay surdu; sebebi her seferinde farkliydi. Karantina,
// SEBEBI BILINMEYEN gelecekteki hatalara karsi genel sigortadir.
//
//   derleme: csc /r:System.Data.dll /r:<System.Data.SQLite.dll>
//            tests\KuyrukKorumalari.cs server\Print360.Db.cs
using System;
using System.IO;
using System.Linq;

class KuyrukKorumalari
{
    static int gecti, toplam;
    static void Kontrol(bool ok, string ad, string ayrinti)
    {
        toplam++; if (ok) gecti++;
        Console.WriteLine((ok ? "GECTI " : "KALDI ") + ad);
        if (ayrinti != null) Console.WriteLine("       -> " + ayrinti);
    }

    static DateTime saat = new DateTime(2026, 9, 19, 12, 0, 0);

    // Sunucunun is secme dongusuyle AYNI: en eski, karantinaya alinmamis isi ver.
    static string IsVer(string qDir)
    {
        foreach (var f in new DirectoryInfo(qDir).GetFiles("*.gz").OrderBy(x => x.CreationTimeUtc))
        {
            string k;
            if (Kuyruk.SorunluysaAyir(f.FullName, out k)) continue;
            return f.Name;
        }
        return null;
    }

    static void Main()
    {
        string kok = Path.Combine(Path.GetTempPath(), "p360-test-korumalar");
        if (Directory.Exists(kok)) Directory.Delete(kok, true);
        Kuyruk.Kok = kok;
        Kuyruk.SuresiDolanKok = Path.Combine(kok, "_suresi-dolan-test");
        Kuyruk.Simdi = delegate { return saat; };

        string q = Path.Combine(kok, "EVOPC");
        Directory.CreateDirectory(q);
        string zehirli = Path.Combine(q, "20260919_100000_001_FS~Bozuk.pdf.gz");
        string saglam  = Path.Combine(q, "20260919_100500_002_FS~Saglam.pdf.gz");
        File.WriteAllText(zehirli, "x"); File.SetCreationTimeUtc(zehirli, DateTime.UtcNow.AddMinutes(-30));
        File.WriteAllText(saglam,  "x"); File.SetCreationTimeUtc(saglam,  DateTime.UtcNow.AddMinutes(-25));

        // --- 1) Dengesiz ag: 20 deneme ama yalnizca 1 dakika icinde -> KARANTINA YOK
        string son = null;
        for (int i = 0; i < 20; i++) { son = IsVer(q); saat = saat.AddSeconds(3); }
        Kontrol(File.Exists(zehirli) && son == Path.GetFileName(zehirli),
                "kisa surede cok deneme saglam isi karantinaya ALMIYOR (dengesiz ag)",
                "20 deneme / 60 sn -> is hala kuyrukta");

        // --- 2) 3 dakika gecti, is hala onaylanmiyor -> karantina, SIRADAKI is verilir
        saat = saat.AddMinutes(3);
        son = IsVer(q);
        Kontrol(!File.Exists(zehirli) && File.Exists(Path.Combine(Path.Combine(q, "_sorunlu"), Path.GetFileName(zehirli))),
                "onaylanmayan is kenara alindi (silinmedi)", "_sorunlu\\" + Path.GetFileName(zehirli));
        Kontrol(son == Path.GetFileName(saglam), "kuyruk TIKANMADI: arkadaki is verildi", son);

        // --- 3) Onaylanan isin sayaci sifirlanir: ayni adla gelen yeni is eskinin gunahini tasimaz
        for (int i = 0; i < 6; i++) IsVer(q);
        Kuyruk.OnaylandiSay(saglam);
        saat = saat.AddMinutes(10);
        son = IsVer(q);
        Kontrol(son == Path.GetFileName(saglam) && File.Exists(saglam),
                "onay sayaci sifirliyor (onaylanan is karantinaya girmez)", son);

        // --- 4) Sure siniri: 7 gunden eski is teslim edilmez, ayri klasore alinir
        Kuyruk.SureSaat = 168;
        string eski = Path.Combine(q, "20260901_090000_003_FS~Eski Gizli Belge.pdf.gz");
        string yeni = Path.Combine(q, "20260919_115900_004_FS~Yeni.pdf.gz");
        File.WriteAllText(eski, "x"); File.SetCreationTime(eski, DateTime.Now.AddDays(-18));
        File.WriteAllText(yeni, "x");
        var dolan = Kuyruk.SuresiDolanlariAyir();
        Kontrol(dolan.Count == 1 && !File.Exists(eski)
                && File.Exists(Path.Combine(Path.Combine(Kuyruk.SuresiDolanKok, "EVOPC"), Path.GetFileName(eski))),
                "18 gunluk is kuyruktan cikarildi (silinmedi)", dolan.Count > 0 ? dolan[0] : "(yok)");
        Kontrol(File.Exists(yeni) && File.Exists(saglam), "suresi dolmamis islere dokunulmadi", null);

        // --- 5) Karantinadaki ve suresi dolan isler bir daha VERILMEZ / sayilmaz
        int dk;
        Kontrol(!Kuyruk.Bekliyor("EVOPC", "20260919_100000_001_FS~Bozuk.pdf", out dk),
                "karantinadaki is 'kuyrukta bekliyor' sayilmiyor", null);

        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        try { Directory.Delete(kok, true); } catch { }
        Environment.Exit(gecti == toplam ? 0 : 1);
    }
}
