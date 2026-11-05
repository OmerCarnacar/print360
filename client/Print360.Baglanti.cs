// ============================================================
//  Print360 Client - SUNUCU BAGLANTI ARACI
//  Gelistirici : Omer CARNACAR
//  Lisans      : GPL-3.0-or-later, acik kaynak (bkz. LICENSE, NOTICE)
//
//  Sunucuyu ve portunu tarar; bulunamazsa adres/port elle girilir. Sonuc
//  baglanti.ini'ye kaydedilir. Print360.ini kullanicilara SALT OKUNURDUR (ajan
//  kullanici hesabiyla calisir); baglanti.ini ise kullanici tarafindan yazilabilir
//  ve yalnizca baglanti ayarlarini ezer (bkz. RdpAlgilama.BaglantiAnahtarlari).
// ============================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

static partial class ClientAgent
{
    static string BaglantiDosyasi { get { return Path.Combine(Path.GetDirectoryName(iniFile), "baglanti.ini"); } }

    static void BaglantiAyariYaz(Dictionary<string, string> degerler)
    {
        string yol = BaglantiDosyasi;
        var eski = File.Exists(yol) ? File.ReadAllLines(yol, System.Text.Encoding.UTF8) : new string[0];
        File.WriteAllLines(yol, RdpAlgilama.AyarlariYaz(eski, degerler).ToArray(), System.Text.Encoding.UTF8);
    }

    // Secilen adresi kaydeder ve hemen kullanir. Ayni makinenin eski adresleri unutulur.
    internal static void BaglantiKaydet(string url)
    {
        var u = new Uri(url);
        BaglantiAyariYaz(new Dictionary<string, string> {
            { "Server", u.Host + ":" + u.Port }, { "Port", "" },
            { "UseHttps", u.Scheme == "https" ? "1" : "0" } });
        lock (sonCalisanSunucular)
            sonCalisanSunucular.RemoveAll(x => { Uri k; return Uri.TryCreate(x, UriKind.Absolute, out k) && k.Host.Equals(u.Host, StringComparison.OrdinalIgnoreCase); });
        try
        {
            if (File.Exists(BulunanDosya))
                File.WriteAllLines(BulunanDosya, File.ReadAllLines(BulunanDosya).Where(x =>
                { Uri k; return !Uri.TryCreate(x.Trim(), UriKind.Absolute, out k) || !k.Host.Equals(u.Host, StringComparison.OrdinalIgnoreCase); }).ToArray());
        }
        catch { }
        saglik.Sifirla();
        BulunanEkle(url);
        Log("BAGLANTI KAYDEDILDI: " + url + " (" + BaglantiDosyasi + ")");
        hbTetik.Set();
    }

    // Acilista: sunucuya baglanilamadiysa ve gercekten gerekliyse araci ac. RDP acik degil ve
    // adres zaten kayitliysa acilmaz (dizustu evde, VPN kapali: her oturumda pencere cikmasin).
    static void AcilisBaglantiKontrolu()
    {
        Thread.Sleep(25000);   // normal yollar ve arka plan taramasi once denesin
        try
        {
            var cfg = ReadIni();
            if (cfg.ContainsKey("BaglantiAraci") && cfg["BaglantiAraci"].Trim() == "0") return;
            if (sunucuBagli > 0) return;
            bool adresVar = (cfg.ContainsKey("Server") && cfg["Server"].Trim().Length > 0) || BilinenAdresler().Count > 0;
            if (adresVar && !rdpAcik) return;
            Log("Sunucuya baglanilamadi: baglanti araci aciliyor.");
            var s = senkron;
            if (s != null && s.IsHandleCreated) s.BeginInvoke((Action)delegate { BaglantiAraciAc(true); });
        }
        catch (Exception ex) { Log("Baglanti araci: " + ex.Message); }
    }

    static BaglantiFormu baglantiFormu;

