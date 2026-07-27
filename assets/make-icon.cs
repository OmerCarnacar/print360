// ============================================================
//  Print360 - RDP Yazdirma ve Yonetim Cozumu
//  Gelistirici : Omer CARNACAR  <omer.carnacar@outlook.com.tr>
//  LinkedIn    : https://www.linkedin.com/in/omercarnacar/
//  Lisans      : UCRETSIZ SURUM - para ile satilamaz (bkz. LICENSE)
//  Telif       : (c) 2026 Omer CARNACAR
// ============================================================
// Print360 - modern logo (.ico + onizleme .png) ureteci
// Derle:  csc /r:System.Drawing.dll make-icon.cs
// Calistir: make-icon.exe   -> Print360.ico ve Print360-logo.png uretir
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class MakeIcon
{
    static GraphicsPath Rounded(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Bitmap Render(int S)
    {
        var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float s = S;

            // --- Zemin: yuvarlak kose kare (squircle), mavi -> indigo gradyan ---
            var tile = new RectangleF(0, 0, s, s);
            float rad = s * 0.222f;
            using (var path = Rounded(tile, rad))
            using (var grad = new LinearGradientBrush(
                       new PointF(0, 0), new PointF(s, s),
                       Color.FromArgb(0x3B, 0x82, 0xF6),   // blue-500
                       Color.FromArgb(0x63, 0x66, 0xF1)))   // indigo-500
            {
                g.FillPath(grad, path);
                // ust kenarda hafif parlaklik
                using (var glossPath = Rounded(new RectangleF(0, 0, s, s * 0.5f), rad))
                using (var gloss = new LinearGradientBrush(
                           new PointF(0, 0), new PointF(0, s * 0.5f),
                           Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)))
                    g.FillPath(gloss, glossPath);
            }

            var cx = s / 2f;
            var white = Color.White;
            var accent = Color.FromArgb(0x22, 0xD3, 0xEE); // cyan-400

            // --- 360 halkasi (buyuk boyutlarda): donme/deviri temsil eden ok ---
            if (S >= 48)
            {
                float ringR = s * 0.375f;
                var ringRect = new RectangleF(cx - ringR, s * 0.5f - ringR, ringR * 2, ringR * 2);
                using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), s * 0.05f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    // ust-sagda acik birak (gap), donen ok hissi
                    g.DrawArc(pen, ringRect, -60, 285);
                }
                // ok ucu (arc bitiminde, ~ -60 derece civari)
                double a = (-60) * Math.PI / 180.0;
                float ex = cx + ringR * (float)Math.Cos(a);
                float ey = s * 0.5f + ringR * (float)Math.Sin(a);
                float ah = s * 0.075f;
                using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                {
                    var tri = new PointF[] {
                        new PointF(ex + ah, ey - ah*0.2f),
                        new PointF(ex - ah*0.3f, ey - ah),
                        new PointF(ex - ah*0.3f, ey + ah)
                    };
                    g.FillPolygon(b, tri);
                }
            }

            // --- Yazici govdesi ---
            float bw = s * 0.50f, bh = s * 0.24f;
            var body = new RectangleF(cx - bw / 2, s * 0.44f, bw, bh);
            using (var path = Rounded(body, s * 0.05f))
            using (var b = new SolidBrush(white))
                g.FillPath(b, path);

            // ust kagit (govde arkasindan cikan)
            float pw = s * 0.34f, ph = s * 0.16f;
            var paper = new RectangleF(cx - pw / 2, s * 0.30f, pw, ph);
            using (var path = Rounded(paper, s * 0.03f))
            using (var b = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                g.FillPath(b, path);

            // guc gostergesi (accent nokta)
            float dd = s * 0.045f;
            using (var b = new SolidBrush(accent))
                g.FillEllipse(b, body.Right - s * 0.10f, body.Y + bh / 2 - dd / 2, dd, dd);

            // --- Cikan ciktı (onde, alt) ---
            float ow = s * 0.40f, oh = s * 0.24f;
            var outp = new RectangleF(cx - ow / 2, s * 0.56f, ow, oh);
            using (var path = Rounded(outp, s * 0.035f))
            using (var b = new SolidBrush(white))
                g.FillPath(b, path);
            // sayfadaki "metin" cizgileri (zemin rengiyle)
            using (var pen = new Pen(Color.FromArgb(0x63, 0x66, 0xF1), s * 0.022f))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                float lx = outp.X + ow * 0.16f, lw = ow * 0.68f;
                for (int i = 0; i < 3; i++)
                {
                    float ly = outp.Y + oh * (0.30f + i * 0.22f);
                    g.DrawLine(pen, lx, ly, lx + (i == 2 ? lw * 0.6f : lw), ly);
                }
            }
        }
        return bmp;
    }

    static void Main()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        int[] sizes = { 256, 64, 48, 32, 16 };

        // Onizleme PNG (256)
        using (var big = Render(256))
            big.Save(Path.Combine(dir, "Print360-logo.png"), ImageFormat.Png);

        // ICO: her boyutu PNG olarak goml (Vista+ destekler)
        var pngs = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            using (var bm = Render(sizes[i]))
            using (var ms = new MemoryStream())
            { bm.Save(ms, ImageFormat.Png); pngs[i] = ms.ToArray(); }

        using (var fs = new FileStream(Path.Combine(dir, "Print360.ico"), FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((short)0);          // reserved
            bw.Write((short)1);          // type = icon
            bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int sz = sizes[i];
                bw.Write((byte)(sz >= 256 ? 0 : sz)); // width
                bw.Write((byte)(sz >= 256 ? 0 : sz)); // height
                bw.Write((byte)0);        // renk sayisi
                bw.Write((byte)0);        // reserved
                bw.Write((short)1);       // planes
                bw.Write((short)32);      // bit depth
                bw.Write(pngs[i].Length); // veri boyutu
                bw.Write(offset);         // veri konumu
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) bw.Write(p);
        }
        Console.WriteLine("Uretildi: Print360.ico + Print360-logo.png");
    }
}
