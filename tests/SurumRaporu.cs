// REGRESYON TESTI: /api/surum karar mantigi
//
// Sahada "kurdum" denilen sunucu haftalarca eski surumde kaldi ve bunu
// uzaktan anlamanin yolu yoktu. /api/surum, calisan panelin, diskteki
// bilesenlerin ve son kurulumun durumunu raporlar. Bu test, raporun
// her senaryoda DOGRU teshis koydugunu sinar.
//
// URETIM KODUNU SINAR: server\Print360.Dashboard.cs ile birlikte derlenir
// (Dashboard.SurumRaporuUret - dosyaya dokunmayan saf karar mantigi).
//   derleme: csc /main:SurumTest tests\SurumRaporu.cs server\Print360.Dashboard.cs ...
using System;

class SurumTest
{
    static int gecti, toplam;
    static void Kontrol(bool ok, string ad, string json)
    {
        toplam++; if (ok) gecti++;
        Console.WriteLine((ok ? "GECTI " : "KALDI ") + ad);
        if (!ok) Console.WriteLine("       -> " + json);
    }

    static bool Var(string json, string parca) { return json.IndexOf(parca, StringComparison.Ordinal) >= 0; }

    static int Main()
    {
        const string Y = "2026.09.30.0838", E = "2026.09.01.1254";
        string ok = Y + "|2026-09-30 09:00:00|sunucu|OK|";

        string j = Dashboard.SurumRaporuUret(Y, Y, Y, Y, Y, ok);
        Kontrol(Var(j, "\"tutarli\":true") && Var(j, "Tum bilesenler " + Y), "her sey guncel -> tutarli", j);

        // Sahadaki en sinsi durum: dosyalar yeni, calisan servis eski
        j = Dashboard.SurumRaporuUret(E, Y, Y, Y, Y, ok);
        Kontrol(Var(j, "\"tutarli\":false") && Var(j, "panel yeniden baslatilmamis") && Var(j, "schtasks /Run"),
                "dosyalar yeni ama CALISAN panel eski -> gorevi yeniden baslatma komutu veriliyor", j);

        // Kurulum update\ exe'yi guncelleyemedi
        j = Dashboard.SurumRaporuUret(Y, Y, Y, Y, E, ok);
        Kontrol(Var(j, "\"tutarli\":false") && Var(j, "farkli surumlerde") && Var(j, "istemci " + E),
                "bir bilesen eski kaldi -> hangisi oldugu soyleniyor", j);

        // Kurulum HATA ile bitti
        j = Dashboard.SurumRaporuUret(Y, Y, Y, Y, Y, Y + "|2026-09-30 09:00:00|sunucu|HATA|Erisim engellendi");
        Kontrol(Var(j, "\"tutarli\":false") && Var(j, "HATA ile bitti: Erisim engellendi"), "kurulum HATA -> ayrinti gosteriliyor", j);

        // Uyarili kurulum tutarli sayilir ama belirtilir
        j = Dashboard.SurumRaporuUret(Y, Y, Y, Y, Y, Y + "|2026-09-30 09:00:00|sunucu|UYARI|1 uyari: port");
        Kontrol(Var(j, "\"tutarli\":true") && Var(j, "uyarilarla bitti"), "uyarili kurulum -> tutarli ama belirtiliyor", j);

        // Dosyalar yok (kurulum eksik / farkli klasor) -> "tutarli" DEMEMELI
        j = Dashboard.SurumRaporuUret(Y, "", "", "", "", null);
        Kontrol(Var(j, "\"tutarli\":false") && Var(j, "okunamadi") && Var(j, "kayit yok"),
                "dosyalar okunamiyor -> yanlislikla 'tutarli' denmiyor", j);

        // Ayrinti icinde tirnak / ters bolu / satir sonu JSON'u bozmamali
        j = Dashboard.SurumRaporuUret(Y, Y, Y, Y, Y, Y + "|t|sunucu|HATA|C:\\yol \"tirnak\"\r\nyeni satir");
        bool jsonTemiz = !j.Contains("\r") && !j.Contains("\n") && Var(j, "C:\\\\yol \\\"tirnak\\\"");
        Kontrol(jsonTemiz, "ozel karakterler JSON icinde kacisli", j);

        // --- Tani sayfasi: oturum listesi (WTSEnumerateSessions P/Invoke)
        // Yapi (struct) hizalamasi yanlis olursa 64-bit'te cop deger okunur ya da
        // surec coker. Bu surecin KENDI oturumu listede, dogru kullaniciyla olmali.
        var ot = Dashboard.OturumListesi();
        int benimOturum = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        var ben = ot.Find(o => o[0] == benimOturum.ToString());
        string kullanici = Environment.UserName;
        bool otOk = ben != null && (ben[1].Equals(kullanici, StringComparison.OrdinalIgnoreCase) || benimOturum == 0);
        Kontrol(otOk, "oturum listesi bu sureci dogru kullaniciyla buluyor (" + ot.Count + " oturum)",
                ben == null ? "oturum " + benimOturum + " listede yok" : string.Join(" | ", ben));

        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        return gecti == toplam ? 0 : 1;
    }
}
