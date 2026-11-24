// ============================================================
//  Print360 - SPOOL KAPISI
//  Gelistirici : Omer CARNACAR
//  Lisans      : GPL-3.0-or-later, acik kaynak (bkz. LICENSE, NOTICE)
//
//  Sanal yazicinin portu kullanici basina TEK bir dosyadir (spool\<kullanici>.pdf).
//  Windows her isi bu dosyanin UZERINE yazar. Arka arkaya gelen iste (toplu fatura,
//  coklu belge) ajan onceki dosyayi almadan sonraki is onu siliyordu: OLCULDU, uc
//  isten yalnizca sonuncusu kaliyordu.
//
//  Kapi: onceki cikti alinmadan sonraki isin yaziciya gecmesine izin vermez. Kullanici
//  kendi isini duraklatabilir (yonetici yetkisi gerekmez). Kuyruga yeni is eklenince
//  bildirim gelir; is bekletilir, onceki dosya alininca sirayla serbest birakilir.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

class SpoolKapisi : IDisposable
{
    // ---- winspool ----
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool OpenPrinter(string ad, out IntPtr h, IntPtr varsayilan);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool ClosePrinter(IntPtr h);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool EnumJobs(IntPtr h, uint ilk, uint adet, uint seviye, IntPtr tampon, uint boyut, out uint gerek, out uint sayi);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetJob(IntPtr h, uint isId, uint seviye, IntPtr is_, uint komut);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern IntPtr FindFirstPrinterChangeNotification(IntPtr h, uint filtre, uint secenek, IntPtr bilgi);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool FindNextPrinterChangeNotification(IntPtr bildirim, out uint degisim, IntPtr secenek, IntPtr bilgi);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool FindClosePrinterChangeNotification(IntPtr bildirim);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct JOB_INFO_1
    {
        public uint JobId;
        public string pPrinterName, pMachineName, pUserName, pDocument, pDatatype, pStatus;
        public uint Status, Priority, Position, TotalPages, PagesPrinted;
        public short Yil, Ay, HaftaGunu, Gun, Saat, Dakika, Saniye, Milisaniye;
    }

    public const uint PAUSED = 0x1, DELETING = 0x4, PRINTING = 0x10, PRINTED = 0x80, DELETED = 0x100, COMPLETE = 0x1000;
    const uint JOB_CONTROL_PAUSE = 1, JOB_CONTROL_RESUME = 2;
    const uint PRINTER_CHANGE_JOB = 0x0000FF00;

    // ---- karar (saf, test edilir) ----
    // Sira: kuyruktaki konum (JOB_INFO_1.Position). Is numaralari bir sinira gelince
    // basa doner (65000 -> 3); siralamayi numaraya gore yapmak o anda YENI isi once
    // gonderiyordu. Konum bilinmiyorsa (0) numaraya bakilir.
    public class Is { public uint Id; public uint Durum; public uint Sira; }

    static bool Bitmis(uint d) { return (d & (PRINTED | COMPLETE | DELETED | DELETING)) != 0; }

    // dosyaVar: onceki isin ciktisi henuz alinmadi. bizim: kapinin duraklattigi isler.
    // Kural: yaziciya giden ya da bekleyen EN FAZLA BIR is olur; o da ancak dosya
    // alinmissa. Digerleri duraklatilir, sirayla (eskiden yeniye) serbest birakilir.
    public static void Karar(IList<Is> isler, bool dosyaVar, ICollection<uint> bizim,
                             List<uint> duraklat, List<uint> surdur)
    {
        var canli = isler.Where(i => !Bitmis(i.Durum)).OrderBy(i => i.Sira == 0 ? uint.MaxValue : i.Sira).ThenBy(i => i.Id).ToList();
        bool basiliyor = canli.Any(i => (i.Durum & PRINTING) != 0);
        var serbest = canli.Where(i => (i.Durum & (PAUSED | PRINTING)) == 0).ToList();

        int izin = (dosyaVar || basiliyor) ? 0 : 1;   // yaziciya gecebilecek is sayisi
        foreach (var i in serbest.Skip(izin)) duraklat.Add(i.Id);
        if (izin == 1 && serbest.Count == 0)
        {
            var sira = canli.FirstOrDefault(i => (i.Durum & PAUSED) != 0 && bizim.Contains(i.Id));
            if (sira != null) surdur.Add(sira.Id);
        }
    }

