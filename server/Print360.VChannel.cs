// ============================================================
//  Print360 - RDP Yazdirma ve Yonetim Cozumu
//  Gelistirici : Omer CARNACAR  <omer.carnacar@outlook.com.tr>
//  LinkedIn    : https://www.linkedin.com/in/omercarnacar/
//  Lisans      : UCRETSIZ SURUM - para ile satilamaz (bkz. LICENSE)
//  Telif       : (c) 2026 Omer CARNACAR
// ============================================================
// ============================================================
//  Print360 - Sunucu tarafi RDP Virtual Channel gonderimi (REFERANS)
//
//  ServerAgent, isi WTS sanal kanalindan ("P360") istemciye gonderir.
//  Istemci PC'deki Print360.VC.dll (mstsc eklentisi) bunu alip
//  C:\Print360\jobs\<ad>'e yazar; mevcut ClientAgent basar.
//
//  ENTEGRASYON (ServerAgent.Dispatch icinde, HTTPS kuyruk kanalindan ONCE):
//     if (pdf != null && VChannel.Gonder(name, pdf))  kanal = "VirtualChannel";
//     else { ... mevcut HTTPS kuyruk / tsclient akisi ... }
//  Boylece: RDP sanal kanali varsa oradan (ayar/port/firewall gerekmez),
//  yoksa mevcut HTTPS kuyruguna duser. Geriye tam uyumlu.
//
//  NOT: Bu kod bu makinede DERLENDI (P/Invoke) ancak gercek bir RDS
//  oturumu olmadan UCTAN UCA test edilemez. WTSVirtualChannelOpen, kanal
//  yoksa (istemci eklentisi kurulu degil / RDP disi) IntPtr.Zero doner
//  ve Gonder() false verir -> sistem HTTPS kanalina duser.
// ============================================================
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

static class VChannel
{
    const string ADI = "P360";
    static readonly IntPtr WTS_CURRENT_SERVER = IntPtr.Zero;
    const int WTS_CURRENT_SESSION = -1;

    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern IntPtr WTSVirtualChannelOpen(IntPtr hServer, int SessionId,
        [MarshalAs(UnmanagedType.LPStr)] string pVirtualName);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSVirtualChannelWrite(IntPtr hChannel, byte[] Buffer, uint Length, out uint pBytesWritten);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSVirtualChannelRead(IntPtr hChannel, uint TimeOut, byte[] Buffer, uint BufferSize, out uint pBytesRead);

    [DllImport("wtsapi32.dll")]
    static extern bool WTSVirtualChannelClose(IntPtr hChannel);

    // ---- PROTOKOL v2: uygulama seviyesinde parcalama ----
    // SVC'de tek mesaj boyutu sinirlidir; PDF bloklara bolunup her blok AYRI
    // kanal mesaji olarak gonderilir. Boylece dosya boyutu sinirsizdir.
    // Cerceve: [sihir"P360"][surum=2][tip][rezerv:2][isId:4][blokNo:4][uzn:4][veri]
    const int BASLIK = 20;
    public const int CERCEVE_VERI = 30000;   // blok basina ham PDF baytı (guvenli sinir)
    const byte TIP_BASLA = 1, TIP_VERI = 2, TIP_BITTI = 3;
    static int _isSayac;

    static byte[] Cerceve(byte tip, int isId, int blokNo, byte[] veri, int ofs, int uzn)
    {
        var f = new byte[BASLIK + uzn];
        f[0] = (byte)'P'; f[1] = (byte)'3'; f[2] = (byte)'6'; f[3] = (byte)'0';
        f[4] = 2; f[5] = tip;                       // surum, tip
        Buffer.BlockCopy(BitConverter.GetBytes(isId), 0, f, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(blokNo), 0, f, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(uzn), 0, f, 16, 4);
        if (uzn > 0) Buffer.BlockCopy(veri, ofs, f, BASLIK, uzn);
        return f;
    }

    // Bir isin TUM cercevelerini uretir (test edilebilir olsun diye ayri metot).
    public static System.Collections.Generic.List<byte[]> Cerceveler(string dosyaAdi, byte[] pdf, int isId)
    {
        var liste = new System.Collections.Generic.List<byte[]>();
        byte[] ad = Encoding.UTF8.GetBytes(dosyaAdi);
        // BASLA: [toplamBoyut:4][adUzunlugu:4][ad]
        var basla = new byte[8 + ad.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(pdf.Length), 0, basla, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(ad.Length), 0, basla, 4, 4);
        Buffer.BlockCopy(ad, 0, basla, 8, ad.Length);
        liste.Add(Cerceve(TIP_BASLA, isId, 0, basla, 0, basla.Length));
        // VERI blokları
        int blok = 0;
        for (int ofs = 0; ofs < pdf.Length; ofs += CERCEVE_VERI, blok++)
        {
            int n = Math.Min(CERCEVE_VERI, pdf.Length - ofs);
            liste.Add(Cerceve(TIP_VERI, isId, blok, pdf, ofs, n));
        }
        // BITTI: [blokSayisi:4]
        liste.Add(Cerceve(TIP_BITTI, isId, 0, BitConverter.GetBytes(blok), 0, 4));
        return liste;
    }

