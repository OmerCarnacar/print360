// REGRESYON TESTI: gonderilen spool dosyasi silinemezse is IKI KEZ gonderilmemeli.
//
// URETIM KODUNU SINAR: server\Print360.ServerAgent.cs (+ Db, Ortak, License,
// Version, VChannel) ile birlikte derlenir; ozel uyeler yansimayla cagrilir.
//   derleme: csc /main:SunucuAjaniTest tests\SunucuAjani.cs server\Print360.ServerAgent.cs ...
//
// Eskiden: is kuyruga yazildiktan SONRA duz File.Delete(spool) yapiliyordu.
// Antivirus dosyayi o an tariyorsa istisna -> dosya yerinde kaliyor -> 2 sn sonra
// AYNI is yeniden gonderiliyor, cikti iki kez basiliyordu.
using System;
using System.IO;
using System.Reflection;

class SunucuAjaniTest
{
    static int gecti, toplam;
    const BindingFlags OZEL = BindingFlags.NonPublic | BindingFlags.Static;

    static void Kontrol(bool ok, string ad, string ayrinti)
    {
        toplam++; if (ok) gecti++;
        Console.WriteLine((ok ? "GECTI " : "KALDI ") + ad);
        if (!ok && ayrinti != null) Console.WriteLine("       -> " + ayrinti);
    }

    static object Cagir(string ad, params object[] a) { return typeof(ServerAgent).GetMethod(ad, OZEL).Invoke(null, a); }
    static void Ayarla(string ad, object d) { typeof(ServerAgent).GetField(ad, OZEL).SetValue(null, d); }
    static object Oku(string ad) { return typeof(ServerAgent).GetField(ad, OZEL).GetValue(null); }

    static int Main()
    {
        string kok = Path.Combine(Path.GetTempPath(), "p360-ajan-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        string spool = Path.Combine(kok, "spool");
        Directory.CreateDirectory(spool);
        Directory.CreateDirectory(Path.Combine(kok, "logs"));
        Ayarla("spoolDir", spool);
        Ayarla("logFile", Path.Combine(kok, "logs", "server.log"));
        Ayarla("jobsCsv", Path.Combine(kok, "jobs.csv"));
        Ayarla("rulesCsv", Path.Combine(kok, "rules.csv"));
        Ayarla("connLog", Path.Combine(kok, "logs", "connections.log"));
        Db.DbIniYolu = Path.Combine(kok, "db.ini");
        Db.SqliteDosya = Path.Combine(kok, "print360.db");
        Db.UyariCsv = Path.Combine(kok, "alerts.csv");
        Kuyruk.Kok = Path.Combine(kok, "queue");
        try
        {
            Console.WriteLine("--- 1. Serbest dosya: silinir");
            string a = Path.Combine(spool, "ayse.pdf");
            File.WriteAllText(a, "%PDF-1.4 ... %%EOF");
            Kontrol((bool)Cagir("SpoolSil", a) && !File.Exists(a), "gonderilen spool dosyasi silindi", null);

            Console.WriteLine("--- 2. Dosya kilitli (antivirus taramasi): ne silinir ne tasinir");
            File.WriteAllText(a, "%PDF-1.4 ilk is %%EOF");
            string imza;
            using (new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                bool silindi = (bool)Cagir("SpoolSil", a);
                Kontrol(!silindi && File.Exists(a), "kilitliyken silinemedigi DOGRU raporlandi (istisna firlatilmadi)", null);
                imza = (string)Cagir("SpoolImzasi", a);
                Ayarla("gonderilmisSpool", imza);
                // Ana dongu 2 sn sonra ayni dosyayi gorur: YENIDEN GONDERMEMELI
                Cagir("SpoolKontrol", "", a);
                Kontrol(Directory.Exists(Kuyruk.Kok) == false || Directory.GetFiles(Kuyruk.Kok, "*", SearchOption.AllDirectories).Length == 0,
                        "ayni is YENIDEN gonderilmedi (kuyruk bos)", null);
                Kontrol((string)Oku("gonderilmisSpool") == imza, "dosya hala 'gonderildi' olarak hatirlaniyor", null);
            }

            Console.WriteLine("--- 3. Kilit kalkinca: ana dongu dosyayi siler, yeniden gondermez");
            Cagir("SpoolKontrol", "", a);
            Kontrol(!File.Exists(a) && Oku("gonderilmisSpool") == null, "kilit kalkinca silindi, hatirlatma temizlendi", null);

            Console.WriteLine("--- 4. Ayni dosyaya YENI is yazildi: eski imzayla karistirilmaz");
            File.WriteAllText(a, "%PDF-1.4 ilk is %%EOF");
            Ayarla("gonderilmisSpool", (string)Cagir("SpoolImzasi", a));
            System.Threading.Thread.Sleep(50);
            File.WriteAllText(a, "%PDF-1.4 ikinci, daha uzun bir is %%EOF");
            File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddSeconds(5));
            Kontrol((string)Cagir("SpoolImzasi", a) != (string)Oku("gonderilmisSpool"),
                    "yeni is farkli imza tasir (gonderilmis sayilip atlanmaz)", null);
        }
        catch (Exception ex) { Kontrol(false, "BEKLENMEYEN ISTISNA", ex.ToString()); }
        finally { try { Directory.Delete(kok, true); } catch { } }
        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        return gecti == toplam ? 0 : 1;
    }
}