    static void BaglantiAraciAc(bool otomatik)
    {
        try
        {
            if (baglantiFormu == null || baglantiFormu.IsDisposed) baglantiFormu = new BaglantiFormu(otomatik);
            baglantiFormu.Show();
            baglantiFormu.Activate();
        }
        catch (Exception ex) { Log("Baglanti araci acilamadi: " + ex.Message); }
    }

    class BaglantiFormu : Form
    {
        readonly TextBox txtAdres = new TextBox(), txtPort = new TextBox();
        readonly CheckBox chkTam = new CheckBox(), chkSorma = new CheckBox();
        readonly Button btnTara = new Button(), btnBaglan = new Button(), btnKapat = new Button();
        readonly ListBox liste = new ListBox();
        readonly ProgressBar bar = new ProgressBar();
        readonly Label lblDurum = new Label();
        volatile bool iptal, calisiyor;
        readonly bool otomatik;

        public BaglantiFormu(bool otomatik)
        {
            this.otomatik = otomatik;
            Text = "Print360 - Sunucu Baglantisi";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 410);
            try { Icon = SimgeYukle(); } catch { }

            var bilgi = new Label { Text = "Print360 sunucusu ve portu otomatik taranir. Bulunamazsa adresi ve portu asagiya yazip "
                                         + "\"Baglan ve kaydet\" deyin. Buradaki port RDP portu DEGIL, Print360 panel portudur." };
            bilgi.SetBounds(14, 12, 492, 34);
            var lblA = new Label { Text = "Sunucu adresi (bos = tumunu tara):" };
            lblA.SetBounds(14, 54, 300, 18);
            txtAdres.SetBounds(14, 74, 360, 24);
            var lblP = new Label { Text = "Port (bos = tara):" };
            lblP.SetBounds(386, 54, 120, 18);
            txtPort.SetBounds(386, 74, 120, 24);
            chkTam.Text = "Bulunamazsa tum portlari tara (makine basina yaklasik 2,5 dk)";
            chkTam.Checked = true;
            chkTam.SetBounds(14, 104, 492, 22);
            btnTara.Text = "Tara"; btnTara.SetBounds(14, 132, 100, 30);
            btnBaglan.Text = "Baglan ve kaydet"; btnBaglan.SetBounds(122, 132, 140, 30);
            bar.SetBounds(14, 172, 492, 16);
            lblDurum.SetBounds(14, 192, 492, 36);
            var lblL = new Label { Text = "Bulunan sunucular:" };
            lblL.SetBounds(14, 232, 300, 18);
            liste.SetBounds(14, 252, 492, 90);
            chkSorma.Text = "Acilista bu pencereyi bir daha acma";
            chkSorma.SetBounds(14, 370, 330, 24);
            btnKapat.Text = "Kapat"; btnKapat.SetBounds(406, 366, 100, 30);
            Controls.AddRange(new Control[] { bilgi, lblA, txtAdres, lblP, txtPort, chkTam, btnTara, btnBaglan, bar, lblDurum, lblL, liste, chkSorma, btnKapat });

            try
            {
                var cfg = ReadIni();
                string host, port;
                string sv = cfg.ContainsKey("Server") ? cfg["Server"].Split(',', ';')[0] : "";
                if (RdpAlgilama.AdresAyir(RdpAlgilama.SunucuGecerli(sv, RdpPortlari()), out host, out port)) { txtAdres.Text = host; txtPort.Text = port; }
                chkSorma.Checked = cfg.ContainsKey("BaglantiAraci") && cfg["BaglantiAraci"].Trim() == "0";
            }
            catch { }

