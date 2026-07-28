// ============================================================
//  Print360 - RDP Yazdirma ve Yonetim Cozumu
//  Gelistirici : Omer CARNACAR  <omer.carnacar@outlook.com.tr>
//  LinkedIn    : https://www.linkedin.com/in/omercarnacar/
//  Lisans      : UCRETSIZ SURUM - para ile satilamaz (bkz. LICENSE)
//  Telif       : (c) 2026 Omer CARNACAR
// ============================================================
// Print360 - MSSQL veritabani katmani (ServerAgent, Dashboard ve Panel'e birlikte derlenir)
// Baglanti ayarlari: C:\Print360\db.ini  (Server=, Database=, User=, Password=)
// SQL erisilemezse cagiran kod CSV'ye geri duser (cift kayit stratejisi).
using System;
using System.Data;
using System.Data.SqlClient;
using System.IO;

static class Db
{
    static string cs;
    public static string Err;
    // Panel dinleme portlari (db.ini'den okunur; varsayilan 8360/8443)
    public static string HttpPort = "8360", HttpsPort = "8443";
    // RDP Virtual Channel ile is tasima (kanal mantigi) - VARSAYILAN ACIK.
    // Kanal 1. tercihtir: is RDP tunelinden gider, IP/port/HTTPS/firewall gerekmez.
    // Istemcide eklenti yoksa kanal acilmaz, sistem OTOMATIK HTTPS kuyruguna duser.
    // db.ini'de VirtualChannel=0 ile tamamen kapatilabilir.
    public static bool VChannelAcik = true;
    // RDP oturumunda hangi Print360 yazicisi VARSAYILAN olsun (db.ini VarsayilanYazici=):
    //   dogrudan (varsayilan) = "Print360 - <user>"      -> istemcinin VARSAYILAN yazicisina sessiz baski
    //   sec                   = "Print360 Yazici Sec.."  -> yazici secim penceresi acilir
    //   pdf                   = "Print360 PDF - <user>"  -> istemcide PDF olarak acilir
    public static string VarsayilanYaziciModu = "dogrudan";

    public static string ConnStr()
    {
        if (cs != null) return cs;
        // Varsayilanlar: yerel makine + BOS sifre. MSSQL istege baglidir; bilgiler
        // db.ini'de yoksa baglanti kurulamaz ve sistem otomatik SQLite'a duser.
        // (Koda gomulu varsayilan sifre BIRAKILMAZ - guvenlik.)
        string server = Environment.MachineName, db = "Print360", user = "sa", pwd = "";
        try
        {
            string f = @"C:\Print360\db.ini";
            if (File.Exists(f))
                foreach (var l in File.ReadAllLines(f))
                {
                    var p = l.Split(new[] { '=' }, 2);
                    if (p.Length != 2) continue;
                    var k = p[0].Trim().ToLowerInvariant(); var v = p[1].Trim();
                    if (k == "server") server = v;
                    else if (k == "database") db = v;
                    else if (k == "user") user = v;
                    else if (k == "password") pwd = v;
                    else if (k == "httpport" && v.Length > 0) HttpPort = v;
                    else if (k == "httpsport" && v.Length > 0) HttpsPort = v;
                    else if (k == "virtualchannel") VChannelAcik = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "varsayilanyazici" && v.Length > 0) VarsayilanYaziciModu = v.ToLowerInvariant();
                }
        }
        catch { }
        cs = "Server=" + server + ";Database=" + db + ";User ID=" + user + ";Password=" + pwd + ";Connect Timeout=4";
        return cs;
    }

    // ============================================================
    //  IKI MOTORLU VERI KATMANI
    //    MsSql  : db.ini'deki MSSQL sunucusuna baglanilabiliyorsa
    //    Sqlite : MSSQL yoksa  ->  C:\Print360\print360.db  (KURULUM GEREKMEZ)
    //  Cagiran kodun tamami T-SQL yazar; SQLite'a giderken CevirSqlite()
    //  ile lehce farklari (TOP, GETDATE, ISNULL, IF EXISTS...) cevrilir.
    //  Ikisi de yoksa CSV/dosya deposu devreye girer (mevcut davranis).
    // ============================================================
    public enum DbMotor { Yok, MsSql, Sqlite }
    static DbMotor _motor = DbMotor.Yok;
    static bool _motorBelirlendi;
    static readonly object _mKilit = new object();
    public const string SqliteDosya = @"C:\Print360\print360.db";

    public static DbMotor Motor { get { MotorBelirle(false); return _motor; } }
    public static string MotorAdi
    {
        get
        {
            switch (Motor)
            {
                case DbMotor.MsSql: return "MSSQL";
                case DbMotor.Sqlite: return "SQLite (yerel dosya)";
                default: return "Dosya (CSV)";
            }
        }
    }

    // MSSQL yeniden denensin diye (panelden SQL kurulunca) sifirlanabilir
    public static void MotorSifirla() { lock (_mKilit) { _motorBelirlendi = false; cs = null; } }

    static void MotorBelirle(bool zorla)
    {
        lock (_mKilit)
        {
            if (_motorBelirlendi && !zorla) return;
            _motorBelirlendi = true;
            // 1) MSSQL
            try
            {
                using (var c = new SqlConnection(ConnStr())) c.Open();
                _motor = DbMotor.MsSql; Err = null; return;
            }
            catch (Exception ex) { Err = ex.Message; }
            // 2) SQLite (yerel dosya - hicbir kurulum gerektirmez)
            try
            {
                SqliteHazirla();
                _motor = DbMotor.Sqlite; return;
            }
            catch (Exception ex)
            {
                Err = "SQLite baslatilamadi: " + ex.Message;
                _motor = DbMotor.Yok;
            }
        }
    }

    static System.Data.SQLite.SQLiteConnection SqliteAc()
    {
        var c = new System.Data.SQLite.SQLiteConnection(
            "Data Source=" + SqliteDosya + ";Version=3;BusyTimeout=5000;");
        c.Open();
        return c;
    }

    static void SqliteHazirla()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SqliteDosya));
        using (var c = SqliteAc())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = SQLITE_SEMA;
            cmd.ExecuteNonQuery();
        }
    }

    // SQLite semasi (T-SQL semasinin karsiligi)
    const string SQLITE_SEMA = @"
