// REGRESYON TESTI: RDP kanal eklentisi (vc\Print360.VC.dll) RDP istemcisini cokertmemeli.
//
// Eklenti mstsc.exe'nin ICINE yuklenir; oradaki bir bellek hatasi uzak masaustunu cokertir.
// Bu test SAHTE BIR RDP ISTEMCISIDIR: DLL'i yukler, VirtualChannelEntry'yi gercek istemcinin
// yaptigi gibi cagirir ve sanal kanal sozlesmesinin kurallarini denetler:
//   - giris noktasi yapisi donusten sonra gecersizdir (eklenti kopyalamali)
//   - VirtualChannelWrite'a verilen tampon WRITE_COMPLETE'e kadar yasamali
//   - ayni surecte birden fazla baglanti birbirini bozmamali
//   - bozuk / dev mesajlar cokertmemeli
// Eklenti PRINT360_VC_KOK ile gecici klasore yonlendirilir; gercek C:\Print360'a dokunulmaz.
//   derleme: csc tests\VcEklentisi.cs      calistirma: VcEklentisi.exe [dll yolu]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class VcEklentisiTest
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string yol);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr h, string ad);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EntryFn(IntPtr ep);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint InitFn(IntPtr ppInit, IntPtr kanal, int adet, uint surum, IntPtr initOlay);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint OpenFn(IntPtr init, IntPtr pOpen, IntPtr ad, IntPtr openOlay);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint CloseFn(uint open);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint WriteFn(uint open, IntPtr veri, uint uzunluk, IntPtr kullanici);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void InitOlayFn(IntPtr init, uint olay, IntPtr veri, uint uzunluk);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void OpenOlayFn(uint open, uint olay, IntPtr veri, uint uzunluk, uint toplam, uint bayrak);

    const uint CONNECTED = 1, DISCONNECTED = 3, TERMINATED = 4, DATA = 10, WRITE_COMPLETE = 11, WRITE_CANCELLED = 12;
    const uint FIRST = 1, LAST = 2;

    static int gecti, toplam;
    static void Kontrol(bool ok, string ad, string ayrinti = null)
    {
        toplam++; if (ok) gecti++;
        Console.WriteLine((ok ? "GECTI " : "KALDI ") + ad);
        if (!ok && ayrinti != null) Console.WriteLine("       -> " + ayrinti);
    }

    // ---- sahte istemcinin durumu ----
    static readonly object kilit = new object();
    static IntPtr sonrakiInit; static uint sonrakiOpen;
    static readonly Dictionary<long, InitOlayFn> initOlay = new Dictionary<long, InitOlayFn>();
    static readonly Dictionary<uint, OpenOlayFn> openOlay = new Dictionary<uint, OpenOlayFn>();
    class Yazma { public uint Open; public IntPtr P; public int N; public byte Ilk; }
    static readonly List<Yazma> yazmalar = new List<Yazma>();
    static readonly List<uint> kapananlar = new List<uint>();
    static string acilanAd = "";
    static bool tuzak;

    static uint Init(IntPtr ppInit, IntPtr kanal, int adet, uint surum, IntPtr olay)
    {
        Marshal.WriteIntPtr(ppInit, sonrakiInit);
        lock (kilit) initOlay[sonrakiInit.ToInt64()] = (InitOlayFn)Marshal.GetDelegateForFunctionPointer(olay, typeof(InitOlayFn));
        return 0;
    }
    static uint Open(IntPtr init, IntPtr pOpen, IntPtr ad, IntPtr olay)
    {
        acilanAd = Marshal.PtrToStringAnsi(ad);
        Marshal.WriteInt32(pOpen, (int)sonrakiOpen);
        lock (kilit) openOlay[sonrakiOpen] = (OpenOlayFn)Marshal.GetDelegateForFunctionPointer(olay, typeof(OpenOlayFn));
        return 0;
    }
    static uint Close(uint open) { lock (kilit) kapananlar.Add(open); return 0; }
    static uint Write(uint open, IntPtr veri, uint n, IntPtr kullanici)
    {
        // Gercek istemci gibi: veriyi HEMEN kopyalamaz, gostericiyi saklar
        lock (kilit) yazmalar.Add(new Yazma { Open = open, P = veri, N = (int)n, Ilk = Marshal.ReadByte(veri) });
        return veri == kullanici ? 0u : 99u;
    }
    static uint TuzakInit(IntPtr a, IntPtr b, int c, uint d, IntPtr e) { tuzak = true; return 1; }
    static uint TuzakOpen(IntPtr a, IntPtr b, IntPtr c, IntPtr d) { tuzak = true; return 1; }
    static uint TuzakClose(uint a) { tuzak = true; return 1; }
    static uint TuzakWrite(uint a, IntPtr b, uint c, IntPtr d) { tuzak = true; return 1; }

    // GC toplamasin
    static readonly InitFn dInit = Init; static readonly OpenFn dOpen = Open; static readonly CloseFn dClose = Close; static readonly WriteFn dWrite = Write;
    static readonly InitFn tInit = TuzakInit; static readonly OpenFn tOpen = TuzakOpen; static readonly CloseFn tClose = TuzakClose; static readonly WriteFn tWrite = TuzakWrite;

    static void YapiYaz(IntPtr ep, Delegate i, Delegate o, Delegate c, Delegate w)
    {
        Marshal.WriteInt32(ep, 0, 40); Marshal.WriteInt32(ep, 4, 1);
        Marshal.WriteIntPtr(ep, 8, Marshal.GetFunctionPointerForDelegate(i));
        Marshal.WriteIntPtr(ep, 16, Marshal.GetFunctionPointerForDelegate(o));
        Marshal.WriteIntPtr(ep, 24, Marshal.GetFunctionPointerForDelegate(c));
        Marshal.WriteIntPtr(ep, 32, Marshal.GetFunctionPointerForDelegate(w));
    }

    static byte[] Cerceve(byte tip, uint isId, uint blok, byte[] veri)
    {
        var f = new byte[20 + veri.Length];
        f[0] = (byte)'P'; f[1] = (byte)'3'; f[2] = (byte)'6'; f[3] = (byte)'0'; f[4] = 2; f[5] = tip;
        BitConverter.GetBytes(isId).CopyTo(f, 8); BitConverter.GetBytes(blok).CopyTo(f, 12);
        BitConverter.GetBytes((uint)veri.Length).CopyTo(f, 16); veri.CopyTo(f, 20);
        return f;
    }
    static byte[] Basla(uint isId, int boyut, string ad)
    {
        var a = Encoding.UTF8.GetBytes(ad);
        return Cerceve(1, isId, 0, BitConverter.GetBytes((uint)boyut).Concat(BitConverter.GetBytes((uint)a.Length)).Concat(a).ToArray());
    }

    // Cerceveyi istemcinin yaptigi gibi 1600 baytlik parcalarla verir; her parca cagridan hemen sonra serbest kalir
    static void Ver(uint open, byte[] f, uint? toplamBildir = null)
    {
        const int PARCA = 1600;
        for (int o = 0; o < f.Length || o == 0; o += PARCA)
        {
            int n = Math.Min(PARCA, f.Length - o);
            IntPtr p = Marshal.AllocHGlobal(Math.Max(n, 1));
            Marshal.Copy(f, o, p, n);
            uint b = (o == 0 ? FIRST : 0) | (o + n >= f.Length ? LAST : 0);
            openOlay[open](open, DATA, p, (uint)n, toplamBildir ?? (uint)f.Length, b);
            for (int i = 0; i < n; i++) Marshal.WriteByte(p, i, 0xEE);   // eklenti gostericiyi sakladiysa bozuk veri okur
            Marshal.FreeHGlobal(p);
            if (f.Length == 0) break;
        }
    }

    static List<byte[]> IsCerceveleri(uint isId, byte[] veri, string ad)
    {
        var l = new List<byte[]> { Basla(isId, veri.Length, ad) };
        uint blok = 0;
        for (int o = 0; o < veri.Length; o += 30000, blok++)
            l.Add(Cerceve(2, isId, blok, veri.Skip(o).Take(30000).ToArray()));
        l.Add(Cerceve(3, isId, 0, BitConverter.GetBytes(blok)));
        return l;
    }

    static byte[] Rastgele(int n, int tohum) { var b = new byte[n]; new Random(tohum).NextBytes(b); return b; }
    static bool Ayni(string yol, byte[] veri) { return File.Exists(yol) && File.ReadAllBytes(yol).SequenceEqual(veri); }
    static bool Bekle(Func<bool> kosul, int ms = 6000) { var t = DateTime.Now; while ((DateTime.Now - t).TotalMilliseconds < ms) { if (kosul()) return true; Thread.Sleep(100); } return kosul(); }
    static bool Saglam(Yazma y) { for (int i = 0; i < y.N; i++) if (Marshal.ReadByte(y.P, i) != y.Ilk) return false; return true; }
    static void Mesaj(string outbox, string ad, byte dolgu, int n = 64)
    {
        File.WriteAllBytes(Path.Combine(outbox, ad + ".tmp"), Enumerable.Repeat(dolgu, n).ToArray());
        File.Move(Path.Combine(outbox, ad + ".tmp"), Path.Combine(outbox, ad + ".msg"));
    }

    static int Main(string[] a)
    {
        string dll = Path.GetFullPath(a.Length > 0 ? a[0] : Path.Combine("vc", "Print360.VC.dll"));
        if (!File.Exists(dll) || IntPtr.Size != 8) { Console.WriteLine("ATLA   eklenti yok ya da surec 64-bit degil: " + dll); Console.WriteLine("Sonuc: 0/0"); return 0; }
        string kok = Path.Combine(Path.GetTempPath(), "p360-vc-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        string jobs = Path.Combine(kok, "jobs"), outbox = Path.Combine(kok, "vc-outbox"), isaret = Path.Combine(outbox, ".vc-aktif");
        Directory.CreateDirectory(kok);
        Environment.SetEnvironmentVariable("PRINT360_VC_KOK", kok);
        const string GERCEK = @"C:\Print360\vc-outbox\.vc-aktif";
        string gercekOnce = File.Exists(GERCEK) ? File.GetLastWriteTimeUtc(GERCEK).Ticks.ToString() : "-";

        IntPtr h = LoadLibrary(dll);
        IntPtr giris = h == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(h, "VirtualChannelEntry");
        Kontrol(giris != IntPtr.Zero, "eklenti yuklendi, VirtualChannelEntry bulundu", dll);
        if (giris == IntPtr.Zero) { Console.WriteLine("Sonuc: " + gecti + "/" + toplam); return 1; }
        var entry = (EntryFn)Marshal.GetDelegateForFunctionPointer(giris, typeof(EntryFn));

        Console.WriteLine("--- 1. Giris noktasi yapisi donusten sonra gecersizdir");
        IntPtr ep = Marshal.AllocHGlobal(40);
        YapiYaz(ep, dInit, dOpen, dClose, dWrite);
        sonrakiInit = new IntPtr(0x1000);
        bool kayit = entry(ep) != 0;
        YapiYaz(ep, tInit, tOpen, tClose, tWrite);   // yapi artik "gecersiz": kullanan tuzaga duser
        Kontrol(kayit && initOlay.ContainsKey(0x1000), "kanal kaydedildi");
        sonrakiOpen = 7;
        initOlay[0x1000](new IntPtr(0x1000), CONNECTED, IntPtr.Zero, 0);
        Kontrol(!tuzak, "giris noktalari KOPYALANMIS (gecersiz yapi kullanilmadi)");
        if (tuzak || !File.Exists(isaret))
        {
            Kontrol(false, "kanal acildi ve isaret dosyasi TEST klasorunde", "eklenti test klasorunu kullanmiyor; gercek veriye dokunmamak icin test durduruldu");
            Console.WriteLine("Sonuc: " + gecti + "/" + toplam); return 1;
        }
        Kontrol(acilanAd == "P360" && openOlay.ContainsKey(7), "P360 kanali acildi, isaret dosyasi test klasorunde");

        Console.WriteLine("--- 2. Is alma (Turkce ad, 1600 baytlik parcalar)");
        var v1 = Rastgele(100000, 1);
        foreach (var f in IsCerceveleri(1, v1, "20261109_ayse~İş Şubat Çıktısı.pdf")) Ver(7, f);
        Kontrol(Ayni(Path.Combine(jobs, "20261109_ayse~İş Şubat Çıktısı.pdf"), v1), "is dosyasi bayt bayt ayni, Turkce ad bozulmadi",
                string.Join(" | ", Directory.Exists(jobs) ? Directory.GetFiles(jobs).Select(Path.GetFileName).ToArray() : new string[0]));
        Kontrol(Directory.GetFiles(jobs, "*.part").Length == 0, "yarim dosya kalmadi");

        Console.WriteLine("--- 3. Yazma tamponu WRITE_COMPLETE'e kadar yasamali (cokme sebebi)");
        Mesaj(outbox, "a1", 0xA1); Mesaj(outbox, "a2", 0xB2);
        Kontrol(Bekle(() => { lock (kilit) return yazmalar.Count >= 2; }), "iki mesaj kanala yazildi");
        Kontrol(Bekle(() => Directory.GetFiles(outbox, "*.msg").Length == 0, 2000), "gonderilen mesaj dosyalari silindi");
        Mesaj(outbox, "a3", 0xC3);   // tamamlanmamis ikisi varken ucuncu: serbest birakilmis bellek yeniden kullanilirdi
        Kontrol(Bekle(() => { lock (kilit) return yazmalar.Count >= 3; }), "ucuncu mesaj yazildi");
        Thread.Sleep(300);
        List<Yazma> y; lock (kilit) y = yazmalar.ToList();
        Kontrol(y.Count == 3 && y.Select(x => x.P).Distinct().Count() == 3, "her yazma AYRI tamponda (tampon erken serbest birakilip yeniden kullanilmadi)");
        Kontrol(y.All(Saglam) && y.Select(x => x.Ilk).OrderBy(x => x).SequenceEqual(new byte[] { 0xA1, 0xB2, 0xC3 }),
                "tamamlanma bildirilene kadar tamponlarin icerigi BOZULMADI", string.Join(",", y.Select(x => x.Ilk.ToString("X2") + (Saglam(x) ? "" : "!")).ToArray()));
        foreach (var x in y) openOlay[7](7, WRITE_COMPLETE, x.P, 0, 0, 0);
        openOlay[7](7, WRITE_COMPLETE, y[0].P, 0, 0, 0);             // ayni tamamlanma iki kez
        IntPtr yabanci = Marshal.AllocHGlobal(64);
        openOlay[7](7, WRITE_CANCELLED, yabanci, 0, 0, 0);            // eklentinin olmayan gosterici
        openOlay[7](7, WRITE_COMPLETE, IntPtr.Zero, 0, 0, 0);
        Marshal.WriteByte(yabanci, 1); Marshal.FreeHGlobal(yabanci);
        Kontrol(true, "tamamlanma, cift tamamlanma ve yabanci gosterici cokertmedi");
        lock (kilit) yazmalar.Clear();

        Console.WriteLine("--- 4. Bozuk ve dev mesajlar");
        Ver(7, new byte[100], 0x7FFFFFF0);                            // 2 GB oldugunu soyleyen mesaj
        var bozuk = Cerceve(1, 9, 0, BitConverter.GetBytes(10u).Concat(BitConverter.GetBytes(0xFFFFFFFCu)).Concat(new byte[8]).ToArray());
        Ver(7, bozuk);                                                // ad uzunlugu tasiyor
        Ver(7, new byte[5]); Ver(7, Encoding.ASCII.GetBytes("P360 ama cerceve degil........"));
        var uzun = Cerceve(2, 1, 0, new byte[10]); BitConverter.GetBytes(0xFFFFFFF0u).CopyTo(uzun, 16); Ver(7, uzun);
        Kontrol(Directory.GetFiles(jobs).Length == 1, "bozuk mesajlar dosya olusturmadi, cokertmedi", Directory.GetFiles(jobs).Length + " dosya");
        var v2 = Rastgele(45000, 2);
        foreach (var f in IsCerceveleri(2, v2, "ikinci.pdf")) Ver(7, f);
        Kontrol(Ayni(Path.Combine(jobs, "ikinci.pdf"), v2), "bozuk mesajlardan sonra normal is alindi");
        var fazla = IsCerceveleri(3, Rastgele(100, 3), "fazla.pdf"); fazla[0] = Basla(3, 50, "fazla.pdf");
        foreach (var f in fazla) Ver(7, f);
        Kontrol(!File.Exists(Path.Combine(jobs, "fazla.pdf")) && Directory.GetFiles(jobs, "*.part").Length == 0, "bildirilenden fazla veri gonderen is reddedildi");

        Console.WriteLine("--- 5. Ayni surecte ikinci RDP baglantisi (sekmeli RDP yoneticileri)");
        IntPtr ep2 = Marshal.AllocHGlobal(40);
        YapiYaz(ep2, dInit, dOpen, dClose, dWrite);
        sonrakiInit = new IntPtr(0x2000);
        bool kayit2 = entry(ep2) != 0;
        YapiYaz(ep2, tInit, tOpen, tClose, tWrite);
        sonrakiOpen = 9;
        initOlay[0x2000](new IntPtr(0x2000), CONNECTED, IntPtr.Zero, 0);
        Kontrol(kayit2 && openOlay.ContainsKey(9) && !tuzak, "ikinci baglanti kanalini acti");
        var vA = Rastgele(90000, 4); var vB = Rastgele(70000, 5);
        var cA = IsCerceveleri(10, vA, "A-birinci-baglanti.pdf"); var cB = IsCerceveleri(10, vB, "B-ikinci-baglanti.pdf");
        Ver(7, cA[0]); Ver(7, cA[1]);
        foreach (var f in cB) Ver(9, f);                              // A yarimken B'nin tamami
        for (int i = 2; i < cA.Count; i++) Ver(7, cA[i]);
        Kontrol(Ayni(Path.Combine(jobs, "A-birinci-baglanti.pdf"), vA) && Ayni(Path.Combine(jobs, "B-ikinci-baglanti.pdf"), vB),
                "ic ice gelen iki baglantinin isleri birbirini BOZMADI");
        Mesaj(outbox, "b1", 0xD4);
        Kontrol(Bekle(() => { lock (kilit) return yazmalar.Count == 1 && yazmalar[0].Open == 9; }), "onay en son acilan baglantiya yazildi");

        Console.WriteLine("--- 6. Baglanti kopmasi ve yeniden baglanma");
        Ver(9, Basla(20, 60000, "yarim.pdf")); Ver(9, Cerceve(2, 20, 0, new byte[30000]));
        Kontrol(File.Exists(Path.Combine(jobs, "yarim.pdf.part")), "is suruyor (.part var)");
        initOlay[0x2000](new IntPtr(0x2000), DISCONNECTED, IntPtr.Zero, 0);
        Kontrol(!File.Exists(Path.Combine(jobs, "yarim.pdf.part")) && !File.Exists(Path.Combine(jobs, "yarim.pdf")), "kopunca yarim is silindi (basilmaz)");
        Kontrol(kapananlar.Contains(9u) && File.Exists(isaret), "kopan kanal kapatildi; diger baglanti acik oldugu icin isaret duruyor");
        lock (kilit) { foreach (var x in yazmalar) openOlay[9](9, WRITE_CANCELLED, x.P, 0, 0, 0); yazmalar.Clear(); }
        Mesaj(outbox, "b2", 0xE5);
        Kontrol(Bekle(() => { lock (kilit) return yazmalar.Count == 1 && yazmalar[0].Open == 7; }), "onay artik acik kalan baglantiya gidiyor");
        initOlay[0x1000](new IntPtr(0x1000), DISCONNECTED, IntPtr.Zero, 0);
        Kontrol(!File.Exists(isaret), "son baglanti da kopunca isaret silindi (istemci HTTPS'e doner)");
        lock (kilit) yazmalar.Clear();
        Mesaj(outbox, "b3", 0xF6);
        Thread.Sleep(2500);
        Kontrol(yazmalar.Count == 0 && File.Exists(Path.Combine(outbox, "b3.msg")), "kanal yokken mesaj yazilmadi, dosya bekliyor");
        sonrakiOpen = 7;
        initOlay[0x1000](new IntPtr(0x1000), CONNECTED, IntPtr.Zero, 0);
        Kontrol(File.Exists(isaret) && Bekle(() => { lock (kilit) return yazmalar.Count == 1; }), "yeniden baglaninca isaret geri geldi, bekleyen mesaj gonderildi");
        var v3 = Rastgele(31000, 6);
        foreach (var f in IsCerceveleri(30, v3, "yeniden.pdf")) Ver(7, f);
        Kontrol(Ayni(Path.Combine(jobs, "yeniden.pdf"), v3), "yeniden baglandiktan sonra is alindi");

        Console.WriteLine("--- 7. Kapanis");
        initOlay[0x1000](new IntPtr(0x1000), TERMINATED, IntPtr.Zero, 0);
        initOlay[0x2000](new IntPtr(0x2000), TERMINATED, IntPtr.Zero, 0);
        initOlay[0x2000](new IntPtr(0x2000), DISCONNECTED, IntPtr.Zero, 0);
        Thread.Sleep(1500);
        Kontrol(!File.Exists(isaret), "kapanis olaylari cokertmedi, isaret silindi");
        string gercekSonra = File.Exists(GERCEK) ? File.GetLastWriteTimeUtc(GERCEK).Ticks.ToString() : "-";
        Kontrol(gercekOnce == gercekSonra, @"gercek C:\Print360 klasorune DOKUNULMADI");
        string gunluk = Path.Combine(kok, "logs", "vc-eklenti.log");
        Kontrol(File.Exists(gunluk) && File.ReadAllText(gunluk).Contains("kanal acildi"), "eklenti tani gunlugu yaziyor");

        try { Directory.Delete(kok, true); } catch { }
        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        return gecti == toplam ? 0 : 1;
    }
}