            btnTara.Click += delegate { if (calisiyor) { iptal = true; btnTara.Enabled = false; } else TaramaBaslat(false); };
            btnBaglan.Click += delegate { Baglan(); };
            btnKapat.Click += delegate { Close(); };
            liste.SelectedIndexChanged += delegate { btnBaglan.Enabled = true; };
            chkSorma.CheckedChanged += delegate
            {
                try { BaglantiAyariYaz(new Dictionary<string, string> { { "BaglantiAraci", chkSorma.Checked ? "0" : "1" } }); }
                catch (Exception ex) { Durum("Ayar kaydedilemedi: " + ex.Message); }
            };
            FormClosing += delegate { iptal = true; aracTariyor = false; };
            Shown += delegate { if (otomatik) TaramaBaslat(true); };
        }

        void Arayuzde(Action a)
        {
            try { if (IsDisposed || !IsHandleCreated) return; if (InvokeRequired) BeginInvoke(a); else a(); } catch { }
        }

        void Durum(string m) { Arayuzde(delegate { lblDurum.Text = m; }); }

        // Otomatik acilista kayitli adres yanlis olabilir: tum hedefler taranir.
        void TaramaBaslat(bool hepsi)
        {
            calisiyor = true; iptal = false; aracTariyor = true;
            btnTara.Text = "Durdur";
            liste.Items.Clear();
            bar.Style = ProgressBarStyle.Marquee;
            string adres = hepsi ? "" : txtAdres.Text.Trim(), portMetin = hepsi ? "" : txtPort.Text.Trim();
            bool tam = chkTam.Checked;
            new Thread(delegate ()
            {
                string sonuc = null;
                int bulunan = 0;
                try { bulunan = Tara(adres, portMetin, tam, out sonuc); }
                catch (Exception ex) { Durum("Tarama hatasi: " + ex.Message); }
                Arayuzde(delegate
                {
                    calisiyor = false; aracTariyor = false;
                    btnTara.Text = "Tara"; btnTara.Enabled = true;
                    bar.Style = ProgressBarStyle.Blocks; bar.Value = 0;
                    if (iptal) lblDurum.Text = "Tarama durduruldu.";
                    else if (bulunan == 0) lblDurum.Text = "Print360 sunucusu bulunamadi. Sunucunun acik oldugunu kontrol edin ya da adresi ve portu elle yazip \"Baglan ve kaydet\" deyin.";
                    else if (bulunan == 1 && otomatik) Kaydet(sonuc);
                    else lblDurum.Text = bulunan + " sunucu bulundu. Listeden secip \"Baglan ve kaydet\" deyin.";
                });
            }) { IsBackground = true }.Start();
        }

        void Bulundu(string url)
        {
            Arayuzde(delegate { if (!liste.Items.Contains(url)) liste.Items.Add(url); if (liste.SelectedIndex < 0) liste.SelectedIndex = 0; });
        }

        int Tara(string adres, string portMetin, bool tam, out string ilk)
        {
            ilk = null;
            var cfg = ReadIni();
            var rdp = RdpPortlari();
            List<string> dogrudan, tailscale, vpnAgi;
            string host, adresPort;
            if (RdpAlgilama.AdresAyir(adres, out host, out adresPort))
            {
                dogrudan = new List<string> { host }; tailscale = new List<string>(); vpnAgi = new List<string>();
                if (portMetin.Length == 0) portMetin = adresPort;
            }
            else AramaHedefleri(cfg, out dogrudan, out tailscale, out vpnAgi);

            var hedefler = dogrudan.Concat(tailscale).Concat(vpnAgi).Distinct().ToList();
            if (hedefler.Count == 0)
            {
                Durum("Taranacak makine yok: RDP oturumu ya da VPN gorunmuyor. Sunucu adresini yazin.");
                return 0;
            }
            var bulunan = new List<string>();
            int tek;
            var portlar = int.TryParse(portMetin, out tek) ? new List<int> { tek }
                        : RdpAlgilama.HizliPortlar(RdpAlgilama.GecerliPort(cfg.ContainsKey("Port") ? cfg["Port"] : "", rdp));
            Durum(hedefler.Count + " makinede " + portlar.Count + " port taraniyor...");
            foreach (var u in HizliTara(hedefler, portlar, () => iptal)) { bulunan.Add(u); Bulundu(u); }

            if (bulunan.Count == 0 && tam && !iptal && !int.TryParse(portMetin, out tek))
            {
                var derin = dogrudan.Concat(tailscale).Distinct().Take(TamTaramaMakineSiniri).ToList();
                Arayuzde(delegate { bar.Style = ProgressBarStyle.Blocks; bar.Maximum = 65535; bar.Value = 0; });
                foreach (var h in derin)
                {
                    if (iptal) break;
                    string hh = h;
                    Durum(hh + ": tum portlar taraniyor...");
                    Arayuzde(delegate { bar.Value = 0; });
                    string u = HostTara(hh, Enumerable.Range(1, 65535), () => iptal,
                        n => Arayuzde(delegate { bar.Value = Math.Min(n, 65535); lblDurum.Text = hh + ": tum portlar taraniyor (" + n + " / 65535)"; }));
                    if (u != null) { bulunan.Add(u); Bulundu(u); break; }
                }
            }
            if (bulunan.Count > 0) ilk = bulunan[0];
            return bulunan.Count;
        }