    // Isi RDP sanal kanalindan istemciye gonderir.
    // Donus: true = gonderildi; false = kanal yok (eklenti kurulu degil / RDP disi).
    public static bool Gonder(string dosyaAdi, byte[] pdf)
    {
        IntPtr h = WTSVirtualChannelOpen(WTS_CURRENT_SERVER, WTS_CURRENT_SESSION, ADI);
        if (h == IntPtr.Zero) return false;
        try
        {
            int isId = System.Threading.Interlocked.Increment(ref _isSayac);
            foreach (var f in Cerceveler(dosyaAdi, pdf, isId))
            {
                uint yazilan;
                if (!WTSVirtualChannelWrite(h, f, (uint)f.Length, out yazilan) || yazilan != f.Length)
                    return false;   // kanal koptu: cagiran HTTPS kuyruguna duser
            }
            return true;
        }
        catch { return false; }
        finally { WTSVirtualChannelClose(h); }
    }

    // ---- TERS YON: istemciden gelen onay/sayac/heartbeat mesajlarini dinle ----
    // Istemci eklentisi (Print360.VC.dll) vc-outbox'taki .msg dosyalarini kanala
    // yazar; sunucu burada okur. Ters protokol (istemci->sunucu):
    //   [turUzunlugu:4 LE][tur UTF-8][veriUzunlugu:4 LE][veri UTF-8]
    //   tur: "ONAY" | "SAYAC" | "HB"   veri: serbest (JSON benzeri "k=v;..." metni)
    // Kanal cift yonlu ayni ADI ("P360") uzerinden calisir.
    static volatile bool _dinleAcik = false;

    // Arka planda kanali acik tutup gelen mesajlari isler.
    // isle(tur, veri) geri cagrisi ServerAgent tarafinda SQL'e yazar.
    public static void DinlemeyeBasla(Action<string, string> isle)
    {
        if (_dinleAcik) return;
        _dinleAcik = true;
        var t = new System.Threading.Thread(() => DinleDongu(isle));
        t.IsBackground = true;
        t.Start();
    }

    public static void DinlemeyiDurdur() { _dinleAcik = false; }

    static void DinleDongu(Action<string, string> isle)
    {
        while (_dinleAcik)
        {
            IntPtr h = WTSVirtualChannelOpen(WTS_CURRENT_SERVER, WTS_CURRENT_SESSION, ADI);
            if (h == IntPtr.Zero) { System.Threading.Thread.Sleep(3000); continue; }  // kanal yok -> bekle, tekrar dene
            try
            {
                var birikim = new MemoryStream();
                byte[] buf = new byte[65536];
                while (_dinleAcik)
                {
                    uint okundu;
                    // 2 sn timeout: kanal koparsa/veri yoksa donguyu canli tut
                    if (!WTSVirtualChannelRead(h, 2000, buf, (uint)buf.Length, out okundu))
                    {
                        int hata = Marshal.GetLastWin32Error();
                        if (hata == 0) continue;              // timeout - veri yok
                        break;                                // gercek hata - kanali yeniden ac
                    }
                    if (okundu == 0) continue;
                    birikim.Write(buf, 0, (int)okundu);
                    TamMesajlariAyikla(birikim, isle);
                }
            }
            catch { }
            finally { WTSVirtualChannelClose(h); }
            System.Threading.Thread.Sleep(1000);
        }
    }

    // Birikimden tam ([tur][veri]) mesajlarini ayikla, isle, kalani sakla.
    static void TamMesajlariAyikla(MemoryStream birikim, Action<string, string> isle)
    {
        byte[] veri = birikim.ToArray();
        int ofs = 0;
        while (veri.Length - ofs >= 8)
        {
            int turLen = BitConverter.ToInt32(veri, ofs);
            if (turLen < 0 || turLen > 64 || veri.Length - ofs < 8 + turLen) break;
            int veriLen = BitConverter.ToInt32(veri, ofs + 4 + turLen);
            if (veriLen < 0 || veriLen > 10000000) break;
            if (veri.Length - ofs < 8 + turLen + veriLen) break;   // mesaj henuz tam degil
            string tur = Encoding.UTF8.GetString(veri, ofs + 4, turLen);
            string icerik = Encoding.UTF8.GetString(veri, ofs + 8 + turLen, veriLen);
            try { isle(tur, icerik); } catch { }
            ofs += 8 + turLen + veriLen;
        }
        // Kalan yarim mesaji birikimde tut
        birikim.SetLength(0);
        if (ofs < veri.Length) birikim.Write(veri, ofs, veri.Length - ofs);
    }
}