CREATE TABLE IF NOT EXISTS Jobs(Id INTEGER PRIMARY KEY AUTOINCREMENT, Tarih TEXT NOT NULL,
  Kullanici TEXT, Makine TEXT, Belge TEXT, Sayfa INTEGER DEFAULT 0, Dosya TEXT, Kagit TEXT,
  KB INTEGER DEFAULT 0, Durum TEXT DEFAULT 'OK');
CREATE TABLE IF NOT EXISTS Printed(Id INTEGER PRIMARY KEY AUTOINCREMENT, Tarih TEXT, Makine TEXT,
  Dosya TEXT, Yazici TEXT, Durum TEXT DEFAULT 'OK');
CREATE TABLE IF NOT EXISTS Heartbeat(Makine TEXT PRIMARY KEY, SonGorulme TEXT, Yazici TEXT,
  IP TEXT, KullaniciAdi TEXT, OS TEXT);
CREATE TABLE IF NOT EXISTS ConnLog(Id INTEGER PRIMARY KEY AUTOINCREMENT, Tarih TEXT, Olay TEXT);
CREATE TABLE IF NOT EXISTS Rules(Tip TEXT NOT NULL, Ad TEXT NOT NULL, Engel INTEGER NOT NULL DEFAULT 0,
  Kota INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(Tip,Ad));
CREATE TABLE IF NOT EXISTS PanelUsers(Kullanici TEXT PRIMARY KEY, SifreHash TEXT NOT NULL, Rol TEXT DEFAULT 'admin');
CREATE TABLE IF NOT EXISTS Alerts(Id INTEGER PRIMARY KEY AUTOINCREMENT, Tarih TEXT, Tur TEXT,
  Mesaj TEXT, Okundu INTEGER DEFAULT 0);
CREATE TABLE IF NOT EXISTS ClientKeys(Makine TEXT PRIMARY KEY, AnahtarHash TEXT NOT NULL,
  Olusturma TEXT DEFAULT (datetime('now','localtime')));
