// ============================================================
//  Print360 - Urun kimligi
//
//  UCRETSIZ ve ACIK KAYNAK: kullanim SINIRSIZDIR, lisans anahtari
//  GEREKMEZ, cikti limiti YOKTUR. Lisans: GNU GPL v3 veya sonrasi
//  (GPL-3.0-or-later) - ayrintilar icin bkz. LICENSE ve NOTICE.
//
//  Gelistirici : Omer CARNACAR
//  Iletisim    : omer.carnacar@outlook.com.tr
//
//  Not: Bu dosya eskiden RSA imzali lisans anahtari dogrulamasi ve
//  deneme surumu sayaci iceriyordu. Urun ucretsize donusturuldugunde
//  bu mekanizmalar etkisiz hale getirilmis ama kod olarak birakilmisti;
//  kaynagi okuyanda "cikti limiti var" izlenimi biraktigi icin tamamen
//  kaldirildi. Geriye yalnizca urun kimligi kaldi.
// ============================================================

static class Lisans
{
    public const string Gelistirici = "Omer CARNACAR";
    public const string Eposta      = "omer.carnacar@outlook.com.tr";
    public const string LinkedIn    = "https://www.linkedin.com/in/omercarnacar/";

    public static string DurumMetni()
    {
        return "UCRETSIZ ve ACIK KAYNAK (GPL-3.0) - sinirsiz kullanim, cikti limiti yok. "
             + "Gelistirici: " + Gelistirici + " (" + Eposta + ")";
    }
}
