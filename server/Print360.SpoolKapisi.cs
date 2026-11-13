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
    public class Is { public uint Id; public uint Durum; }

    static bool Bitmis(uint d) { return (d & (PRINTED | COMPLETE | DELETED | DELETING)) != 0; }

    // dosyaVar: onceki isin ciktisi henuz alinmadi. bizim: kapinin duraklattigi isler.
    // Kural: yaziciya giden ya da bekleyen EN FAZLA BIR is olur; o da ancak dosya
    // alinmissa. Digerleri duraklatilir, sirayla (eskiden yeniye) serbest birakilir.
    public static void Karar(IList<Is> isler, bool dosyaVar, ICollection<uint> bizim,
                             List<uint> duraklat, List<uint> surdur)
    {
        var canli = isler.Where(i => !Bitmis(i.Durum)).OrderBy(i => i.Id).ToList();
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

    public static List<string> PortaBagliYazicilar(string dosya)
    {
        var l = new List<string>();
        uint gerek, sayi;
        EnumPrinters(2 /*LOCAL*/, null, 5, IntPtr.Zero, 0, out gerek, out sayi);
        if (gerek == 0) return l;
        IntPtr t = Marshal.AllocHGlobal((int)gerek);
        try
        {
            if (!EnumPrinters(2, null, 5, t, gerek, out gerek, out sayi)) return l;
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

    public void Baslat()
    {
        is_ = new Thread(Dongu) { IsBackground = true, Name = "SpoolKapisi " + yazici };
        is_.Start();
    }

    void Dongu()
    {
        IntPtr h;
        if (!OpenPrinter(yazici, out h, IntPtr.Zero)) { gunluk("Spool kapisi: yazici acilamadi '" + yazici + "' (hata " + Marshal.GetLastWin32Error() + ")"); return; }
        // Ajan yeniden basladiysa onceki kapinin duraklattigi isler bizimdir
        try { foreach (var i in Isler(h)) if ((i.Durum & PAUSED) != 0) bizim.Add(i.Id); } catch { }
        IntPtr bildirim = FindFirstPrinterChangeNotification(h, PRINTER_CHANGE_JOB, 0, IntPtr.Zero);
        try
        {
            while (!dur)
            {
                try { Tur(h); } catch (Exception ex) { gunluk("Spool kapisi: " + ex.Message); }
                if (bildirim != IntPtr.Zero && bildirim.ToInt64() != -1)
                {
                    // Bildirim gelince hemen; gelmezse dosyanin alinmasini 200 ms'de bir yokla
                    if (WaitForSingleObject(bildirim, 200) == 0) { uint d; FindNextPrinterChangeNotification(bildirim, out d, IntPtr.Zero, IntPtr.Zero); }
                }
                else Thread.Sleep(200);
            }
        }
        finally
        {
            if (bildirim != IntPtr.Zero && bildirim.ToInt64() != -1) FindClosePrinterChangeNotification(bildirim);
            // Kapanista duraklattigimiz hicbir is sonsuza kadar beklemesin
            try { foreach (var id in bizim.ToList()) SetJob(h, id, 0, IntPtr.Zero, JOB_CONTROL_RESUME); } catch { }
            ClosePrinter(h);
        }
    }

    [DllImport("kernel32", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr h, uint ms);

    void Tur(IntPtr h)
    {
        var isler = Isler(h);
        bizim.IntersectWith(isler.Select(i => i.Id));                // biten isler unutulur
        var duraklat = new List<uint>(); var surdur = new List<uint>();
        Karar(isler, File.Exists(dosya), bizim, duraklat, surdur);
        foreach (var id in duraklat)
            if (SetJob(h, id, 0, IntPtr.Zero, JOB_CONTROL_PAUSE)) bizim.Add(id);
            else gunluk("Spool kapisi: is " + id + " bekletilemedi (hata " + Marshal.GetLastWin32Error() + ")");
        foreach (var id in surdur)
            if (SetJob(h, id, 0, IntPtr.Zero, JOB_CONTROL_RESUME)) bizim.Remove(id);
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

    List<Is> Isler(IntPtr h)
    {
        var l = new List<Is>();
        uint gerek, sayi;
        EnumJobs(h, 0, 512, 1, IntPtr.Zero, 0, out gerek, out sayi);
        if (gerek == 0) return l;
        IntPtr t = Marshal.AllocHGlobal((int)gerek);
        try
        {
            if (!EnumJobs(h, 0, 512, 1, t, gerek, out gerek, out sayi)) return l;
            int boy = Marshal.SizeOf(typeof(JOB_INFO_1));
            for (int i = 0; i < sayi; i++)
            {
                var j = (JOB_INFO_1)Marshal.PtrToStructure(new IntPtr(t.ToInt64() + (long)i * boy), typeof(JOB_INFO_1));
                l.Add(new Is { Id = j.JobId, Durum = j.Status });
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
