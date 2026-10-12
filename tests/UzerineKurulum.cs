// REGRESYON TESTI: eski surumun UZERINE kurulum
//
// Her yeni surum, oncekinin biraktigi ve artik kullanilmayan ogeleri
// temizlemeli; bekleyen baski islerini ve kullanici verisini KORUMALI ve
// raporlamali. Eskiden kurulum yalnizca adini bildigi eski ogeleri siliyordu;
// listeye girmeyen her kalinti birikiyordu. Ustelik istemci kurulumu, ayni
// makinedeki SUNUCUNUN dosyalarini da siliyordu.
//
// URETIM KODUNU SINAR: setup\Print360.Setup.cs (Kurulum.KalintiTemizle,
// BekleyenRaporu, RunKalintiMi, GorevKalintiMi, KuralKalintiMi).
// Gercek bir sahte "C:\Print360" agaci gecici klasorde kurulup sinanir.
//   derleme: csc /main:UzerineKurulumTest tests\UzerineKurulum.cs setup\Print360.Setup.cs ...
using System;
using System.Collections.Generic;
using System.IO;

class UzerineKurulumTest
{
    static int gecti, toplam;
    static void Kontrol(bool ok, string ad, string ayrinti)
    {
        toplam++; if (ok) gecti++;
        Console.WriteLine((ok ? "GECTI " : "KALDI ") + ad);
        if (!ok && ayrinti != null) Console.WriteLine("       -> " + ayrinti);
    }

    static void Yaz(string kok, string yol, string icerik = "x")
    {
        string tam = Path.Combine(kok, yol);
        Directory.CreateDirectory(Path.GetDirectoryName(tam));
        File.WriteAllText(tam, icerik);
    }
    static bool Var(string kok, string yol) { return File.Exists(Path.Combine(kok, yol)) || Directory.Exists(Path.Combine(kok, yol)); }

