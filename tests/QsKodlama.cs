// TANI + REGRESYON TESTI
// HttpListener.QueryString, yuzde-kodlu degerleri OS ANSI kod sayfasiyla cozer.
// Istemci ise UTF-8 kodlar. Bu uyusmazlik, onaylanan isin sunucuda
// bulunamamasina ve isin sonsuza kadar yeniden verilmesine yol aciyordu.
// Sunucudaki Q() yardimcisi HAM URL'yi UTF-8 ile cozer; asagida ikisi
// yan yana kanitlanir.
using System;
using System.Net;
using System.Text;
using System.Threading;

class QsKodlama
{
    // server/Print360.Dashboard.cs icindeki Q() ile AYNI mantik
    static string Q(HttpListenerContext ctx, string ad)
    {
        try
        {
            string raw = ctx.Request.RawUrl;
            if (raw == null) return null;
            int s = raw.IndexOf('?');
            if (s < 0) return null;
            string[] parcalar = raw.Substring(s + 1).Split('&');
            for (int i = 0; i < parcalar.Length; i++)
            {
                int e = parcalar[i].IndexOf('=');
                if (e < 0) continue;
                if (!string.Equals(parcalar[i].Substring(0, e), ad, StringComparison.OrdinalIgnoreCase)) continue;
                string deger = parcalar[i].Substring(e + 1).Replace("+", "%20");
                try { return Uri.UnescapeDataString(deger); } catch { return deger; }
            }
            return null;
        }
        catch { return null; }
    }

    static string eskiYol, yeniYol;

    static void Main()
    {
        string[] adlar = new string[] {
            "20260821_193128_109_Administrator~Belgeyi Yazdır.pdf.gz",   // sahadaki takilan is
            "Şirket Özet Ğider Çizelge.pdf.gz",           // s,O,g,C
            "yuzde %100 zam & indirim.pdf.gz",
            "duz-ascii-belge.pdf.gz"
        };

        var l = new HttpListener();
        l.Prefixes.Add("http://localhost:18099/");
        l.Start();
        var t = new Thread(delegate()
        {
            while (true)
            {
                var ctx = l.GetContext();
                eskiYol = ctx.Request.QueryString["id"] ?? "";
                yeniYol = Q(ctx, "id") ?? "";
                var b = Encoding.UTF8.GetBytes("OK");
                ctx.Response.ContentLength64 = b.Length;
                ctx.Response.OutputStream.Write(b, 0, b.Length);
                ctx.Response.Close();
            }
        });
        t.IsBackground = true; t.Start();

        Console.WriteLine("Sunucu ANSI kod sayfasi: " + Encoding.Default.WebName);
        Console.WriteLine();
        int gecti = 0;
        foreach (string ad in adlar)
        {
            string bekleniyor = "F:" + ad;
            using (var wc = new WebClient())
                wc.UploadString("http://localhost:18099/x?machine=TEST&id=F:"
                                + Uri.EscapeDataString(ad), "POST", "");   // ACK ile birebir ayni
            Thread.Sleep(120);
            bool eskiOk = (eskiYol == bekleniyor);
            bool yeniOk = (yeniYol == bekleniyor);
            if (yeniOk) gecti++;
            Console.WriteLine((yeniOk ? "GECTI " : "KALDI ") + ad);
            Console.WriteLine("   QueryString (ESKI): " + (eskiOk ? "eslesti" : "BOZULDU -> " + eskiYol));
            Console.WriteLine("   Q() UTF-8   (YENI): " + (yeniOk ? "eslesti" : "BOZULDU -> " + yeniYol));
        }
        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + adlar.Length);
        Environment.Exit(gecti == adlar.Length ? 0 : 1);
    }
}
