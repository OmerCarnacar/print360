// REGRESYON TESTI: panel sunucusu zamanlanmis gorevi
//
// Panel sunucusu (web paneli + HTTPS istemcilerinin is aldigi /api/jobs)
// eskiden HKLM\Run ile oturum acan ILK kullanicinin oturumunda calisiyordu;
// o kullanici cikis yapinca panel ve is dagitimi da kapaniyordu. Artik SYSTEM
// olarak acilista ve 5 dakikada bir (bekci) tetiklenen bir gorevle calisir.
//
// Bu test kurulumun URETTIGI XML'i (Kurulum.PanelGoreviXml) sinar:
//   1) yapisal kurallar (SYSTEM, sure siniri yok, tek kopya, bekci),
//   2) Windows Gorev Zamanlayici'nin XML'i GERCEKTEN kabul ettigi: gecici bir
//      adla, bu kullanici adina kaydedilip geri okunur ve silinir. (SYSTEM ve
//      acilis tetikleyicisi yonetici gerektirdigi icin yalnizca o iki oge
//      kullanici esdegerleriyle degistirilir; geri kalan her sey ayni XML'dir.)
//
//   derleme: csc /main:PanelGoreviTest tests\PanelGorevi.cs setup\Print360.Setup.cs ...
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;

class PanelGoreviTest
{
    static int gecti, toplam;
    static void Kontrol(bool ok, string ad, string ayrinti)
    {
        toplam++; if (ok) gecti++;
        Console.WriteLine((ok ? "GECTI " : "KALDI ") + ad);
        if (!ok && ayrinti != null) Console.WriteLine("       -> " + ayrinti);
    }

    static string Tek(XmlDocument d, XmlNamespaceManager ns, string yol)
    {
        var n = d.SelectSingleNode(yol, ns);
        return n == null ? null : n.InnerText;
    }

    static int Calistir(string arg, out string cikti)
    {
        var psi = new ProcessStartInfo("schtasks.exe", arg)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using (var p = Process.Start(psi))
        {
            cikti = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(30000);
            return p.ExitCode;
        }
    }

    static int Main()
    {
        const string exe = @"C:\Print360\Print360.Dashboard.exe";
        string xml = Kurulum.PanelGoreviXml(exe);

        var d = new XmlDocument();
        bool iyiBicim = true;
        try { d.LoadXml(xml); } catch (Exception ex) { iyiBicim = false; Console.WriteLine("       -> " + ex.Message); }
        Kontrol(iyiBicim, "XML iyi bicimli", null);
        if (!iyiBicim) { Console.WriteLine("\nSonuc: " + gecti + "/" + toplam); return 1; }

        var ns = new XmlNamespaceManager(d.NameTable);
        ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");

        Kontrol(Tek(d, ns, "//t:Principal/t:UserId") == "S-1-5-18", "SYSTEM olarak calisir (oturumdan bagimsiz)", Tek(d, ns, "//t:Principal/t:UserId"));
        Kontrol(Tek(d, ns, "//t:ExecutionTimeLimit") == "PT0S", "sure siniri yok (varsayilan 72 saatte OLDURULURDU)", Tek(d, ns, "//t:ExecutionTimeLimit"));
        Kontrol(Tek(d, ns, "//t:MultipleInstancesPolicy") == "IgnoreNew", "calisiyorsa ikinci kopya baslatilmaz", null);
        Kontrol(d.SelectSingleNode("//t:BootTrigger", ns) != null, "acilista baslar (kimse oturum acmadan)", null);
        Kontrol(Tek(d, ns, "//t:TimeTrigger/t:Repetition/t:Interval") == "PT5M",
                "bekci: 5 dakikada bir (TimeTrigger'a bagli - BootTrigger'a degil)", null);
        Kontrol(d.SelectSingleNode("//t:BootTrigger/t:Repetition", ns) == null,
                "tekrar BootTrigger'da DEGIL (orada ancak bir sonraki acilista baslardi)", null);
        Kontrol(Tek(d, ns, "//t:Exec/t:Command") == exe, "dogru exe calistirilir", Tek(d, ns, "//t:Exec/t:Command"));

        // --- Windows Gorev Zamanlayici XML'i kabul ediyor mu? (gercek kayit)
        string ad = "Print360-Test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string kullanici = Environment.UserDomainName + "\\" + Environment.UserName;
        var k = (XmlDocument)d.Clone();
        var p = k.SelectSingleNode("//t:Principal", ns);
        p.InnerXml = "<UserId xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" + SecurityElement(kullanici) + "</UserId>"
                   + "<LogonType xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">InteractiveToken</LogonType>"
                   + "<RunLevel xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">LeastPrivilege</RunLevel>";
        var boot = k.SelectSingleNode("//t:BootTrigger", ns);
        boot.ParentNode.RemoveChild(boot);
        k.SelectSingleNode("//t:Exec/t:Command", ns).InnerText = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var w = k.SelectSingleNode("//t:Exec/t:WorkingDirectory", ns);
        if (w != null) w.InnerText = Path.GetTempPath();
        // pasif: test kaydi hicbir zaman calismasin
        k.SelectSingleNode("//t:Settings/t:Enabled", ns).InnerText = "false";

        string dosya = Path.Combine(Path.GetTempPath(), ad + ".xml");
        File.WriteAllText(dosya, k.OuterXml, new UnicodeEncoding(false, true));
        string cikti;
        int rc = -1;
        try { rc = Calistir("/Create /TN \"" + ad + "\" /XML \"" + dosya + "\"", out cikti); }
        catch (Exception ex) { cikti = ex.Message; }
        if (rc == -1 && cikti.IndexOf("schtasks", StringComparison.OrdinalIgnoreCase) < 0 && !File.Exists(Path.Combine(Environment.SystemDirectory, "schtasks.exe")))
            Console.WriteLine("ATLA   Gorev Zamanlayici yok (" + cikti.Trim() + ")");
        else
        {
            Kontrol(rc == 0, "Windows Gorev Zamanlayici XML'i kabul etti (gecici kayit)", cikti.Trim());
            if (rc == 0)
            {
                string geri;
                Calistir("/Query /TN \"" + ad + "\" /XML", out geri);
                Kontrol(geri.Contains("PT5M") && geri.Contains("IgnoreNew") && geri.Contains("PT0S"),
                        "kayitli gorev ayarlari korunmus (geri okundu)", null);
                Calistir("/Delete /TN \"" + ad + "\" /F", out geri);
            }
        }
        try { File.Delete(dosya); } catch { }

        Console.WriteLine();
        Console.WriteLine("Sonuc: " + gecti + "/" + toplam);
        return gecti == toplam ? 0 : 1;
    }

    static string SecurityElement(string s) { return System.Security.SecurityElement.Escape(s); }
}