        void Baglan()
        {
            string secili = liste.SelectedItem as string;
            string host, adresPort;
            if (secili != null && txtPort.Text.Trim().Length == 0) { Kaydet(secili); return; }
            if (!RdpAlgilama.AdresAyir(txtAdres.Text, out host, out adresPort))
            {
                if (secili != null) { Kaydet(secili); return; }
                MessageBox.Show(this, "Sunucu adresini yazin ya da once tarayin.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string pm = txtPort.Text.Trim().Length > 0 ? txtPort.Text.Trim() : adresPort;
            int port = 0;
            if (pm.Length > 0 && (!int.TryParse(pm, out port) || port < 1 || port > 65535))
            {
                MessageBox.Show(this, "Port 1-65535 arasinda bir sayi olmali.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (port > 0 && RdpPortlari().Contains(port))
            {
                MessageBox.Show(this, port + " bu bilgisayarin kullandigi bir RDP portu. Buraya Print360 panel portu yazilmalidir "
                    + "(varsayilan 8443). Bilmiyorsaniz portu bos birakin, taransin.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            btnBaglan.Enabled = false;
            Durum("Baglanti deneniyor: " + host + (port > 0 ? ":" + port : "") + " ...");
            int p = port; string h = host;
            new Thread(delegate ()
            {
                string url = null;
                try { url = p > 0 ? HostTara(h, new[] { p }) : HostTara(h, RdpAlgilama.HizliPortlar("")); }
                catch { }
                Arayuzde(delegate
                {
                    btnBaglan.Enabled = true;
                    if (url != null) { Kaydet(url); return; }
                    if (p == 0) { lblDurum.Text = h + " uzerinde Print360 bulunamadi. Portu yazin ya da \"Tara\" ile tum portlara baktirin."; return; }
                    var c = MessageBox.Show(this, h + ":" + p + " Print360 olarak yanit vermedi (kapali, engelli ya da baska bir servis).\r\n\r\nYine de kaydedilsin mi?",
                                            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (c == DialogResult.Yes) Kaydet("https://" + h + ":" + p);
                    else lblDurum.Text = "Kaydedilmedi.";
                });
            }) { IsBackground = true }.Start();
        }

        void Kaydet(string url)
        {
            try
            {
                BaglantiKaydet(url);
                lblDurum.Text = "Kaydedildi: " + url + ". Istemci bu adrese baglaniyor; durum penceresinden izleyebilirsiniz.";
            }
            catch (Exception ex)
            {
                lblDurum.Text = "Kaydedilemedi: " + ex.Message;
                Log("BAGLANTI KAYDEDILEMEDI: " + url + " | " + ex.Message);
            }
        }
    }
}