    // ---- calisan kapi ----
    readonly string yazici, dosya;
    readonly Action<string> gunluk, ayrinti;
    readonly HashSet<uint> bizim = new HashSet<uint>();
    volatile bool dur;
    Thread is_;
    int sonBekleyen = -1;
    DateTime bekleyenBaslangic = DateTime.MaxValue;
    bool takilmaBildirildi;
    public static TimeSpan TakilmaSuresi = TimeSpan.FromMinutes(2);

    // gunluk: her zaman yazilan (takilma uyarisi); ayrinti: hata ayiklama modunda yazilan
    public SpoolKapisi(string yazici, string dosya, Action<string> gunluk, Action<string> ayrinti = null)
    {
        this.yazici = yazici; this.dosya = dosya;
        this.gunluk = gunluk ?? delegate { }; this.ayrinti = ayrinti ?? delegate { };
    }

    // Bu dosyaya yazan (portu bu dosya olan) yazicilar
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool EnumPrinters(uint bayrak, string ad, uint seviye, IntPtr tampon, uint boyut, out uint gerek, out uint sayi);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PRINTER_INFO_5 { public string pPrinterName, pPortName; public uint Attributes, DeviceNotSelectedTimeout, TransmissionRetryTimeout; }

    // null: yazicilar okunamadi (bos liste ile karistirilmasin: cagiran kapilari kapatmasin)
    public static List<string> PortaBagliYazicilar(string dosya)
    {
        var l = new List<string>();
        uint gerek, sayi;
        if (!EnumPrinters(2 /*LOCAL*/, null, 5, IntPtr.Zero, 0, out gerek, out sayi) && Marshal.GetLastWin32Error() != 122) return null;
        if (gerek == 0) return l;
        IntPtr t = Marshal.AllocHGlobal((int)gerek);
        try
        {
            if (!EnumPrinters(2, null, 5, t, gerek, out gerek, out sayi)) return null;
            int boy = Marshal.SizeOf(typeof(PRINTER_INFO_5));
            for (int i = 0; i < sayi; i++)
            {
                var p = (PRINTER_INFO_5)Marshal.PtrToStructure(new IntPtr(t.ToInt64() + (long)i * boy), typeof(PRINTER_INFO_5));
                if (string.Equals((p.pPortName ?? "").Trim(), dosya, StringComparison.OrdinalIgnoreCase)) l.Add(p.pPrinterName);
            }
        }
        finally { Marshal.FreeHGlobal(t); }
        return l;
    }

    // ---- kapinin duraklattigi islerin kaydi ----
    // Ajan cokerse/oldurulurse duraklattigi isler beklemede kalir; yeni ajan bunlari
    // serbest birakmali. Eskiden TUM duraklatilmis isler sahiplenilirdi: kullanicinin
    // KENDI duraklattigi is de kendiliginden basiliyordu. Artik yalniz kayittakiler.
    public static string KayitYolu(string dosya) { return dosya + ".kapi"; }

