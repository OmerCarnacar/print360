// ============================================================
//  Print360 - RDP Yazdirma ve Yonetim Cozumu
//  Gelistirici : Omer CARNACAR  <omer.carnacar@outlook.com.tr>
//  LinkedIn    : https://www.linkedin.com/in/omercarnacar/
//  Lisans      : UCRETSIZ SURUM - para ile satilamaz (bkz. LICENSE)
//  Telif       : (c) 2026 Omer CARNACAR
// ============================================================
// Print360 Panel - WPF masaustu uygulamasi (sunucuda calisir)
// Web paneliyle ayni veri kaynaklarini okur:
//   C:\Print360\stats\jobs.csv, C:\Print360\stats\clients\*.csv, Active Directory
// Sayfalar: Genel Bakis | Makineler (AD) | Periyotlar | Isler
using System;
using System.Collections;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

static class Panel360
{
    static string jobsCsv = @"C:\Print360\stats\jobs.csv";
    static string clientsDir = @"C:\Print360\stats\clients";

    static readonly Brush Koyu = Hex("#1F3A5F");
    static readonly Brush KoyuHover = Hex("#28497A");
    static readonly Brush Zemin = Hex("#F2F4F8");
    static readonly Brush Beyaz = Brushes.White;
    static readonly Brush Gri = Hex("#667788");
    static readonly Brush Bar = Hex("#3F77C9");

    class Sent { public DateTime Time; public string User, Machine, Doc, Pages, File, Paper, Status; public int PageN, KbN; }
    class AdPc { public string Name, Os; public DateTime? LastLogon; }
    class AdUser { public string Sam, Ad; public bool Aktif; public DateTime? LastLogon; }
    public class KuralSatir
    {
        public string Tip { get; set; }
        public string Ad { get; set; }
        public bool Engelli { get; set; }
        public int GunlukSayfaKotasi { get; set; }
    }

    static ContentControl icerik;
    static string aktifSayfa = "genel";
    static DateTime pFrom = DateTime.Today.AddDays(-29), pTo = DateTime.Today;
    static List<Button> navButtons = new List<Button>();
    static List<AdPc> adCache; static DateTime adCacheTime = DateTime.MinValue; static string adError;
    static List<AdUser> adUserCache; static DateTime adUserCacheTime = DateTime.MinValue;
    static ControlTemplate navTpl, btnTpl;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    // Baslatma hatasini SESSIZCE yutma: kullaniciya goster + log'a yaz.
    // (Eksik DevExpress DLL'i gibi hatalar WPF tipleri yuklenirken olusur;
    //  bu yuzden asil is ayri bir metotta - JIT hatasi burada yakalanabilsin.)
    [STAThread]
    static void Main()
    {
        try { Calistir(); }
        catch (Exception ex)
        {
            string ek = "";
            try
            {
                var dir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                ek = "\r\n\r\nPanel klasoru: " + dir
                   + "\r\n\r\nPanel harici bilesen KULLANMAZ; yalnizca .NET Framework 4.x gerekir."
                   + "\r\nWeb paneli her zaman calisir: http://<sunucu>:8360";
            }
            catch { }
            try
            {
                Directory.CreateDirectory(@"C:\Print360\logs");
                File.AppendAllText(@"C:\Print360\logs\panel-hata.log",
                    DateTime.Now + "  [Baslatma] " + ex + ek + "\r\n");
            }
            catch { }
            MessageBoxW(IntPtr.Zero,
                "Print360 Panel baslatilamadi.\r\n\r\n" + ex.Message + ek
                + "\r\n\r\nAyrinti: C:\\Print360\\logs\\panel-hata.log",
                "Print360 Panel - Hata", 0x10 /*MB_ICONERROR*/);
        }
    }