CREATE TABLE IF NOT EXISTS Costs(Kagit TEXT PRIMARY KEY, Fiyat REAL NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS PrinterHealth(Makine TEXT NOT NULL, Yazici TEXT NOT NULL, Durum TEXT,
  Hata TEXT, Kuyruk INTEGER DEFAULT 0, Guncelleme TEXT, PRIMARY KEY(Makine,Yazici));
CREATE TABLE IF NOT EXISTS JobQueue(Id INTEGER PRIMARY KEY AUTOINCREMENT, Makine TEXT, Dosya TEXT,
  Yol TEXT, BoyutKB INTEGER DEFAULT 0, SikistirilmisKB INTEGER DEFAULT 0, Olusturma TEXT,
  Durum TEXT DEFAULT 'BEKLIYOR', Alinma TEXT NULL);
CREATE INDEX IF NOT EXISTS ix_jobs_tarih ON Jobs(Tarih);
CREATE INDEX IF NOT EXISTS ix_printed_dosya ON Printed(Dosya);
CREATE INDEX IF NOT EXISTS ix_queue_makine ON JobQueue(Makine,Durum);";

    // ---- T-SQL -> SQLite lehce cevirisi ----
    // Kod tabaninda kullanilan yapilar sinirlidir; hepsi burada karsilanir.
    static string CevirSqlite(string sql)
    {
        string s = sql;
        // SELECT TOP n ...  ->  SELECT ... LIMIT n
        var m = System.Text.RegularExpressions.Regex.Match(
            s, @"^\s*SELECT\s+TOP\s+(\d+)\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            s = "SELECT " + s.Substring(m.Index + m.Length);
            s = s.TrimEnd().TrimEnd(';') + " LIMIT " + m.Groups[1].Value;
        }
        // Tarih/saat fonksiyonlari
        s = System.Text.RegularExpressions.Regex.Replace(s, @"CAST\s*\(\s*GETDATE\(\)\s+AS\s+DATE\s*\)",
            "date('now','localtime')", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s,
            @"DATEADD\s*\(\s*(\w+)\s*,\s*(-?\d+)\s*,\s*GETDATE\(\)\s*\)",
            mm => "datetime('now','localtime','" + mm.Groups[2].Value + " " + mm.Groups[1].Value.ToLowerInvariant() + "s')",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"GETDATE\(\)",
            "datetime('now','localtime')", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // ISNULL -> IFNULL
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bISNULL\s*\(",
            "IFNULL(", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return s;
    }

    // "IF EXISTS(<kosul>) <A> ELSE <B>"  /  "IF NOT EXISTS(<kosul>) <A>"
    // SQLite bu yapiyi desteklemez -> kosul ayri sorgulanip A veya B calistirilir.
    static bool SqliteKosulluCalistir(string sql, object[] kv)
    {
        var bas = System.Text.RegularExpressions.Regex.Match(sql,
            @"^\s*IF\s+(NOT\s+)?EXISTS\s*\(",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!bas.Success) return false;
        bool degil = bas.Groups[1].Success;

        // Kosulun kapanis parantezini DENGELI tarayarak bul (ic ice parantez olabilir)
        int ac = bas.Index + bas.Length;   // ilk '(' den hemen sonrasi
        int derinlik = 1, i2 = ac;
        while (i2 < sql.Length && derinlik > 0)
        {
            if (sql[i2] == '(') derinlik++;
            else if (sql[i2] == ')') derinlik--;
            if (derinlik == 0) break;
            i2++;
        }
        if (derinlik != 0) return false;
        string kosul = sql.Substring(ac, i2 - ac);
        string govde = sql.Substring(i2 + 1);

        // ELSE ayirici: DIKKAT - UPDATE govdesinde "CASE WHEN .. ELSE .. END"
        // bulunabilir. Bu yuzden yalnizca INSERT INTO'dan ONCE gelen ELSE
        // ayirici sayilir (aksi halde sorgu yanlis bolunur ve kayit yazilmaz).
        string a = govde, b = null;
        var em = System.Text.RegularExpressions.Regex.Match(govde,
            @"\sELSE\s+(?=INSERT\s+INTO)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (em.Success)
        {
            a = govde.Substring(0, em.Index);
            b = govde.Substring(em.Index + em.Length);
        }

        using (var c = SqliteAc())
        {
            bool varMi;
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = CevirSqlite(kosul);
                SqliteParams(cmd, kv);
                using (var r = cmd.ExecuteReader()) varMi = r.Read();
            }
            string calistir = (varMi != degil) ? a : b;   // NOT EXISTS ise tersine
            if (string.IsNullOrEmpty(calistir)) return true;   // yapacak is yok
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = CevirSqlite(calistir);
                SqliteParams(cmd, kv);
                cmd.ExecuteNonQuery();
            }
        }
        return true;
    }

    static void SqliteParams(System.Data.SQLite.SQLiteCommand cmd, object[] kv)
    {
        for (int i = 0; i + 1 < kv.Length; i += 2)
        {
            object v = kv[i + 1] ?? (object)DBNull.Value;
            if (v is DateTime) v = ((DateTime)v).ToString("yyyy-MM-dd HH:mm:ss");
            if (v is bool) v = ((bool)v) ? 1 : 0;
            cmd.Parameters.AddWithValue((string)kv[i], v);
        }
    }

    public static bool Ok()
    {
        return Motor != DbMotor.Yok;
    }

    // kv: "@ad", deger, "@ad2", deger2 ...
    public static bool Exec(string sql, params object[] kv)
    {
        if (Motor == DbMotor.Yok) { Err = "Veritabani yok"; return false; }
        try
        {
            if (_motor == DbMotor.Sqlite)
            {
                if (SqliteKosulluCalistir(sql, kv)) { Err = null; return true; }
                using (var c = SqliteAc())
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = CevirSqlite(sql);
                    SqliteParams(cmd, kv);
                    cmd.ExecuteNonQuery();
                }
                Err = null; return true;
            }
            using (var c = new SqlConnection(ConnStr()))
            {
                c.Open();
                using (var cmd = new SqlCommand(sql, c)) { AddParams(cmd, kv); cmd.ExecuteNonQuery(); }
            }
            Err = null; return true;
        }
        catch (Exception ex) { Err = ex.Message; return false; }
    }

    public static DataTable Query(string sql, params object[] kv)
    {
        if (Motor == DbMotor.Yok) { Err = "Veritabani yok"; return null; }
        try
        {
            var dt = new DataTable();
            if (_motor == DbMotor.Sqlite)
            {
                using (var c = SqliteAc())
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = CevirSqlite(sql);
                    SqliteParams(cmd, kv);
                    using (var da = new System.Data.SQLite.SQLiteDataAdapter(cmd)) da.Fill(dt);
                }
                Err = null; return dt;
            }
            using (var c = new SqlConnection(ConnStr()))
            {
                c.Open();
                using (var cmd = new SqlCommand(sql, c))
                {
                    AddParams(cmd, kv);
                    using (var da = new SqlDataAdapter(cmd)) da.Fill(dt);
                    Err = null; return dt;
                }
            }
        }
        catch (Exception ex) { Err = ex.Message; return null; }
    }

    public static object Scalar(string sql, params object[] kv)
    {
        if (Motor == DbMotor.Yok) { Err = "Veritabani yok"; return null; }
        try
        {
            if (_motor == DbMotor.Sqlite)
            {
                using (var c = SqliteAc())
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = CevirSqlite(sql);
                    SqliteParams(cmd, kv);
                    Err = null; return cmd.ExecuteScalar();
                }
            }
            using (var c = new SqlConnection(ConnStr()))
            {
                c.Open();
                using (var cmd = new SqlCommand(sql, c)) { AddParams(cmd, kv); Err = null; return cmd.ExecuteScalar(); }
            }
        }
        catch (Exception ex) { Err = ex.Message; return null; }
    }

    static void AddParams(SqlCommand cmd, object[] kv)
    {
        for (int i = 0; i + 1 < kv.Length; i += 2)
            cmd.Parameters.AddWithValue((string)kv[i], kv[i + 1] ?? (object)DBNull.Value);
    }

    // Tablolar yoksa olustur (veritabaninin kendisi kurulumda olusturulur).
    // SQLite motorunda sema zaten SqliteHazirla() ile kurulur.
    public static bool EnsureSchema()
    {
        if (Motor == DbMotor.Sqlite) { try { SqliteHazirla(); return true; } catch { return false; } }
        if (Motor == DbMotor.Yok) return false;
        return Exec(@"
IF OBJECT_ID('dbo.Jobs','U') IS NULL CREATE TABLE dbo.Jobs(
  Id INT IDENTITY PRIMARY KEY, Tarih DATETIME NOT NULL, Kullanici NVARCHAR(100), Makine NVARCHAR(100),
  Belge NVARCHAR(400), Sayfa INT DEFAULT 0, Dosya NVARCHAR(200), Kagit NVARCHAR(50), KB INT DEFAULT 0,
  Durum NVARCHAR(100) DEFAULT 'OK');
IF OBJECT_ID('dbo.Printed','U') IS NULL CREATE TABLE dbo.Printed(
  Id INT IDENTITY PRIMARY KEY, Tarih DATETIME, Makine NVARCHAR(100), Dosya NVARCHAR(200),
  Yazici NVARCHAR(200), Durum NVARCHAR(200) DEFAULT 'OK');
IF OBJECT_ID('dbo.Printed','U') IS NOT NULL ALTER TABLE dbo.Printed ALTER COLUMN Durum NVARCHAR(200);
IF OBJECT_ID('dbo.Heartbeat','U') IS NULL CREATE TABLE dbo.Heartbeat(
  Makine NVARCHAR(100) PRIMARY KEY, SonGorulme DATETIME, Yazici NVARCHAR(200), IP NVARCHAR(50));
IF OBJECT_ID('dbo.ConnLog','U') IS NULL CREATE TABLE dbo.ConnLog(
  Id INT IDENTITY PRIMARY KEY, Tarih DATETIME, Olay NVARCHAR(500));
IF OBJECT_ID('dbo.Rules','U') IS NULL CREATE TABLE dbo.Rules(
  Tip NVARCHAR(10) NOT NULL, Ad NVARCHAR(100) NOT NULL, Engel BIT NOT NULL DEFAULT 0,
  Kota INT NOT NULL DEFAULT 0, CONSTRAINT PK_Rules PRIMARY KEY(Tip,Ad));
IF OBJECT_ID('dbo.PanelUsers','U') IS NULL CREATE TABLE dbo.PanelUsers(
  Kullanici NVARCHAR(100) PRIMARY KEY, SifreHash CHAR(64) NOT NULL, Rol NVARCHAR(20) DEFAULT 'admin');
IF OBJECT_ID('dbo.Alerts','U') IS NULL CREATE TABLE dbo.Alerts(
  Id INT IDENTITY PRIMARY KEY, Tarih DATETIME, Tur NVARCHAR(50), Mesaj NVARCHAR(500), Okundu BIT DEFAULT 0);
IF OBJECT_ID('dbo.ClientKeys','U') IS NULL CREATE TABLE dbo.ClientKeys(
  Makine NVARCHAR(100) PRIMARY KEY, AnahtarHash CHAR(64) NOT NULL, Olusturma DATETIME DEFAULT GETDATE());
IF COL_LENGTH('dbo.Heartbeat','KullaniciAdi') IS NULL ALTER TABLE dbo.Heartbeat ADD KullaniciAdi NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.Heartbeat','OS') IS NULL ALTER TABLE dbo.Heartbeat ADD OS NVARCHAR(150) NULL;
IF OBJECT_ID('dbo.Costs','U') IS NULL CREATE TABLE dbo.Costs(
  Kagit NVARCHAR(50) PRIMARY KEY, Fiyat DECIMAL(10,4) NOT NULL DEFAULT 0);
IF OBJECT_ID('dbo.PrinterHealth','U') IS NULL CREATE TABLE dbo.PrinterHealth(
  Makine NVARCHAR(100) NOT NULL, Yazici NVARCHAR(150) NOT NULL, Durum NVARCHAR(50),
  Hata NVARCHAR(100), Kuyruk INT DEFAULT 0, Guncelleme DATETIME,
  CONSTRAINT PK_PrinterHealth PRIMARY KEY(Makine, Yazici));
IF OBJECT_ID('dbo.JobQueue','U') IS NULL CREATE TABLE dbo.JobQueue(
  Id INT IDENTITY PRIMARY KEY, Makine NVARCHAR(100), Dosya NVARCHAR(200), Yol NVARCHAR(300),
  BoyutKB INT DEFAULT 0, SikistirilmisKB INT DEFAULT 0, Olusturma DATETIME,
  Durum NVARCHAR(20) DEFAULT 'BEKLIYOR', Alinma DATETIME NULL);");
    }

    // ============================================================
    //  YAZICI SAGLIGI - DOSYA DEPOSU (MSSQL ZORUNLU DEGIL)
    //  C:\Print360\stats\printers.csv
    //    "makine","yazici","durum","hata","kuyruk","guncelleme"
    //  SQL varsa oraya da yazilir; yoksa panel bu dosyadan okur.
    // ============================================================
    public static readonly string YaziciCsv = @"C:\Print360\stats\printers.csv";
    static readonly object _yKilit = new object();

    public static void YaziciSagligiYaz(string makine, string yazici, string durum, string hata, int kuyruk)
    {
        try
        {
            lock (_yKilit)
            {
                var satirlar = new System.Collections.Generic.List<string[]>();
                foreach (var s in YaziciSagligiOku())
                    if (!(string.Equals(s[0], makine, StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(s[1], yazici, StringComparison.OrdinalIgnoreCase)))
                        satirlar.Add(s);   // ayni makine+yazici kaydini degistir
                satirlar.Add(new[] { makine, yazici, durum, hata, kuyruk.ToString(),
                                     DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
                var sb = new System.Text.StringBuilder();
                foreach (var s in satirlar)
                {
                    for (int i = 0; i < s.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append('"').Append((s[i] ?? "").Replace("\"", "\"\"")).Append('"');
                    }
                    sb.Append("\r\n");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(YaziciCsv));
                File.WriteAllText(YaziciCsv, sb.ToString(), System.Text.Encoding.UTF8);
            }
        }
        catch { }
    }

    // Donus: [makine, yazici, durum, hata, kuyruk, guncelleme]
    public static System.Collections.Generic.List<string[]> YaziciSagligiOku()
    {
        var liste = new System.Collections.Generic.List<string[]>();
        try
        {
            if (!File.Exists(YaziciCsv)) return liste;
            foreach (var ln in File.ReadAllLines(YaziciCsv))
            {
                if (ln.Trim().Length == 0) continue;
                var f = CsvAyir(ln);
                if (f.Length >= 6) liste.Add(f);
            }
        }
        catch { }
        return liste;
    }

    static string[] CsvAyir(string line)
    {
        var f = new System.Collections.Generic.List<string>();
        var cur = new System.Text.StringBuilder();
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else if (c == '"') q = false;
                else cur.Append(c);
            }
            else
            {
                if (c == '"') q = true;
                else if (c == ',') { f.Add(cur.ToString()); cur.Length = 0; }
                else cur.Append(c);
            }
        }
        f.Add(cur.ToString());
        return f.ToArray();
    }

    // ---- Panelden MSSQL aktiflestirme ----
    // Kurulumda SQL secilmediyse (CSV modu) yonetici panelden buradan acabilir:
    // veritabanini olusturur, db.ini'yi gunceller, semayi kurar ve onbellegi tazeler.
    public static bool SqlKur(string server, string kullanici, string sifre, out string mesaj)
    {
        mesaj = "";
        if (string.IsNullOrWhiteSpace(server)) { mesaj = "SQL sunucusu bos olamaz."; return false; }
        try
        {
            // 1) master'a baglan ve veritabanini olustur (yoksa)
            string master = "Server=" + server + ";Database=master;User ID=" + kullanici
                          + ";Password=" + sifre + ";Connect Timeout=8";
            using (var c = new SqlConnection(master))
            {
                c.Open();
                using (var cmd = new SqlCommand("IF DB_ID('Print360') IS NULL CREATE DATABASE Print360", c))
                    cmd.ExecuteNonQuery();
            }
            // 2) db.ini'yi guncelle (diger ayarlar korunur)
            IniGuncelle(server, kullanici, sifre);
            // 3) onbellegi tazele ve semayi kur (motor yeniden secilsin: SQLite -> MSSQL)
            MotorSifirla();
            ConnStr();
            if (!EnsureSchema()) { mesaj = "Tablolar olusturulamadi: " + Err; return false; }
            mesaj = "Veritabani hazir: Print360 @ " + server + " (tablolar kuruldu)";
            Err = null;
            return true;
        }
        catch (Exception ex) { Err = ex.Message; mesaj = ex.Message; return false; }
    }

    // db.ini icindeki SQL satirlarini gunceller, digerlerine dokunmaz.
    static void IniGuncelle(string server, string kullanici, string sifre)
    {
        string f = @"C:\Print360\db.ini";
        var cikti = new System.Collections.Generic.List<string>();
        bool vS = false, vD = false, vU = false, vP = false;
        if (File.Exists(f))
            foreach (var l in File.ReadAllLines(f))
            {
                var p = l.Split(new[] { '=' }, 2);
                if (p.Length == 2)
                {
                    var k = p[0].Trim().ToLowerInvariant();
                    if (k == "server") { cikti.Add("Server=" + server); vS = true; continue; }
                    if (k == "database") { cikti.Add("Database=Print360"); vD = true; continue; }
                    if (k == "user") { cikti.Add("User=" + kullanici); vU = true; continue; }
                    if (k == "password") { cikti.Add("Password=" + sifre); vP = true; continue; }
                }
                cikti.Add(l);
            }
        if (!vS) cikti.Insert(0, "Server=" + server);
        if (!vD) cikti.Add("Database=Print360");
        if (!vU) cikti.Add("User=" + kullanici);
        if (!vP) cikti.Add("Password=" + sifre);
        File.WriteAllLines(f, cikti.ToArray());
    }

    // ============================================================
    //  UYARILAR - DOSYA DEPOSU (MSSQL ZORUNLU DEGIL)
    //  C:\Print360\stats\alerts.csv
    //    "tarih","tur","mesaj","okundu"
    //  SQL varsa oraya DA yazilir; yoksa panel bu dosyadan okur.
    //  En yeni 500 kayit tutulur.
    // ============================================================
    public static readonly string UyariCsv = @"C:\Print360\stats\alerts.csv";
    const int UYARI_LIMIT = 500;
    static readonly object _uKilit = new object();

    public static void Alert(string tur, string mesaj)
    {
        // 1) SQL (varsa)
        Exec("INSERT INTO Alerts(Tarih,Tur,Mesaj,Okundu) VALUES(GETDATE(),@t,@m,0)", "@t", tur, "@m", mesaj);
        // 2) Dosya (her zaman) - SQL yoksa panel buradan okur
        try
        {
            lock (_uKilit)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(UyariCsv));
                File.AppendAllText(UyariCsv,
                    CsvSatir(new[] { DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), tur, mesaj, "0" }),
                    System.Text.Encoding.UTF8);
                UyarilariKirp();
            }
        }
        catch { }
    }

    // Donus (en yeni once): [tarih, tur, mesaj, okundu]
    public static System.Collections.Generic.List<string[]> UyarilariOku(int adet = 200)
    {
        var liste = new System.Collections.Generic.List<string[]>();
        try
        {
            if (!File.Exists(UyariCsv)) return liste;
            string[] tum;
            lock (_uKilit) tum = File.ReadAllLines(UyariCsv);
            for (int i = tum.Length - 1; i >= 0 && liste.Count < adet; i--)
            {
                if (tum[i].Trim().Length == 0) continue;
                var f = CsvAyir(tum[i]);
                if (f.Length >= 4) liste.Add(f);
            }
        }
        catch { }
        return liste;
    }

    public static int OkunmamisUyari()
    {
        int n = 0;
        foreach (var u in UyarilariOku(UYARI_LIMIT)) if (u[3] != "1") n++;
        return n;
    }

    public static void UyarilariOkunduIsaretle()
    {
        try
        {
            lock (_uKilit)
            {
                if (!File.Exists(UyariCsv)) return;
                var sb = new System.Text.StringBuilder();
                foreach (var ln in File.ReadAllLines(UyariCsv))
                {
                    if (ln.Trim().Length == 0) continue;
                    var f = CsvAyir(ln);
                    if (f.Length < 4) continue;
                    sb.Append(CsvSatir(new[] { f[0], f[1], f[2], "1" }));
                }
                File.WriteAllText(UyariCsv, sb.ToString(), System.Text.Encoding.UTF8);
            }
        }
        catch { }
    }

    // Dosya sismesin: en yeni UYARI_LIMIT kayit kalsin
    static void UyarilariKirp()
    {
        try
        {
            var tum = File.ReadAllLines(UyariCsv);
            if (tum.Length <= UYARI_LIMIT + 100) return;
            var sb = new System.Text.StringBuilder();
            for (int i = tum.Length - UYARI_LIMIT; i < tum.Length; i++)
                if (tum[i].Trim().Length > 0) sb.AppendLine(tum[i]);
            File.WriteAllText(UyariCsv, sb.ToString(), System.Text.Encoding.UTF8);
        }
        catch { }
    }

    static string CsvSatir(string[] alanlar)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < alanlar.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append((alanlar[i] ?? "").Replace("\"", "\"\"")).Append('"');
        }
        sb.Append("\r\n");
        return sb.ToString();
    }
}