    public static HashSet<uint> KayitOku(string yol)
    {
        var l = new HashSet<uint>();
        try
        {
            if (!File.Exists(yol)) return l;
            foreach (var x in File.ReadAllText(yol).Split(new[] { ',', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            { uint id; if (uint.TryParse(x, out id)) l.Add(id); }
        }
        catch { }
        return l;
    }

    public static void KayitYaz(string yol, ICollection<uint> ids)
    {
        try
        {
            if (ids.Count == 0) { if (File.Exists(yol)) File.Delete(yol); return; }
            File.WriteAllText(yol, string.Join(",", ids.OrderBy(i => i)));
        }
        catch { }
    }

    // Acilista sahiplenilecek isler: hala duraklatilmis VE kayitta olanlar
    public static List<uint> Sahiplen(IList<Is> isler, ICollection<uint> kayitli)
    {
        return isler.Where(i => (i.Durum & PAUSED) != 0 && !Bitmis(i.Durum) && kayitli.Contains(i.Id)).Select(i => i.Id).ToList();
    }

    public void Baslat()
    {
        is_ = new Thread(Dongu) { IsBackground = true, Name = "SpoolKapisi " + yazici };
        is_.Start();
    }

    // Iplik yalnizca Dispose ile biter. Yazici acilamazsa ya da tutamac bozulursa
    // (spooler yeniden basladi, yazici silinip kuruldu) bekleyip YENIDEN acar. Eskiden
    // ilk hatada iplik sessizce oluyordu ve kapi bir daha kurulmuyordu.
    public static TimeSpan YenidenDeneme = TimeSpan.FromSeconds(10);
    public bool Calisiyor { get { return is_ != null && is_.IsAlive; } }
    public int TurSayisi;          // test: bos beklemede donguyu sayar (CPU yakmamali)
    string sonHata;

    // Ayni hata 200 ms'de bir gunluge yazilmasin: degisince yaz
    void HataBildir(string m) { if (m == sonHata) return; sonHata = m; gunluk(m); }

    void Bekle(TimeSpan t)
    {
        var son = DateTime.UtcNow + t;
        while (!dur && DateTime.UtcNow < son) Thread.Sleep(50);
    }

    void Dongu()
    {
        string kayit = KayitYolu(dosya);
        while (!dur)
        {
            IntPtr h;
            if (!OpenPrinter(yazici, out h, IntPtr.Zero))
            {
                HataBildir("Spool kapisi: yazici acilamadi '" + yazici + "' (hata " + Marshal.GetLastWin32Error() + "); "
                         + (int)YenidenDeneme.TotalSeconds + " sn sonra yeniden denenecek");
                Bekle(YenidenDeneme);
                continue;
            }
            try { Calis(h, kayit); }
            catch (Exception ex) { HataBildir("Spool kapisi: " + ex.Message); }
            finally { ClosePrinter(h); }
            if (!dur) Bekle(YenidenDeneme);
        }
    }

    static bool Gecerli(IntPtr b) { return b != IntPtr.Zero && b.ToInt64() != -1; }

    void Calis(IntPtr h, string kayit)
    {
        var ilk = Isler(h);
        if (ilk != null) foreach (var id in Sahiplen(ilk, KayitOku(kayit))) bizim.Add(id);
        IntPtr bildirim = FindFirstPrinterChangeNotification(h, PRINTER_CHANGE_JOB, 0, IntPtr.Zero);
        bool bildirimVar = Gecerli(bildirim);
        int ardisikHata = 0;
        try
        {
            while (!dur)
            {
                TurSayisi++;
                var isler = Isler(h);
                if (isler == null)
                {
                    if (++ardisikHata >= 5)
                    {
                        HataBildir("Spool kapisi: '" + yazici + "' is listesi okunamiyor (hata " + sonEnumHata + "); yazici yeniden aciliyor");
                        return;
                    }
                }
                else
                {
                    ardisikHata = 0;
                    try { Tur(h, isler, kayit); sonHata = null; }
                    catch (Exception ex) { HataBildir("Spool kapisi: " + ex.Message); }
                }
                if (bildirimVar)
                {
                    // Bildirim gelince hemen; gelmezse dosyanin alinmasini 200 ms'de bir yokla.
                    // Tutamac bozulursa (WAIT_FAILED ya da FindNext basarisiz) bekleme ANINDA
                    // donerdi: dongu %100 CPU yakiyordu. Artik bildirim birakilir, yoklamaya gecilir.
                    uint w = WaitForSingleObject(bildirim, 200);
                    uint d;
                    if (w == 0 ? !FindNextPrinterChangeNotification(bildirim, out d, IntPtr.Zero, IntPtr.Zero) : w != 0x102 /*TIMEOUT*/)
                    {
                        FindClosePrinterChangeNotification(bildirim);
                        bildirimVar = false;
                    }
                }
                else Thread.Sleep(200);
            }
        }
        finally
        {
            if (bildirimVar) FindClosePrinterChangeNotification(bildirim);
            if (dur)
            {
                // Kapanista duraklattigimiz hicbir is sonsuza kadar beklemesin
                foreach (var id in bizim.ToList())
                    try { if (SetJob(h, id, 0, IntPtr.Zero, JOB_CONTROL_RESUME)) bizim.Remove(id); } catch { }
                KayitYaz(kayit, bizim);
            }
        }
    }

    [DllImport("kernel32", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr h, uint ms);

    string sonKayit = "";
    readonly HashSet<uint> uyarilan = new HashSet<uint>();
    void Tur(IntPtr h, List<Is> isler, string kayit)
    {
        bizim.IntersectWith(isler.Select(i => i.Id));                // biten isler unutulur
        uyarilan.IntersectWith(isler.Select(i => i.Id));
        var duraklat = new List<uint>(); var surdur = new List<uint>();
        Karar(isler, File.Exists(dosya), bizim, duraklat, surdur);
        foreach (var id in duraklat)
            if (SetJob(h, id, 0, IntPtr.Zero, JOB_CONTROL_PAUSE)) bizim.Add(id);
            else
            {
                int e = Marshal.GetLastWin32Error();
                // Her turda (200 ms) ayni is icin yeniden yazilmasin: is basina bir kez
                if (uyarilan.Add(id)) gunluk("Spool kapisi: is " + id + " bekletilemedi (hata " + e + "); bu is siraya alinmadan gidebilir");
            }
        foreach (var id in surdur)
            if (SetJob(h, id, 0, IntPtr.Zero, JOB_CONTROL_RESUME)) bizim.Remove(id);
        string k = string.Join(",", bizim.OrderBy(i => i));
        if (k != sonKayit) { KayitYaz(kayit, bizim); sonKayit = k; }
        int bekleyen = bizim.Count;
        if (bekleyen != sonBekleyen && (bekleyen > 0 || sonBekleyen > 0))
            ayrinti(bekleyen > 0 ? "Spool kapisi: " + bekleyen + " is sirada bekliyor (onceki cikti alinana kadar)" : "Spool kapisi: sira bosaldi");
        sonBekleyen = bekleyen;
        // Normalde bekleme saniyelerdir. Uzarsa onceki cikti alinamiyor demektir: kullanici bilsin.
        if (bekleyen == 0) { bekleyenBaslangic = DateTime.MaxValue; takilmaBildirildi = false; }
        else
        {
            if (bekleyenBaslangic == DateTime.MaxValue) bekleyenBaslangic = DateTime.Now;
            if (!takilmaBildirildi && DateTime.Now - bekleyenBaslangic >= TakilmaSuresi)
            {
                takilmaBildirildi = true;
                gunluk("UYARI: SPOOL KAPISI - '" + yazici + "' yazicisinda " + bekleyen + " is " + (int)(DateTime.Now - bekleyenBaslangic).TotalMinutes
                     + " dakikadir bekliyor: onceki cikti (" + Path.GetFileName(dosya) + ") alinamadi. Isler SILINMEDI; onceki cikti alininca sirayla gidecek.");
            }
        }
    }

    int sonEnumHata;
    // null: okunamadi (tutamac bozuk olabilir). Bos liste: kuyruk bos.
    List<Is> Isler(IntPtr h)
    {
        var l = new List<Is>();
        uint gerek, sayi;
        if (!EnumJobs(h, 0, 512, 1, IntPtr.Zero, 0, out gerek, out sayi))
        {
            int e = Marshal.GetLastWin32Error();
            if (e != 122 /*INSUFFICIENT_BUFFER*/) { sonEnumHata = e; return null; }
        }
        if (gerek == 0) return l;
        IntPtr t = Marshal.AllocHGlobal((int)gerek);
        try
        {
            if (!EnumJobs(h, 0, 512, 1, t, gerek, out gerek, out sayi)) { sonEnumHata = Marshal.GetLastWin32Error(); return null; }
            int boy = Marshal.SizeOf(typeof(JOB_INFO_1));
            for (int i = 0; i < sayi; i++)
            {
                var j = (JOB_INFO_1)Marshal.PtrToStructure(new IntPtr(t.ToInt64() + (long)i * boy), typeof(JOB_INFO_1));
                l.Add(new Is { Id = j.JobId, Durum = j.Status, Sira = j.Position });
            }
        }
        finally { Marshal.FreeHGlobal(t); }
        return l;
    }

    public void Dispose()
    {
        dur = true;
        if (is_ != null) is_.Join(3000);
    }
}