    static void Calistir()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try { File.AppendAllText(@"C:\Print360\logs\panel-hata.log",
                DateTime.Now + "  [AppDomain] " + e.ExceptionObject + "\r\n"); } catch { }
        };
        HazirlaSablonlar();

        // Giris: SQL PanelUsers (kullanici adi + sifre); SQL yoksa panel.pwd tek-sifre modu
        var sqlUsers = SqlKullanicilar();
        string dosyaHash = "";
        try { if (File.Exists(@"C:\Print360\panel.pwd")) dosyaHash = File.ReadAllText(@"C:\Print360\panel.pwd").Trim(); } catch { }
        if ((sqlUsers != null && sqlUsers.Count > 0) || dosyaHash.Length > 0)
        {
            if (!LoginGoster(sqlUsers, dosyaHash)) return;
        }

        var w = new Window
        {
            Title = "Print360 Panel",
            Width = 1220, Height = 800,
            Background = Zemin,
            FontFamily = new FontFamily("Segoe UI"),
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        var root = new DockPanel();

        // Sol menu (gradyan)
        var nav = new StackPanel { Width = 222 };
        var navBg = new Border
        {
            Child = nav,
            Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#16263F"),
                (Color)ColorConverter.ConvertFromString("#274A78"), 90)
        };
        DockPanel.SetDock(navBg, Dock.Left);
        nav.Children.Add(new TextBlock
        {
            Text = "\U0001F5A8 Print360",
            Foreground = Beyaz, FontSize = 21, FontWeight = FontWeights.Bold,
            Margin = new Thickness(22, 22, 20, 2)
        });
        nav.Children.Add(new TextBlock
        {
            Text = "Yazdırma Yönetim Paneli",
            Foreground = Hex("#8FA6C6"), FontSize = 11,
            Margin = new Thickness(23, 0, 20, 14)
        });
        nav.Children.Add(new Border { Height = 1, Background = Hex("#33FFFFFF"), Margin = new Thickness(16, 0, 16, 12) });
        nav.Children.Add(NavBtn("genel", "\U0001F4C8  Genel Bakış"));
        nav.Children.Add(NavBtn("makineler", "\U0001F5A5  Makineler (AD)"));
        nav.Children.Add(NavBtn("yazicilar", "\U0001F5A8  Yazıcılar"));
        nav.Children.Add(NavBtn("kullanicilar", "\U0001F465  Kullanıcılar"));
        nav.Children.Add(NavBtn("periyot", "\U0001F4C5  Periyotlar"));
        nav.Children.Add(NavBtn("isler", "\U0001F4C4  İşler"));
        nav.Children.Add(NavBtn("yetki", "\U0001F6AB  Yetkiler"));
        nav.Children.Add(NavBtn("uyarilar", "\U0001F514  Uyarılar"));
        var yenile = NavBtn("yenile", "↻  Yenile");
        yenile.Margin = new Thickness(8, 24, 8, 0);
        nav.Children.Add(yenile);
        nav.Children.Add(new TextBlock
        {
            Text = "v" + Surum.Etiket + "  •  " + Environment.MachineName,
            Foreground = Hex("#6C84A8"), FontSize = 10,
            Margin = new Thickness(23, 26, 20, 0)
        });
        // Ucretsiz surum notu + gelistirici (e-postaya tiklaninca posta programi acilir)
        nav.Children.Add(new TextBlock
        {
            Text = "Ücretsiz sürüm — para ile satılamaz",
            Foreground = Hex("#6C84A8"), FontSize = 10, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(23, 10, 20, 0)
        });
        nav.Children.Add(new TextBlock
        {
            Text = "Geliştirici: Ömer ÇARNAÇAR",
            Foreground = Hex("#8FA6C6"), FontSize = 10, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(23, 6, 20, 0)
        });
        var mailLink = new TextBlock { Margin = new Thickness(23, 2, 20, 0), TextWrapping = TextWrapping.Wrap };
        var hyper = new System.Windows.Documents.Hyperlink(
            new System.Windows.Documents.Run(Lisans.Eposta))
        { Foreground = Hex("#6FB1FF") };
        hyper.ToolTip = "E-posta gönder";
        hyper.Click += (s, e) => MailAc();
        mailLink.Inlines.Add(hyper);
        mailLink.FontSize = 10;
        nav.Children.Add(mailLink);

        var inLink = new TextBlock { Margin = new Thickness(23, 3, 20, 0), FontSize = 10 };
        var inHyper = new System.Windows.Documents.Hyperlink(
            new System.Windows.Documents.Run("in  LinkedIn profili"))
        { Foreground = Hex("#6FB1FF") };
        inHyper.ToolTip = Lisans.LinkedIn;
        inHyper.Click += (s, e) => LinkAc(Lisans.LinkedIn);
        inLink.Inlines.Add(inHyper);
        nav.Children.Add(inLink);
        root.Children.Add(navBg);

        // Icerik
        icerik = new ContentControl { Margin = new Thickness(24, 18, 24, 18) };
        var scroll = new ScrollViewer { Content = icerik, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll);
        w.Content = root;

        Goster("genel");


        // 30 sn'de bir otomatik yenile
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (s, e) => Goster(aktifSayfa);
        timer.Start();

        var app = new Application();
        app.DispatcherUnhandledException += (s, e) =>
        {
            try { File.AppendAllText(@"C:\Print360\logs\panel-hata.log",
                DateTime.Now + "  [Dispatcher] " + e.Exception + "\r\n"); } catch { }
            e.Handled = true; // logla ama uygulamayi capraz yikma
        };
        app.Run(w);
    }

    // Baglantiyi varsayilan tarayicida ac (LinkedIn vb.)
    static void LinkAc(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Clipboard.SetText(url);
                MessageBox.Show("Tarayıcı açılamadı.\r\nAdres panoya kopyalandı:\r\n\r\n" + url,
                    "Print360", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }
    }

    // Gelistirici e-postasina tiklaninca varsayilan posta programini ac
    static void MailAc()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "mailto:" + Lisans.Eposta + "?subject="
                + Uri.EscapeDataString("Print360 Panel v" + "1.1" + " - " + Environment.MachineName))
            { UseShellExecute = true });
        }
        catch
        {
            try
            {   // Posta istemcisi yoksa adresi panoya kopyala
                Clipboard.SetText(Lisans.Eposta);
                MessageBox.Show("Posta programı açılamadı.\r\nAdres panoya kopyalandı:\r\n\r\n"
                    + Lisans.Eposta, "Print360", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }
    }

    // Yuvarlatilmis, hover efektli sablonlar (XAML)
    static void HazirlaSablonlar()
    {
        string ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";
        navTpl = (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate TargetType='Button' " + ns + ">" +
            "<Border x:Name='bd' Background='{TemplateBinding Background}' CornerRadius='9'>" +
            "<ContentPresenter Margin='{TemplateBinding Padding}' VerticalAlignment='Center' HorizontalAlignment='Left'/></Border>" +
            "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'>" +
            "<Setter TargetName='bd' Property='Background' Value='#2EFFFFFF'/></Trigger></ControlTemplate.Triggers>" +
            "</ControlTemplate>");
        btnTpl = (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate TargetType='Button' " + ns + ">" +
            "<Border x:Name='bd' Background='{TemplateBinding Background}' CornerRadius='8'>" +
            "<ContentPresenter Margin='{TemplateBinding Padding}' VerticalAlignment='Center' HorizontalAlignment='Center'/></Border>" +
            "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'>" +
            "<Setter TargetName='bd' Property='Background' Value='#2B5288'/></Trigger></ControlTemplate.Triggers>" +
            "</ControlTemplate>");
    }

    // Modern birincil / ikincil dugme
    static Button ModernBtn(string text, bool birincil)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(16, 8, 16, 8),
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = birincil ? Beyaz : Koyu,
            Background = birincil ? Koyu : Hex("#E4EAF3"),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = btnTpl
        };
        if (!birincil)
        {
            // ikincil dugmede hover'i acik tonda tut
            b.Template = (ControlTemplate)XamlReader.Parse(
                "<ControlTemplate TargetType='Button' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
                "<Border x:Name='bd' Background='{TemplateBinding Background}' CornerRadius='8'>" +
                "<ContentPresenter Margin='{TemplateBinding Padding}' VerticalAlignment='Center' HorizontalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'>" +
                "<Setter TargetName='bd' Property='Background' Value='#D2DCEA'/></Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate>");
        }
        return b;
    }

    static Dictionary<string, string> SqlKullanicilar()
    {
        var dt = Db.Query("SELECT Kullanici, SifreHash FROM PanelUsers");
        if (dt == null) return null;
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Data.DataRow r in dt.Rows) d[Convert.ToString(r[0])] = Convert.ToString(r[1]).Trim();
        return d;
    }

    // Modern giris ekrani - SQL kullanici+sifre; SQL yoksa tek sifre (panel.pwd)
    static bool LoginGoster(Dictionary<string, string> sqlUsers, string hash)
    {
        bool kullaniciModu = sqlUsers != null && sqlUsers.Count > 0;
        bool ok = false;
        var lw = new Window
        {
            Width = 400, Height = kullaniciModu ? 380 : 330,
            WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            FontFamily = new FontFamily("Segoe UI")
        };
        var kart = new Border
        {
            Background = Beyaz, CornerRadius = new CornerRadius(18), Margin = new Thickness(14),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 3, Opacity = 0.35 }
        };
        var g = new Grid();
        var kapat = new Button
        {
            Content = "✕", FontSize = 13, Width = 30, Height = 30,
            Background = Brushes.Transparent, Foreground = Gri, BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 10, 0), Cursor = System.Windows.Input.Cursors.Hand
        };
        kapat.Click += (s, e) => lw.Close();
        var sp = new StackPanel { Margin = new Thickness(38, 34, 38, 30), VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock
        {
            Text = "\U0001F5A8 Print360", FontSize = 26, FontWeight = FontWeights.Bold,
            Foreground = Koyu, HorizontalAlignment = HorizontalAlignment.Center
        });
        sp.Children.Add(new TextBlock
        {
            Text = "Yazdırma Yönetim Paneli", FontSize = 12, Foreground = Gri,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 22)
        });
        TextBox tbUser = null;
        if (kullaniciModu)
        {
            tbUser = new TextBox
            {
                FontSize = 14, Padding = new Thickness(11, 9, 11, 9),
                BorderBrush = Hex("#CDD7E5"), BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 10)
            };
            sp.Children.Add(new TextBlock { Text = "Kullanıcı adı", FontSize = 11, Foreground = Gri, Margin = new Thickness(2, 0, 0, 3) });
            sp.Children.Add(tbUser);
            sp.Children.Add(new TextBlock { Text = "Şifre", FontSize = 11, Foreground = Gri, Margin = new Thickness(2, 0, 0, 3) });
        }
        var pb = new PasswordBox
        {
            FontSize = 14, Padding = new Thickness(11, 9, 11, 9),
            BorderBrush = Hex("#CDD7E5"), BorderThickness = new Thickness(1)
        };
        sp.Children.Add(pb);
        var hataTxt = new TextBlock
        {
            Text = "Hatalı şifre, tekrar deneyin.", Foreground = Hex("#C0392B"),
            FontSize = 12, Margin = new Thickness(2, 6, 0, 0), Visibility = Visibility.Collapsed
        };
        sp.Children.Add(hataTxt);
        var giris = ModernBtn("Giriş", true);
        giris.Margin = new Thickness(0, 16, 0, 0);
        giris.HorizontalAlignment = HorizontalAlignment.Stretch;
        Action dene = () =>
        {
            bool dogru;
            if (kullaniciModu)
            {
                string h; string u = tbUser.Text.Trim();
                dogru = u.Length > 0 && sqlUsers.TryGetValue(u, out h) && h == Sha256Hex(pb.Password);
            }
            else dogru = Sha256Hex(pb.Password) == hash;
            if (dogru) { ok = true; lw.Close(); }
            else { hataTxt.Visibility = Visibility.Visible; pb.Clear(); pb.Focus(); }
        };
        giris.Click += (s, e) => dene();
        pb.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) dene(); };
        sp.Children.Add(giris);
        g.Children.Add(sp); g.Children.Add(kapat);
        kart.Child = g; lw.Content = kart;
        lw.MouseLeftButtonDown += (s, e) => { try { lw.DragMove(); } catch { } };
        lw.Loaded += (s, e) => { if (kullaniciModu) tbUser.Focus(); else pb.Focus(); };
        lw.ShowDialog();
        return ok;
    }

    static string Sha256Hex(string s)
    {
        using (var sha = SHA256.Create())
        {
            var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            var sb = new StringBuilder();
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
    }

    static Button NavBtn(string key, string text)
    {
        var b = new Button
        {
            Content = text, Tag = key,
            Background = Brushes.Transparent, Foreground = Hex("#C9D6EA"),
            BorderThickness = new Thickness(0), FontSize = 14,
            Margin = new Thickness(8, 2, 8, 2),
            Padding = new Thickness(14, 10, 10, 10), Cursor = System.Windows.Input.Cursors.Hand,
            Template = navTpl, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        b.Click += (s, e) => { if (key == "yenile") { adCacheTime = DateTime.MinValue; adUserCacheTime = DateTime.MinValue; Goster(aktifSayfa); } else Goster(key); };
        if (key != "yenile") navButtons.Add(b);
        return b;
    }

    static void Goster(string key)
    {
        aktifSayfa = key;
        foreach (var b in navButtons)
        {
            bool act = (string)b.Tag == key;
            b.Background = act ? KoyuHover : Brushes.Transparent;
            b.Foreground = act ? Beyaz : Hex("#C9D6EA");
            b.FontWeight = act ? FontWeights.SemiBold : FontWeights.Normal;
        }
        kartSira = 0; // kart renkleri her sayfada ayni sirayla baslasin
        try
        {
            switch (key)
            {
                case "makineler": icerik.Content = SayfaMakineler(); break;
                case "yazicilar": icerik.Content = SayfaYazicilar(); break;
                case "kullanicilar": icerik.Content = SayfaKullanicilar(); break;
                case "periyot": icerik.Content = SayfaPeriyot(); break;
                case "isler": icerik.Content = SayfaIsler(""); break;
                case "yetki": icerik.Content = SayfaYetki(); break;
                case "uyarilar": icerik.Content = SayfaUyarilar(); break;
                default: icerik.Content = SayfaGenel(); break;
            }
        }
        catch (Exception ex)
        {
            icerik.Content = new TextBlock { Text = "Hata: " + ex.Message, Foreground = Brushes.Red };
        }
    }

    // ---------------- Sayfalar ----------------

    static UIElement SayfaGenel()
    {
        var all = LoadSent();
        var sent = all.Where(s => s.Status == "OK").ToList();
        var printed = LoadPrinted();
        var sp = new StackPanel();
        sp.Children.Add(Baslik("Genel Bakış"));
        sp.Children.Add(new Border
        {
            Background = Hex("#E7F0FD"),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new TextBlock
            {
                Text = "🔑 " + Lisans.DurumMetni(),
                FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Foreground = Hex("#1F3A5F")
            }
        });

        int totalPages = sent.Sum(s => s.PageN);
        int bugunN = sent.Count(s => s.Time.Date == DateTime.Today);
        int dunN = sent.Count(s => s.Time.Date == DateTime.Today.AddDays(-1));
        int buHafta = sent.Count(s => s.Time >= DateTime.Today.AddDays(-7));
        int gecenHafta = sent.Count(s => s.Time >= DateTime.Today.AddDays(-14) && s.Time < DateTime.Today.AddDays(-7));
        Brush tR1, tR2;
        string bugunTrend = TrendMetni(bugunN, dunN, out tR1);
        string haftaTrend = TrendMetni(buHafta, gecenHafta, out tR2);
        var maliyetler = LoadCosts();

        var kartlar = new WrapPanel();
        kartlar.Children.Add(Kart(sent.Count.ToString(), "Toplam Çıktı", "son 7 gün: " + buHafta, tR2));
        kartlar.Children.Add(Kart(bugunN.ToString(), "Bugün", bugunTrend, tR1));
        kartlar.Children.Add(Kart(buHafta.ToString(), "Bu Hafta", haftaTrend, tR2));
        kartlar.Children.Add(Kart(sent.Count(s => printed.ContainsKey(s.File)).ToString(), "Basıldı (onaylı)"));
        kartlar.Children.Add(Kart(totalPages.ToString(), "Toplam Sayfa"));
        kartlar.Children.Add(Kart(sent.Count > 0 ? Math.Round((double)totalPages / sent.Count, 1).ToString(CultureInfo.InvariantCulture) : "0", "Ort. Sayfa/İş"));
        kartlar.Children.Add(Kart(Math.Round(sent.Sum(s => (double)s.KbN) / 1024.0, 1).ToString(CultureInfo.InvariantCulture) + " MB", "Toplam Veri"));
        kartlar.Children.Add(Kart((all.Count - sent.Count).ToString(), "Engellenen"));
        if (maliyetler.Count > 0) kartlar.Children.Add(Kart(Para(ToplamMaliyet(sent, maliyetler)), "Toplam Maliyet"));
        sp.Children.Add(kartlar);

        // Son 7 gun cikti trendi (mini cubuk grafik)
        sp.Children.Add(AltBaslik("Son 7 Gün Trendi"));
        var trendKutu = BeyazKutu();
        var gunler = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(-6 + i)).ToList();
        int maxGun = Math.Max(1, gunler.Max(d => sent.Count(s => s.Time.Date == d)));
        foreach (var d in gunler)
        {
            int say = sent.Count(s => s.Time.Date == d);
            int sayfa = sent.Where(s => s.Time.Date == d).Sum(s => s.PageN);
            ((StackPanel)trendKutu.Child).Children.Add(BarSatir(
                d.ToString("dd.MM ddd", new CultureInfo("tr-TR")), say, maxGun,
                say + " çıktı / " + sayfa + " sf" + (maliyetler.Count > 0 ? " / " + Para(ToplamMaliyet(sent.Where(s => s.Time.Date == d), maliyetler)) : "")));
        }
        sp.Children.Add(trendKutu);

        var iki = IkiSutun();
        var sol = (StackPanel)((Grid)iki).Children[0];
        var sag = (StackPanel)((Grid)iki).Children[1];

        sol.Children.Add(AltBaslik("Makine Bazlı"));
        sol.Children.Add(Tablo(sent.GroupBy(s => MK(s)).OrderByDescending(g => g.Count()).Select(g => new
        {
            Makine = g.Key, Gonderilen = g.Count(),
            Basilan = g.Count(x => printed.ContainsKey(x.File)),
            Sayfa = g.Sum(x => x.PageN)
        }).ToList()));
        sol.Children.Add(AltBaslik("Kullanıcı Bazlı"));
        sol.Children.Add(Tablo(sent.GroupBy(s => s.User).OrderByDescending(g => g.Count()).Select(g => new
        {
            Kullanici = g.Key, Cikti = g.Count(), Sayfa = g.Sum(x => x.PageN)
        }).ToList()));

        sag.Children.Add(AltBaslik("Kağıt Türü"));
        if (maliyetler.Count > 0)
            sag.Children.Add(Tablo(sent.GroupBy(s => s.Paper).OrderByDescending(g => g.Count()).Select(g => new
            {
                Kagit = g.Key, Cikti = g.Count(), Sayfa = g.Sum(x => x.PageN),
                Oran = (sent.Count > 0 ? Math.Round(100.0 * g.Count() / sent.Count) : 0) + "%",
                Maliyet = Para(ToplamMaliyet(g, maliyetler))
            }).ToList()));
        else
            sag.Children.Add(Tablo(sent.GroupBy(s => s.Paper).OrderByDescending(g => g.Count()).Select(g => new
            {
                Kagit = g.Key, Cikti = g.Count(), Sayfa = g.Sum(x => x.PageN),
                Oran = (sent.Count > 0 ? Math.Round(100.0 * g.Count() / sent.Count) : 0) + "%"
            }).ToList()));
        sag.Children.Add(AltBaslik("Yazıcı Bazlı"));
        sag.Children.Add(Tablo(sent.Where(s => printed.ContainsKey(s.File))
            .GroupBy(s => printed[s.File].Length > 3 ? printed[s.File][3] : "?")
            .OrderByDescending(g => g.Count()).Select(g => new
            {
                Yazici = g.Key, Cikti = g.Count(), Sayfa = g.Sum(x => x.PageN)
            }).ToList()));
        sp.Children.Add(iki);

        sp.Children.Add(AltBaslik("Son 20 İş"));
        sp.Children.Add(IsTablosu(Enumerable.Reverse(sent).Take(20), printed));
        return sp;
    }

    static UIElement SayfaMakineler()
    {
        var sent = LoadSent().Where(s => s.Status == "OK").ToList();
        var printed = LoadPrinted();
        var hb = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hdt = Db.Query("SELECT Makine, SonGorulme FROM Heartbeat");
        if (hdt != null)
        {
            foreach (System.Data.DataRow r in hdt.Rows)
            {
                if (r[1] == DBNull.Value) continue;
                var t = Convert.ToDateTime(r[1]);
                hb[Convert.ToString(r[0])] = (DateTime.Now - t).TotalMinutes <= 3
                    ? "● Çevrimiçi" : "Çevrimdışı (" + t.ToString("yyyy-MM-dd HH:mm") + ")";
            }
        }
        else
            foreach (var r in ReadCsv(@"C:\Print360\stats\heartbeat.csv").Where(r => r.Length >= 2))
            {
                DateTime t;
                if (DateTime.TryParseExact(r[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                    hb[r[0]] = (DateTime.Now - t).TotalMinutes <= 3 ? "● Çevrimiçi" : "Çevrimdışı (" + r[1] + ")";
            }
        var ad = LoadAd();
        // Istemci kayitli makineler (ClientKeys) + oturum kullanicisi/OS (Heartbeat)
        var clientKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ck = Db.Query("SELECT Makine FROM ClientKeys");
        if (ck != null) foreach (System.Data.DataRow r in ck.Rows) clientKeys.Add(Convert.ToString(r[0]));
        var hbInfo = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var hbDt = Db.Query("SELECT Makine, ISNULL(KullaniciAdi,''), ISNULL(OS,''), ISNULL(IP,'') FROM Heartbeat");
        if (hbDt != null) foreach (System.Data.DataRow r in hbDt.Rows)
            hbInfo[Convert.ToString(r[0])] = new[] { Convert.ToString(r[1]), Convert.ToString(r[2]), Convert.ToString(r[3]) };

        var sp = new StackPanel();
        sp.Children.Add(Baslik("Makineler"));

        var stats = sent.GroupBy(s => MK(s), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new
            {
                N = g.Count(), B = g.Count(x => printed.ContainsKey(x.File)),
                P = g.Sum(x => x.PageN), Son = g.Max(x => x.Time)
            }, StringComparer.OrdinalIgnoreCase);

        // TUM makineler: AD + yazdiranlar + bagli (heartbeat) + istemci kayitli (ClientKeys)
        var names = ad.Select(a => a.Name)
            .Union(stats.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(hb.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(clientKeys, StringComparer.OrdinalIgnoreCase)
            .Where(n => !string.IsNullOrWhiteSpace(n) && !n.StartsWith("("))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        int cevrimici = hb.Values.Count(v => v.StartsWith("●"));
        var kartlar = new WrapPanel();
        kartlar.Children.Add(Kart(names.Count.ToString(), "Toplam Makine"));
        kartlar.Children.Add(Kart(cevrimici.ToString(), "● Çevrimiçi"));
        kartlar.Children.Add(Kart(clientKeys.Count.ToString(), "İstemci Kayıtlı"));
        kartlar.Children.Add(Kart(ad.Count.ToString(), "AD Kayıtlı"));
        sp.Children.Add(kartlar);
        sp.Children.Add(new TextBlock
        {
            Text = adError != null
                ? "Active Directory yok/erişilemez — makineler bağlantı ve yazdırma kayıtlarından listeleniyor."
                : "Etki alanında " + ad.Count + " bilgisayar. AD + bağlı + istemci-kayıtlı + yazdıran makineler birleşik.",
            Foreground = Gri, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap
        });

        sp.Children.Add(Tablo(names.Select(n =>
        {
            var a = ad.FirstOrDefault(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
            bool var_ = stats.ContainsKey(n);
            var st = var_ ? stats[n] : null;
            string bag; hb.TryGetValue(n, out bag);
            string[] hi; hbInfo.TryGetValue(n, out hi);
            bool online = bag != null && bag.StartsWith("●");
            string durum = var_ ? (st.Son > DateTime.Now.AddDays(-7) ? "Aktif yazdırıyor" : "Pasif (7+ gün)")
                          : (online ? "Bağlı (hazır)" : (clientKeys.Contains(n) ? "Kayıtlı (bağlantı yok)" : "Hiç yazdırmadı"));
            return new
            {
                Makine = n,
                Baglanti = bag ?? "—",
                Istemci = clientKeys.Contains(n) ? "✓" : "—",
                Kullanici = hi != null ? hi[0] : "",
                OS = hi != null && hi[1].Length > 0 ? hi[1] : (a != null ? a.Os : ""),
                IP = hi != null ? hi[2] : "",
                AD = a != null ? "✓" : "—",
                Cikti = var_ ? st.N : 0, Sayfa = var_ ? st.P : 0,
                SonYazdirma = var_ ? st.Son.ToString("yyyy-MM-dd HH:mm") : "",
                Durum = durum
            };
        }).ToList()));
        return sp;
    }

    static UIElement SayfaYazicilar()
    {
        var sp = new StackPanel();
        sp.Children.Add(Baslik("Yazıcı Sağlığı"));
        // MSSQL ZORUNLU DEGIL: SQL varsa oradan, yoksa dosya deposundan oku.
        // [makine, yazici, durum, hata, kuyruk, guncelleme]
        var kayit = new List<string[]>();
        var dt = Db.Query("SELECT Makine, Yazici, Durum, Hata, Kuyruk, Guncelleme FROM PrinterHealth ORDER BY Makine, Yazici");
        if (dt != null)
            foreach (System.Data.DataRow r in dt.Rows)
                kayit.Add(new[] { Convert.ToString(r[0]), Convert.ToString(r[1]), Convert.ToString(r[2]),
                                  Convert.ToString(r[3]), r[4] == DBNull.Value ? "0" : Convert.ToString(r[4]),
                                  r[5] == DBNull.Value ? "" : Convert.ToDateTime(r[5]).ToString("yyyy-MM-dd HH:mm:ss") });
        else
        {
            kayit = Db.YaziciSagligiOku();
            kayit.Sort((a, b) => string.Compare(a[0] + "|" + a[1], b[0] + "|" + b[1], StringComparison.OrdinalIgnoreCase));
            sp.Children.Add(new TextBlock
            {
                Text = "Dosya modunda çalışılıyor (MSSQL gerekmiyor). Liste istemci ajanlarının raporlarından gelir.",
                Foreground = Gri, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
        }
        if (kayit.Count == 0)
            sp.Children.Add(new TextBlock
            {
                Text = "Henüz yazıcı raporu gelmedi. İstemci ajanı bağlandıktan ~1 dakika sonra burada listelenir.",
                Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });

        int sorunlu = 0, cevrimdisi = 0, kuyrukT = 0;
        var satirlar = new List<object>();
        foreach (var r in kayit)
        {
            string d = r[2], h = r[3];
            int q; int.TryParse(r[4], out q);
            if (h.Length > 0 || d == "Durduruldu") sorunlu++;
            if (d == "Cevrimdisi") cevrimdisi++;
            kuyrukT += q;
            DateTime g; if (!DateTime.TryParse(r[5], out g)) g = DateTime.MinValue;
            satirlar.Add(new
            {
                Makine = r[0],
                Yazici = r[1],
                Durum = d,
                Sorun = h.Length > 0 ? h : "—",
                Kuyruk = q,
                Guncelleme = g == DateTime.MinValue ? "" :
                    g.ToString("yyyy-MM-dd HH:mm:ss") + (g < DateTime.Now.AddMinutes(-5) ? " (eski)" : "")
            });
        }
        var kartlar = new WrapPanel();
        kartlar.Children.Add(Kart(kayit.Count.ToString(), "Toplam Yazıcı"));
        kartlar.Children.Add(Kart(sorunlu.ToString(), "Sorunlu"));
        kartlar.Children.Add(Kart(cevrimdisi.ToString(), "Çevrimdışı"));
        kartlar.Children.Add(Kart(kuyrukT.ToString(), "Kuyruktaki İş"));
        sp.Children.Add(kartlar);
        if (satirlar.Count > 0) sp.Children.Add(Tablo(satirlar));
        else sp.Children.Add(new TextBlock { Text = "Henüz yazıcı raporu gelmedi.", Foreground = Gri });
        return sp;
    }

    static UIElement SayfaKullanicilar()
    {
        var all = LoadSent();
        var sent = all.Where(s => s.Status == "OK").ToList();
        var adUsers = LoadAdUsers();
        var sp = new StackPanel();
        sp.Children.Add(Baslik("Kullanıcılar Raporu — Active Directory"));
        sp.Children.Add(new TextBlock
        {
            Text = adError != null
                ? "Active Directory sorgulanamadı: " + adError
                : "Etki alanında " + adUsers.Count + " kullanıcı hesabı bulundu. (5 dk önbellek)",
            Foreground = adError != null ? Brushes.DarkOrange : Gri,
            Margin = new Thickness(0, 0, 0, 10)
        });
        var stats = sent.GroupBy(s => s.User, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var engel = all.Where(s => s.Status != "OK").GroupBy(s => s.User, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var kartlar = new WrapPanel();
        kartlar.Children.Add(Kart((adUsers.Select(u => u.Sam).Union(stats.Keys, StringComparer.OrdinalIgnoreCase).Count()).ToString(), "Toplam Kullanıcı"));
        kartlar.Children.Add(Kart(stats.Count.ToString(), "Yazdıran"));
        kartlar.Children.Add(Kart(adUsers.Count(u => !u.Aktif).ToString(), "AD Pasif Hesap"));
        kartlar.Children.Add(Kart(engel.Values.Sum().ToString(), "Engellenen İş"));
        sp.Children.Add(kartlar);

        var names = adUsers.Select(u => u.Sam).Union(stats.Keys, StringComparer.OrdinalIgnoreCase)
                           .Where(n => n.Length > 0).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        var costsK = LoadCosts();
        sp.Children.Add(Tablo(names.Select(n =>
        {
            var au = adUsers.FirstOrDefault(u => string.Equals(u.Sam, n, StringComparison.OrdinalIgnoreCase));
            List<Sent> st; stats.TryGetValue(n, out st);
            int e; engel.TryGetValue(n, out e);
            return new
            {
                Kullanici = n,
                AD = au != null ? "✓" : "—",
                AdSoyad = au != null ? au.Ad : "",
                ADHesap = au == null ? "" : (au.Aktif ? "Aktif" : "Devre dışı"),
                SonADOturumu = au != null && au.LastLogon.HasValue ? au.LastLogon.Value.ToString("yyyy-MM-dd HH:mm") : "",
                Cikti = st != null ? st.Count : 0,
                Sayfa = st != null ? st.Sum(s => s.PageN) : 0,
                Maliyet = costsK.Count > 0 && st != null ? Para(ToplamMaliyet(st, costsK)) : "",
                Veri = st != null ? Math.Round(st.Sum(s => (double)s.KbN) / 1024.0, 1) + " MB" : "",
                Engellenen = e,
                SonYazdirma = st != null ? st.Max(s => s.Time).ToString("yyyy-MM-dd HH:mm") : "",
                EnCokKagit = st != null ? st.GroupBy(s => s.Paper).OrderByDescending(g => g.Count()).First().Key : ""
            };
        }).ToList()));

        // Kisi bazli maliyet dokumu: kullanici x kagit turu (dinamik sutunlar icin DataTable)
        if (costsK.Count > 0 && sent.Count > 0)
        {
            sp.Children.Add(AltBaslik("Kişi Bazlı Maliyet Dökümü (kağıt türüne göre)"));
            var kagitlar = sent.GroupBy(s => s.Paper).OrderByDescending(g => g.Sum(x => x.PageN))
                               .Select(g => g.Key).ToList();
            var mt = new System.Data.DataTable();
            mt.Columns.Add("Kullanıcı");
            foreach (var k in kagitlar) mt.Columns.Add(k);
            mt.Columns.Add("Toplam");
            foreach (var u in stats.OrderByDescending(kv => ToplamMaliyet(kv.Value, costsK)))
            {
                var row = mt.NewRow();
                row[0] = u.Key;
                int i = 1;
                foreach (var k in kagitlar)
                {
                    var alt = u.Value.Where(s => s.Paper.Equals(k, StringComparison.OrdinalIgnoreCase)).ToList();
                    row[i++] = alt.Count > 0 ? alt.Sum(s => s.PageN) + " sf / " + Para(ToplamMaliyet(alt, costsK)) : "—";
                }
                row[i] = Para(ToplamMaliyet(u.Value, costsK));
                mt.Rows.Add(row);
            }
            var toplamRow = mt.NewRow();
            toplamRow[0] = "TOPLAM";
            int j = 1;
            foreach (var k in kagitlar)
            {
                var alt = sent.Where(s => s.Paper.Equals(k, StringComparison.OrdinalIgnoreCase)).ToList();
                toplamRow[j++] = alt.Sum(s => s.PageN) + " sf / " + Para(ToplamMaliyet(alt, costsK));
            }
            toplamRow[j] = Para(ToplamMaliyet(sent, costsK));
            mt.Rows.Add(toplamRow);
            sp.Children.Add(Tablo(mt.DefaultView));
        }
        return sp;
    }

    static UIElement SayfaYetki()
    {
        var sp = new StackPanel();
        sp.Children.Add(Baslik("Yetkiler — Engelleme ve Kotalar"));
        sp.Children.Add(new TextBlock
        {
            Text = "Engelli işaretlenen kullanıcı/makine yazdıramaz. Kota: kullanıcının günlük sayfa limiti (0 = limitsiz). Denetim sunucu ajanında uygulanır.",
            Foreground = Gri, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });

        var all = LoadSent();
        var kurallar = new Dictionary<string, KuralSatir>(StringComparer.OrdinalIgnoreCase);
        var rdt = Db.Query("SELECT Tip,Ad,Engel,Kota FROM Rules");
        if (rdt != null)
        {
            foreach (System.Data.DataRow r in rdt.Rows)
                kurallar[Convert.ToString(r[0]).ToLowerInvariant() + "|" + Convert.ToString(r[1])] = new KuralSatir
                {
                    Tip = Convert.ToString(r[0]).ToLowerInvariant(), Ad = Convert.ToString(r[1]),
                    Engelli = Convert.ToBoolean(r[2]), GunlukSayfaKotasi = Convert.ToInt32(r[3])
                };
        }
        else
            foreach (var r in ReadCsv(@"C:\Print360\rules.csv").Where(r => r.Length >= 3))
            {
                int q = 0; if (r.Length > 3) int.TryParse(r[3], out q);
                kurallar[r[0].ToLowerInvariant() + "|" + r[1]] = new KuralSatir
                { Tip = r[0].ToLowerInvariant(), Ad = r[1], Engelli = r[2] == "1", GunlukSayfaKotasi = q };
            }
        // Active Directory kullanicilari + makineleri ile yazdirma kayitlarinin birlesimi
        var adUsers = LoadAdUsers();
        var adPcs = LoadAd();
        var liste = new List<KuralSatir>();
        foreach (var u in adUsers.Select(x => x.Sam).Union(all.Select(s => s.User), StringComparer.OrdinalIgnoreCase)
                                 .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
        {
            KuralSatir k;
            liste.Add(kurallar.TryGetValue("user|" + u, out k) ? k : new KuralSatir { Tip = "user", Ad = u });
        }
        foreach (var m in adPcs.Select(x => x.Name).Union(all.Select(s => s.Machine), StringComparer.OrdinalIgnoreCase)
                               .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
        {
            KuralSatir k;
            liste.Add(kurallar.TryGetValue("machine|" + m, out k) ? k : new KuralSatir { Tip = "machine", Ad = m });
        }
        if (adError == null && (adUsers.Count > 0 || adPcs.Count > 0))
            sp.Children.Add(new TextBlock
            {
                Text = "Liste Active Directory'den alındı: " + adUsers.Count + " kullanıcı, " + adPcs.Count + " makine.",
                Foreground = Gri, Margin = new Thickness(0, 0, 0, 8)
            });
        var dg = Tablo(liste, true);   // editable GridControl (engel/kota duzenlenebilir)
        sp.Children.Add(dg);

        var kaydet = ModernBtn("Kaydet", true);
        kaydet.Padding = new Thickness(28, 9, 28, 9);
        kaydet.Margin = new Thickness(0, 12, 0, 0);
        kaydet.HorizontalAlignment = HorizontalAlignment.Left;
        kaydet.Click += (s, e) =>
        {
            // Duzenlenen son hucre henuz onaylanmamis olabilir -> once kesinlestir
            try { dg.CommitEdit(DataGridEditingUnit.Cell, true); dg.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
            var sb = new StringBuilder();
            foreach (var k in liste.Where(x => x.Engelli || x.GunlukSayfaKotasi > 0))
                sb.Append("\"").Append(k.Tip).Append("\",\"").Append(k.Ad.Replace("\"", "\"\""))
                  .Append("\",\"").Append(k.Engelli ? "1" : "0").Append("\",\"").Append(k.GunlukSayfaKotasi).Append("\"\r\n");
            try
            {
                File.WriteAllText(@"C:\Print360\rules.csv", sb.ToString());
                // SQL'e de yaz (ajanin birincil kaynagi)
                if (Db.Exec("DELETE FROM Rules"))
                    foreach (var k in liste.Where(x => x.Engelli || x.GunlukSayfaKotasi > 0))
                        Db.Exec("INSERT INTO Rules(Tip,Ad,Engel,Kota) VALUES(@t,@a,@e,@k)",
                            "@t", k.Tip, "@a", k.Ad, "@e", k.Engelli ? 1 : 0, "@k", k.GunlukSayfaKotasi);
                MessageBox.Show("Yetki kuralları kaydedildi" + (Db.Err == null ? " (SQL + dosya)." : " (yalnız dosya; SQL: " + Db.Err + ")"),
                    "Print360", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show("Kaydedilemedi: " + ex.Message, "Print360", MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        sp.Children.Add(kaydet);
        return sp;
    }

    static UIElement SayfaUyarilar()
    {
        var sp = new StackPanel();
        sp.Children.Add(Baslik("Uyarılar"));
        // MSSQL ZORUNLU DEGIL: SQL varsa oradan, yoksa dosya deposundan oku.
        var kayit = new List<string[]>();   // [tarih, tur, mesaj, okundu]
        var dt = Db.Query("SELECT TOP 200 Tarih,Tur,Mesaj,Okundu FROM Alerts ORDER BY Id DESC");
        if (dt != null)
            foreach (System.Data.DataRow r in dt.Rows)
                kayit.Add(new[] { r[0] == DBNull.Value ? "" : Convert.ToDateTime(r[0]).ToString("yyyy-MM-dd HH:mm:ss"),
                                  Convert.ToString(r[1]), Convert.ToString(r[2]),
                                  Convert.ToBoolean(r[3]) ? "1" : "0" });
        else
        {
            kayit = Db.UyarilariOku(200);
            sp.Children.Add(new TextBlock
            {
                Text = "Dosya modunda çalışılıyor (MSSQL gerekmiyor).",
                Foreground = Gri, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
        }
        int okunmamis = 0;
        var satirlar = new List<object>();
        foreach (var r in kayit)
        {
            bool okundu = r[3] == "1";
            if (!okundu) okunmamis++;
            satirlar.Add(new { Tarih = r[0], Tur = r[1], Mesaj = r[2], Durum = okundu ? "Okundu" : "YENİ" });
        }
        var kartlar = new WrapPanel();
        kartlar.Children.Add(Kart(okunmamis.ToString(), "Okunmamış"));
        kartlar.Children.Add(Kart(kayit.Count.ToString(), "Son 200 Kayıt"));
        sp.Children.Add(kartlar);
        var okunduBtn = ModernBtn("Tümünü okundu işaretle", true);
        okunduBtn.Margin = new Thickness(0, 0, 0, 10);
        okunduBtn.HorizontalAlignment = HorizontalAlignment.Left;
        okunduBtn.Click += (s, e) => { Db.Exec("UPDATE Alerts SET Okundu=1 WHERE Okundu=0"); Db.UyarilariOkunduIsaretle(); Goster("uyarilar"); };
        sp.Children.Add(okunduBtn);
        sp.Children.Add(Tablo(satirlar));
        return sp;
    }

    static UIElement SayfaPeriyot()
    {
        var sent = LoadSent().Where(s => s.Status == "OK").ToList();
        var sp = new StackPanel();
        sp.Children.Add(Baslik("Yazdırma Periyotları"));

        // Hizli secimler + tarih araligi
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        Action<string, DateTime> hizli = (ad_, f) =>
        {
            var b = ModernBtn(ad_, false);
            b.Margin = new Thickness(0, 0, 8, 0);
            b.Click += (s, e) => { pFrom = f; pTo = DateTime.Today; Goster("periyot"); };
            bar.Children.Add(b);
        };
        hizli("Bugün", DateTime.Today);
        hizli("Bu Hafta", DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7)));
        hizli("Bu Ay", new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        hizli("Son 30 Gün", DateTime.Today.AddDays(-29));
        var dpF = new DatePicker { SelectedDate = pFrom, Margin = new Thickness(16, 0, 6, 0) };
        var dpT = new DatePicker { SelectedDate = pTo, Margin = new Thickness(0, 0, 6, 0) };
        var uygula = ModernBtn("Filtrele", true);
        uygula.Click += (s, e) =>
        {
            if (dpF.SelectedDate.HasValue) pFrom = dpF.SelectedDate.Value;
            if (dpT.SelectedDate.HasValue) pTo = dpT.SelectedDate.Value;
            Goster("periyot");
        };
        bar.Children.Add(dpF); bar.Children.Add(dpT); bar.Children.Add(uygula);
        sp.Children.Add(bar);

        var to = pTo.Date.AddDays(1).AddSeconds(-1);
        var range = sent.Where(s => s.Time >= pFrom && s.Time <= to).ToList();

        var kartlar = new WrapPanel();
        kartlar.Children.Add(Kart(range.Count.ToString(), "Çıktı"));
        kartlar.Children.Add(Kart(range.Sum(s => s.PageN).ToString(), "Sayfa"));
        kartlar.Children.Add(Kart(range.Select(s => MK(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(), "Makine"));
        kartlar.Children.Add(Kart(range.Select(s => s.User).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(), "Kullanıcı"));
        sp.Children.Add(kartlar);

        sp.Children.Add(AltBaslik("Günlük Dağılım"));
        var byDay = range.GroupBy(s => s.Time.Date).OrderBy(g => g.Key).ToList();
        int max = byDay.Count > 0 ? byDay.Max(g => g.Count()) : 1;
        var gPanel = BeyazKutu();
        foreach (var g in byDay)
            ((StackPanel)gPanel.Child).Children.Add(BarSatir(
                g.Key.ToString("dd.MM ddd", new CultureInfo("tr-TR")), g.Count(), max,
                g.Count() + " çıktı / " + g.Sum(x => x.PageN) + " sf"));
        if (byDay.Count == 0) ((StackPanel)gPanel.Child).Children.Add(new TextBlock { Text = "Bu aralıkta kayıt yok.", Foreground = Gri });
        sp.Children.Add(gPanel);

        sp.Children.Add(AltBaslik("Saatlik Dağılım"));
        var byHour = range.GroupBy(s => s.Time.Hour).OrderBy(g => g.Key).ToList();
        int hmax = byHour.Count > 0 ? byHour.Max(g => g.Count()) : 1;
        var hPanel = BeyazKutu();
        foreach (var g in byHour)
            ((StackPanel)hPanel.Child).Children.Add(BarSatir(g.Key.ToString("00") + ":00", g.Count(), hmax, g.Count() + " çıktı"));
        if (byHour.Count == 0) ((StackPanel)hPanel.Child).Children.Add(new TextBlock { Text = "Kayıt yok.", Foreground = Gri });
        sp.Children.Add(hPanel);

        var iki = IkiSutun();
        var sol = (StackPanel)((Grid)iki).Children[0];
        var sag = (StackPanel)((Grid)iki).Children[1];
        sol.Children.Add(AltBaslik("Dönem İçi Makine Bazlı"));
        sol.Children.Add(Tablo(range.GroupBy(s => MK(s), StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count())
            .Select(g => new { Makine = g.Key, Cikti = g.Count(), Sayfa = g.Sum(x => x.PageN) }).ToList()));
        sag.Children.Add(AltBaslik("Dönem İçi Kağıt Türü"));
        sag.Children.Add(Tablo(range.GroupBy(s => s.Paper).OrderByDescending(g => g.Count())
            .Select(g => new { Kagit = g.Key, Cikti = g.Count(), Sayfa = g.Sum(x => x.PageN) }).ToList()));
        sp.Children.Add(iki);
        return sp;
    }

    static UIElement SayfaIsler(string filtre)
    {
        var sent = LoadSent();
        var printed = LoadPrinted();
        var sp = new StackPanel();
        sp.Children.Add(Baslik("Tüm İşler"));

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var tb = new TextBox
        {
            Width = 300, Padding = new Thickness(9, 7, 9, 7), Text = filtre,
            FontSize = 13, BorderBrush = Hex("#CDD7E5"), BorderThickness = new Thickness(1)
        };
        var ara = ModernBtn("Ara", true);
        ara.Margin = new Thickness(8, 0, 0, 0);
        ara.Click += (s, e) => icerik.Content = SayfaIsler(tb.Text.Trim());
        tb.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) icerik.Content = SayfaIsler(tb.Text.Trim()); };
        bar.Children.Add(tb); bar.Children.Add(ara);
        sp.Children.Add(bar);

        var list = Enumerable.Reverse(sent).AsEnumerable();
        if (filtre.Length > 0)
            list = list.Where(s => (s.User + " " + s.Machine + " " + s.Doc).IndexOf(filtre, StringComparison.OrdinalIgnoreCase) >= 0);
        sp.Children.Add(IsTablosu(list.Take(300), printed));
        return sp;
    }

    // ---------------- UI yardimcilari ----------------

    static TextBlock Baslik(string t)
    {
        return new TextBlock { Text = t, FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) };
    }

    static TextBlock AltBaslik(string t)
    {
        return new TextBlock { Text = t, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 6) };
    }

    static int kartSira = 0;
    static readonly string[] kartRenk = { "#3F77C9", "#2E9E6B", "#B26A00", "#7B5CC6", "#C05555", "#3A9FAF", "#6B7A90" };

    static Border Kart(string n, string t, string trend = null, Brush trendRenk = null)
    {
        var akcent = Hex(kartRenk[kartSira++ % kartRenk.Length]);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bar = new Border { Background = akcent, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 2, 12, 2) };
        Grid.SetColumn(bar, 0); g.Children.Add(bar);
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = n, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Koyu });
        s.Children.Add(new TextBlock { Text = t, FontSize = 12, Foreground = Gri });
        if (trend != null)
            s.Children.Add(new TextBlock { Text = trend, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = trendRenk ?? Gri, Margin = new Thickness(0, 3, 0, 0) });
        Grid.SetColumn(s, 1); g.Children.Add(s);
        return new Border
        {
            Background = Beyaz, CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 13, 24, 13), Margin = new Thickness(0, 0, 12, 12),
            MinWidth = 140, Child = g,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.10 }
        };
    }

    // Iki deger karsilastirmasindan trend metni + renk (bugun vs dun / bu hafta vs gecen)
    static string TrendMetni(double simdi, double onceki, out Brush renk)
    {
        if (onceki <= 0) { renk = Gri; return simdi > 0 ? "▲ yeni" : "—"; }
        double yuzde = (simdi - onceki) / onceki * 100.0;
        if (yuzde >= 1) { renk = Hex("#1a7f37"); return "▲ %" + Math.Round(yuzde) + " (dün " + onceki.ToString("0") + ")"; }
        if (yuzde <= -1) { renk = Hex("#c0392b"); return "▼ %" + Math.Round(-yuzde) + " (dün " + onceki.ToString("0") + ")"; }
        renk = Gri; return "= aynı (dün " + onceki.ToString("0") + ")";
    }

    static Border BeyazKutu()
    {
        return new Border
        {
            Background = Beyaz, CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14), Child = new StackPanel(),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.12 }
        };
    }

    static UIElement BarSatir(string etiket, int deger, int max, string yazi)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        var l = new TextBlock { Text = etiket, FontSize = 12, Foreground = Gri, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(l, 0); g.Children.Add(l);
        var barAlan = new Grid();
        double oran = max > 0 ? (double)deger / max : 0;
        barAlan.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(oran, 0.02), GridUnitType.Star) });
        barAlan.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - oran, 0.001), GridUnitType.Star) });
        var bar = new Border
        {
            Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#3F77C9"),
                (Color)ColorConverter.ConvertFromString("#6FB1FF"), 0),
            CornerRadius = new CornerRadius(4), Height = 16
        };
        Grid.SetColumn(bar, 0); barAlan.Children.Add(bar);
        Grid.SetColumn(barAlan, 1); g.Children.Add(barAlan);
        var v = new TextBlock { Text = yazi, FontSize = 12, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(v, 2); g.Children.Add(v);
        return g;
    }

    static Grid IkiSutun()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sol = new StackPanel { Margin = new Thickness(0, 0, 11, 0) };
        var sag = new StackPanel { Margin = new Thickness(11, 0, 0, 0) };
        Grid.SetColumn(sol, 0); Grid.SetColumn(sag, 1);
        g.Children.Add(sol); g.Children.Add(sag);
        return g;
    }

    // Saf WPF DataGrid (harici bilesen gerektirmez) - sutun basligina tiklayarak
    // siralama yerlesiktir; editable=true ise hucreler duzenlenebilir.
    static DataGrid Tablo(IEnumerable veri, bool editable = false)
    {
        return new DataGrid
        {
            ItemsSource = veri,
            AutoGenerateColumns = true,
            IsReadOnly = !editable,
            Margin = new Thickness(0, 0, 0, 6),
            MaxHeight = 640,
            FontSize = 13,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = Hex("#EEF1F6"),
            BorderThickness = new Thickness(1),
            BorderBrush = Hex("#DDE3EC"),
            Background = Brushes.White,
            RowBackground = Brushes.White,
            AlternatingRowBackground = Hex("#F7F9FC"),
            ColumnHeaderStyle = BaslikStili(),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    // Tablo sutun basligi gorunumu (panelin geri kalaniyla uyumlu)
    static Style BaslikStili()
    {
        var st = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        st.Setters.Add(new Setter(Control.BackgroundProperty, Hex("#E8EDF5")));
        st.Setters.Add(new Setter(Control.ForegroundProperty, Hex("#44556B")));
        st.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        st.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        st.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        st.Setters.Add(new Setter(Control.BorderBrushProperty, Hex("#D5DCE7")));
        st.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        st.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        return st;
    }

    static UIElement IsTablosu(IEnumerable<Sent> jobs, Dictionary<string, string[]> printed)
    {
        return Tablo(jobs.Select(s => new
        {
            Tarih = s.Time == DateTime.MinValue ? "" : s.Time.ToString("yyyy-MM-dd HH:mm:ss"),
            Belge = s.Doc.Length > 0 ? s.Doc : s.File,
            Kullanici = s.User, Makine = s.Machine, Sayfa = s.Pages, Kagit = s.Paper,
            Boyut = s.KbN > 0 ? (s.KbN >= 1024 ? Math.Round(s.KbN / 1024.0, 1) + " MB" : s.KbN + " KB") : "",
            Yazici = printed.ContainsKey(s.File) && printed[s.File].Length > 3 ? printed[s.File][3] : "",
            Durum = s.Status != "OK" ? s.Status.Replace("ENGEL:", "Engellendi:")
                  : (printed.ContainsKey(s.File) ? "Basıldı ✓" : "Gönderildi")
        }).ToList());
    }

    static Brush Hex(string h)
    {
        return (Brush)new BrushConverter().ConvertFromString(h);
    }

    static string MK(Sent s)
    {
        return s.Machine.Length > 0 ? s.Machine : ("(" + s.User + ")");
    }

    // Maliyet tanimlari (SQL Costs tablosu)
    static Dictionary<string, decimal> LoadCosts()
    {
        var d = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var dt = Db.Query("SELECT Kagit, Fiyat FROM Costs");
        if (dt != null)
            foreach (System.Data.DataRow r in dt.Rows)
                d[Convert.ToString(r[0])] = Convert.ToDecimal(r[1]);
        return d;
    }

    static decimal ToplamMaliyet(IEnumerable<Sent> list, Dictionary<string, decimal> costs)
    {
        decimal t = 0;
        foreach (var s in list)
        {
            decimal f;
            if (costs.TryGetValue(s.Paper, out f)) t += s.PageN * f;
            else if (costs.TryGetValue("*", out f)) t += s.PageN * f;
        }
        return t;
    }

    static string Para(decimal t)
    {
        return t.ToString("N2", new CultureInfo("tr-TR")) + " ₺";
    }

    // ---------------- Veri (web paneliyle ayni format) ----------------

    static List<Sent> LoadSent()
    {
        var dt = Db.Query("SELECT Tarih,Kullanici,Makine,Belge,Sayfa,Dosya,Kagit,KB,Durum FROM Jobs ORDER BY Id");
        if (dt != null)
        {
            var list = new List<Sent>();
            foreach (System.Data.DataRow r in dt.Rows)
            {
                var paper = Convert.ToString(r[6]);
                list.Add(new Sent
                {
                    Time = r[0] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(r[0]),
                    User = Convert.ToString(r[1]), Machine = Convert.ToString(r[2]),
                    Doc = Convert.ToString(r[3]),
                    PageN = r[4] == DBNull.Value ? 0 : Convert.ToInt32(r[4]),
                    Pages = r[4] == DBNull.Value ? "" : Convert.ToString(r[4]),
                    File = Convert.ToString(r[5]),
                    Paper = paper.Length > 0 ? paper : "Bilinmiyor",
                    KbN = r[7] == DBNull.Value ? 0 : Convert.ToInt32(r[7]),
                    Status = Convert.ToString(r[8]).Length > 0 ? Convert.ToString(r[8]) : "OK"
                });
            }
            return list;
        }
        return ReadCsv(jobsCsv).Where(r => r.Length >= 6).Select(r =>
        {
            DateTime t;
            DateTime.TryParseExact(r[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
            int p; int.TryParse(r[4], out p);
            int kb = 0; if (r.Length > 7) int.TryParse(r[7], out kb);
            return new Sent { Time = t, User = r[1], Machine = r[2], Doc = r[3], Pages = r[4], File = r[5],
                              Paper = r.Length > 6 && r[6].Length > 0 ? r[6] : "Bilinmiyor", PageN = p, KbN = kb,
                              Status = r.Length > 8 && r[8].Length > 0 ? r[8] : "OK" };
        }).ToList();
    }

    static Dictionary<string, string[]> LoadPrinted()
    {
        var d = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var dt = Db.Query("SELECT Tarih,Makine,Dosya,Yazici,Durum FROM Printed WHERE Durum='OK'");
        if (dt != null)
        {
            foreach (System.Data.DataRow r in dt.Rows)
                d[Convert.ToString(r[2])] = new[] {
                    r[0] == DBNull.Value ? "" : Convert.ToDateTime(r[0]).ToString("yyyy-MM-dd HH:mm:ss"),
                    Convert.ToString(r[1]), Convert.ToString(r[2]), Convert.ToString(r[3]), "OK" };
            return d;
        }
        if (Directory.Exists(clientsDir))
            foreach (var f in Directory.GetFiles(clientsDir, "*.csv"))
                foreach (var r in ReadCsv(f).Where(r => r.Length >= 5 && r[4] == "OK"))
                    d[r[2]] = r;
        return d;
    }

    static List<AdPc> LoadAd()
    {
        if (adCache != null && (DateTime.Now - adCacheTime).TotalMinutes < 5) return adCache;
        var list = new List<AdPc>();
        adError = null;
        try
        {
            using (var root = new DirectoryEntry())
            using (var ds = new DirectorySearcher(root, "(objectCategory=computer)",
                       new[] { "name", "operatingSystem", "lastLogonTimestamp" }))
            {
                ds.PageSize = 500;
                foreach (SearchResult r in ds.FindAll())
                {
                    var pc = new AdPc
                    {
                        Name = r.Properties.Contains("name") ? Convert.ToString(r.Properties["name"][0]) : "",
                        Os = r.Properties.Contains("operatingsystem") ? Convert.ToString(r.Properties["operatingsystem"][0]) : ""
                    };
                    if (r.Properties.Contains("lastlogontimestamp"))
                        try { pc.LastLogon = DateTime.FromFileTime((long)r.Properties["lastlogontimestamp"][0]); } catch { }
                    list.Add(pc);
                }
            }
        }
        catch (Exception ex) { adError = ex.Message; }
        adCache = list; adCacheTime = DateTime.Now;
        return list;
    }

    // Active Directory kullanici listesi (5 dk onbellek)
    static List<AdUser> LoadAdUsers()
    {
        if (adUserCache != null && (DateTime.Now - adUserCacheTime).TotalMinutes < 5) return adUserCache;
        var list = new List<AdUser>();
        try
        {
            using (var root = new DirectoryEntry())
            using (var ds = new DirectorySearcher(root, "(&(objectCategory=person)(objectClass=user))",
                       new[] { "samaccountname", "displayname", "lastlogontimestamp", "useraccountcontrol" }))
            {
                ds.PageSize = 500;
                foreach (SearchResult r in ds.FindAll())
                {
                    var u = new AdUser
                    {
                        Sam = r.Properties.Contains("samaccountname") ? Convert.ToString(r.Properties["samaccountname"][0]) : "",
                        Ad = r.Properties.Contains("displayname") ? Convert.ToString(r.Properties["displayname"][0]) : "",
                        Aktif = true
                    };
                    if (r.Properties.Contains("useraccountcontrol"))
                    {
                        int uac; int.TryParse(Convert.ToString(r.Properties["useraccountcontrol"][0]), out uac);
                        u.Aktif = (uac & 2) == 0;
                    }
                    if (r.Properties.Contains("lastlogontimestamp"))
                        try { u.LastLogon = DateTime.FromFileTime((long)r.Properties["lastlogontimestamp"][0]); } catch { }
                    list.Add(u);
                }
            }
        }
        catch (Exception ex) { adError = ex.Message; }
        adUserCache = list; adUserCacheTime = DateTime.Now;
        return list;
    }

    static List<string[]> ReadCsv(string path)
    {
        var rows = new List<string[]>();
        if (!File.Exists(path)) return rows;
        string text;
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
                text = sr.ReadToEnd();
        }
        catch { return rows; }
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = new List<string>();
            var cur = new StringBuilder();
            bool inQ = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQ)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else if (c == '"') inQ = false;
                    else cur.Append(c);
                }
                else
                {
                    if (c == '"') inQ = true;
                    else if (c == ',') { fields.Add(cur.ToString()); cur.Length = 0; }
                    else cur.Append(c);
                }
            }
            fields.Add(cur.ToString());
            rows.Add(fields.ToArray());
        }
        return rows;
    }
}
