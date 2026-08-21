// ============================================================
//  Print360 - Urun kimligi
//
//  UCRETSIZ SURUM: Yazilim bedelsizdir, kullanim SINIRSIZDIR,
//  lisans anahtari GEREKMEZ, cikti limiti YOKTUR.
//  PARA ILE SATILAMAZ - ayrintilar icin bkz. LICENSE dosyasi.
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
        return "UCRETSIZ SURUM - sinirsiz kullanim, cikti limiti yok. "
             + "Para ile satilamaz. Gelistirici: " + Gelistirici + " (" + Eposta + ")";
    }
}