    static int Main()
    {
        string kok = Path.Combine(Path.GetTempPath(), "p360-uzerine-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        try
        {
            SunucuUzerineKurulum(Path.Combine(kok, "sunucu"));
            IstemciAyniMakinede(Path.Combine(kok, "ikisi"));
            BekleyenIsler(Path.Combine(kok, "rapor"));
            SistemOgeleri();
        }
        finally { try { Directory.Delete(kok, true); } catch { } }
        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        return gecti == toplam ? 0 : 1;
    }

    // Eski bir sunucu kurulumu: guncel ikililer + eski surum kalintilari + kullanici verisi
    static void SunucuUzerineKurulum(string k)
    {
        // bu surumun ikilileri (silinip yenisi kopyalanacak)
        Yaz(k, "Print360.ServerAgent.exe"); Yaz(k, "Print360.Dashboard.exe"); Yaz(k, "Print360.Panel.exe");
        Yaz(k, "System.Data.SQLite.dll"); Yaz(k, "x64\\SQLite.Interop.dll");
        // eski surum kalintilari
        Yaz(k, "Print360.KeyGen.exe");                 // eski lisans araci
        Yaz(k, "DevExpress.Xpf.Core.v23.dll");          // eski arayuz kutuphanesi
        Yaz(k, "Print360.Dashboard.pdb");
        Yaz(k, "Print360.ClientAgent.exe.new");         // yarim kalmis kendini guncelleme
        Yaz(k, "update.cmd");
        Yaz(k, "Install-Server.ps1");                    // eski PowerShell kurulumu
        Yaz(k, "x64\\eski-yardimci.dll");
        Yaz(k, "license.key", "MUSTERI-LISANSI");        // veri olabilir: SILINMEZ, kenara
        Yaz(k, "agent.log", "eski gunluk");              // eski surum kokte gunluk tutuyordu
        Yaz(k, "DevExpress\\tema.dll");                  // eski klasor
        Yaz(k, "lisans\\musteri.txt", "veri");
        // kullanici verisi ve ayarlar
        string[] veri = { "db.ini", "mail.ini", "panel.pwd", "cert-thumbprint.txt", "rules.csv", "print360.db",
                          "Print360.ico", "kurulum-sunucu.txt" };
        foreach (var v in veri) Yaz(k, v, "VERI");
        Yaz(k, "queue\\PC1\\20261001_090000_001_ali~Fatura.pdf.gz");
        Yaz(k, "spool\\ali.pdf");
        Yaz(k, "archive\\2026-09\\eski.pdf.gz");
        Yaz(k, "logs\\dashboard.log");
        Yaz(k, "stats\\jobs.csv");
        Yaz(k, "queue-suresi-dolan\\PC9\\eski.pdf.gz");

        var r = Kurulum.KalintiTemizle(k, true, new DateTime(2026, 10, 1, 10, 0, 0));
        string kalinti = Path.Combine(k, "eski-surum-kalintilari\\20261001_100000");

        Kontrol(!Var(k, "Print360.ServerAgent.exe") && !Var(k, "Print360.Dashboard.exe") && !Var(k, "System.Data.SQLite.dll"),
                "bu surumun ikilileri kaldirildi (yenisi kopyalanacak)", null);
        Kontrol(!Var(k, "Print360.KeyGen.exe") && !Var(k, "DevExpress.Xpf.Core.v23.dll") && !Var(k, "Print360.Dashboard.pdb")
             && !Var(k, "Print360.ClientAgent.exe.new") && !Var(k, "update.cmd") && !Var(k, "Install-Server.ps1"),
                "ADI BILINMEYEN eski ikili/betik kalintilari silindi", string.Join(" | ", r.ToArray()));
        Kontrol(!Var(k, "x64\\eski-yardimci.dll") && Var(k, "x64\\SQLite.Interop.dll"),
                "x64\\ icindeki eski dosya silindi, SQLite cekirdegi korundu (uzerine yazilacak)", null);
        Kontrol(!Var(k, "license.key") && File.Exists(Path.Combine(kalinti, "license.key"))
             && File.ReadAllText(Path.Combine(kalinti, "license.key")) == "MUSTERI-LISANSI",
                "bilinmeyen VERI dosyasi silinmedi, kenara alindi", null);
        Kontrol(File.Exists(Path.Combine(kalinti, "agent.log")) && Directory.Exists(Path.Combine(kalinti, "DevExpress"))
             && File.Exists(Path.Combine(kalinti, "lisans\\musteri.txt")),
                "bilinmeyen klasorler icerigiyle birlikte kenara alindi", null);
        bool veriTamam = true;
        foreach (var v in veri) if (!File.Exists(Path.Combine(k, v)) || File.ReadAllText(Path.Combine(k, v)) != "VERI") veriTamam = false;
        Kontrol(veriTamam, "ayarlar ve veritabani DOKUNULMADAN yerinde", null);
        Kontrol(Var(k, "queue\\PC1\\20261001_090000_001_ali~Fatura.pdf.gz") && Var(k, "spool\\ali.pdf")
             && Var(k, "archive\\2026-09\\eski.pdf.gz") && Var(k, "logs\\dashboard.log") && Var(k, "stats\\jobs.csv")
             && Var(k, "queue-suresi-dolan\\PC9\\eski.pdf.gz"),
                "bekleyen isler, spool, arsiv, gunluk ve istatistik korundu", null);
        Kontrol(r.Exists(x => x.StartsWith("KENARA ALINDI") && x.Contains("license.key"))
             && r.Exists(x => x.StartsWith("SILINDI") && x.Contains("Print360.KeyGen.exe")),
                "yapilan her islem satir satir raporlandi", null);

        // ikinci kurulum: yapacak is yok, kenara alinanlara dokunulmaz
        var r2 = Kurulum.KalintiTemizle(k, true, new DateTime(2026, 10, 2, 10, 0, 0));
        Kontrol(r2.Count == 0 && File.Exists(Path.Combine(kalinti, "license.key")),
                "tekrar kurulumda yeni kalinti yok, kenara alinanlar yerinde", string.Join(" | ", r2.ToArray()));
    }

    // Ayni makinede sunucu + istemci: istemci kurulumu SUNUCUNUN dosyalarini silmemeli
    static void IstemciAyniMakinede(string k)
    {
        Yaz(k, "Print360.ServerAgent.exe"); Yaz(k, "Print360.Dashboard.exe"); Yaz(k, "Print360.Panel.exe");
        Yaz(k, "System.Data.SQLite.dll");
        Yaz(k, "Print360.ClientAgent.exe"); Yaz(k, "Print360.VC.dll"); Yaz(k, "SumatraPDF.exe");
        Yaz(k, "Print360.ini", "VERI"); Yaz(k, "kimlik.txt", "VERI"); Yaz(k, "SumatraPDF-settings.txt", "VERI");
        Yaz(k, "jobs\\yazdirilacak.pdf"); Yaz(k, "vc-outbox\\a.msg"); Yaz(k, "sumatrapdfcache\\x.dat");

        var r = Kurulum.KalintiTemizle(k, false, DateTime.Now);
        Kontrol(Var(k, "Print360.ServerAgent.exe") && Var(k, "Print360.Dashboard.exe") && Var(k, "Print360.Panel.exe")
             && Var(k, "System.Data.SQLite.dll"),
                "istemci kurulumu ayni makinedeki SUNUCU dosyalarini silmiyor (eski hata)", string.Join(" | ", r.ToArray()));
        Kontrol(!Var(k, "Print360.ClientAgent.exe") && !Var(k, "Print360.VC.dll") && Var(k, "SumatraPDF.exe"),
                "istemcinin kendi ikilileri yenileniyor, SumatraPDF (16 MB) korunuyor", null);
        Kontrol(Var(k, "Print360.ini") && Var(k, "kimlik.txt") && Var(k, "SumatraPDF-settings.txt") && Var(k, "jobs\\yazdirilacak.pdf")
             && Var(k, "vc-outbox\\a.msg") && Var(k, "sumatrapdfcache\\x.dat") && !Var(k, "eski-surum-kalintilari"),
                "istemci verisi korundu, gereksiz kalinti klasoru olusmadi", null);
    }

    static void BekleyenIsler(string k)
    {
        var simdi = new DateTime(2026, 10, 1, 12, 0, 0);
        Action<string, DateTime> is_ = (yol, t) => { Yaz(k, yol); File.SetCreationTime(Path.Combine(k, yol), t); };
        is_("queue\\DESKTOP-7FRHC7B\\20260821_193128_109_FS~Belgeyi Yazdir.pdf.gz", new DateTime(2026, 8, 21, 19, 31, 0));
        is_("queue\\DESKTOP-7FRHC7B\\20260919_164250_935_FS.pdf.gz", new DateTime(2026, 9, 19, 16, 42, 0));
        is_("queue\\EVOPC\\20261001_115000_001_FS.pdf.gz", new DateTime(2026, 10, 1, 11, 50, 0));
        is_("queue\\EVOPC\\_sorunlu\\20260930_100000_001_FS.pdf.gz", new DateTime(2026, 9, 30, 10, 0, 0));
        Directory.CreateDirectory(Path.Combine(k, "queue\\BOS-PC"));
        Yaz(k, "spool\\FS.pdf");

        var r = Kurulum.BekleyenRaporu(k, true, simdi);
        string hepsi = string.Join("\n", r.ToArray());
        Kontrol(hepsi.Contains("BEKLEYEN 3 IS KORUNDU (2 makine, en eskisi 2026-08-21)"), "toplam, makine sayisi, en eski tarih", hepsi);
        Kontrol(hepsi.Contains("DESKTOP-7FRHC7B: 2 is") && hepsi.Contains("EVOPC: 1 is") && !hepsi.Contains("BOS-PC"),
                "makine bazinda dokum (bos klasor yazilmaz)", hepsi);
        Kontrol(hepsi.Contains("2 is 7 gunden eski") && hepsi.Contains("Yeniden gonder"),
                "eski islerin akibeti aciklaniyor (silinmez, yeniden gonderilebilir)", hepsi);
        Kontrol(hepsi.Contains("Karantinada 1 is") && hepsi.Contains("SPOOL'DA 1") && hepsi.Contains("FS.pdf"),
                "karantina ve spool da raporlaniyor", hepsi);

        string ik = Path.Combine(k, "istemci");
        Yaz(ik, "jobs\\a.pdf"); Yaz(ik, "jobs\\b.pdf"); Yaz(ik, "jobs\\yarim.pdf.tmp"); Yaz(ik, "failed\\c.pdf");
        string ir = string.Join("\n", Kurulum.BekleyenRaporu(ik, false, simdi).ToArray());
        Kontrol(ir.Contains("YAZDIRILMAMIS 2 IS") && ir.Contains("1 is"), "istemcide bekleyen ve basilamamis isler", ir);
        Kontrol(Kurulum.BekleyenRaporu(Path.Combine(k, "yok"), true, simdi)[0].Contains("bekleyen is yok"),
                "bos kurulumda 'bekleyen is yok'", null);
    }

    static void SistemOgeleri()
    {
        Kontrol(Kurulum.RunKalintiMi("Print360", @"C:\Print360\Print360.ServerAgent.exe")
             && Kurulum.RunKalintiMi("Print360Agent", "x")
             && Kurulum.RunKalintiMi("EskiAjan", @"""C:\Print360\Print360.Agent.exe"""),
                "Run: eski adlar ve C:\\Print360'a isaret eden bilinmeyen kayitlar kalinti", null);
        Kontrol(!Kurulum.RunKalintiMi("Print360ServerAgent", @"C:\Print360\x.exe") && !Kurulum.RunKalintiMi("Print360ClientAgent", "")
             && !Kurulum.RunKalintiMi("Print360Dashboard", "") && !Kurulum.RunKalintiMi("OneDrive", @"C:\Program Files\OneDrive.exe")
             && !Kurulum.RunKalintiMi("Baska", @"C:\Program Files\Print360Server\x.exe"),
                "Run: gecerli kayitlar ve baska programlar korunur", null);
        Kontrol(Kurulum.GorevKalintiMi(@"\Print360 Ajan") && Kurulum.GorevKalintiMi(@"\Print360\Eski Gorev")
             && !Kurulum.GorevKalintiMi(@"\Print360 Yazdirma Ajani") && !Kurulum.GorevKalintiMi(@"\Print360 Panel Sunucusu")
             && !Kurulum.GorevKalintiMi(@"\Microsoft\Windows\Defrag\ScheduledDefrag") && !Kurulum.GorevKalintiMi(""),
                "zamanlanmis gorev: eskiler kalinti, guncel ve baska gorevler korunur", null);
        Kontrol(Kurulum.KuralKalintiMi("Print360 Dashboard", "8360", "8443") && Kurulum.KuralKalintiMi("Print360 Dashboard HTTPS", "8360", "8443")
             && Kurulum.KuralKalintiMi("Print360 9443", "8360", "8443")
             && !Kurulum.KuralKalintiMi("Print360 8443", "8360", "8443") && !Kurulum.KuralKalintiMi("Remote Desktop", "8360", "8443"),
                "guvenlik duvari: eski PowerShell kurallari ve eski port kurallari kalinti", null);

        // Guncelleme sonrasi: masaustu olan hangi oturumda ajan YOK?
        var eksik = Kurulum.AjanEksikOturumlar(new[] { 0, 2, 3, 3, 5 }, new[] { 2, 5 });
        Kontrol(eksik.Count == 1 && eksik[0] == 3, "ajani olmayan kullanici oturumu bulunuyor (oturum 0 haric)",
                string.Join(",", eksik.ConvertAll(x => x.ToString()).ToArray()));
        Kontrol(Kurulum.AjanEksikOturumlar(new[] { 2 }, new[] { 2 }).Count == 0, "hepsinde ajan varsa eksik yok", null);
    }
}
