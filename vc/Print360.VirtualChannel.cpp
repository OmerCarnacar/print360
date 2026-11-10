// ============================================================
//  Print360 - RDP Yazdirma ve Yonetim Cozumu
//  Gelistirici : Omer CARNACAR  <omer.carnacar@outlook.com.tr>
//  LinkedIn    : https://www.linkedin.com/in/omercarnacar/
//  Lisans      : GPL-3.0-or-later, acik kaynak (bkz. LICENSE, NOTICE)
//  Telif       : (c) 2026 Omer CARNACAR
// ============================================================
//  Print360 - RDP Static Virtual Channel istemci eklentisi (kanal mantigi)
//
//  mstsc.exe (RDP istemcisi) bu DLL'i registry AddIns kaydiyla yukler.
//  Sunucudaki ServerAgent isi RDP sanal kanalindan ("P360") gonderir;
//  bu eklenti veriyi alip  C:\Print360\jobs\<ad>  olarak yazar.
//  Baskiyi mevcut Print360.ClientAgent.exe yapar.
//  IP / port / HTTPS / firewall GEREKMEZ - her sey RDP tunelinden akar.
//
//  PROTOKOL v2 - UYGULAMA SEVIYESINDE PARCALAMA
//  ---------------------------------------------
//  SVC'de tek mesaj boyutu sinirlidir; bu yuzden PDF, sunucu tarafinda
//  <= CERCEVE_VERI baytlik bloklara bolunup her blok AYRI bir kanal
//  mesaji olarak gonderilir. Boylece dosya boyutu sinirsizdir.
//
//  Cerceve (little endian, 20 bayt baslik):
//    [0]  sihir[4]   = "P360"
//    [4]  surum(1)   = 2
//    [5]  tip(1)     = 1 BASLA | 2 VERI | 3 BITTI
//    [6]  rezerv(2)  = 0
//    [8]  isId(4)    = is numarasi (oturum icinde artan)
//    [12] blokNo(4)  = VERI icin blok sirasi (0'dan baslar)
//    [16] uzunluk(4) = takip eden veri uzunlugu
//    [20] veri[uzunluk]
//
//    BASLA verisi : [toplamBoyut:4][adUzunlugu:4][ad UTF-8]
//    VERI  verisi : ham PDF blogu
//    BITTI verisi : [blokSayisi:4]   (dogrulama icin)
//
//  Istemci: BASLA'da <ad>.part acar, VERI'lerde ekler, BITTI'de boyut ve
//  blok sayisini dogrulayip <ad> olarak atomik yeniden adlandirir.
//  Yarim kalan is asla basilmaz (ClientAgent yalnizca .part olmayani gorur).
//
//  DERLEME:  bkz. build-vc.cmd
//  TEST   :  build-vc.cmd test   ->  protokolu kanal olmadan dogrular
// ============================================================
#include <windows.h>
#include <string>
#include <vector>

#ifndef P360_TEST
#include <cchannel.h>
#endif

#define KANAL_ADI "P360"          // SVC kanal adi en fazla 7 karakter

// Cerceve sabitleri
#define BASLIK_BOYU 20
#define TIP_BASLA 1
#define TIP_VERI  2
#define TIP_BITTI 3
#define EN_UZUN_AD 512

// Kok klasor. PRINT360_VC_KOK ortam degiskeni testlerin gercek veriye dokunmamasi icindir.
static std::string g_kok = "C:\\Print360";
// Isin yazilacagi klasor (test harness bunu degistirebilir)
static std::string g_jobsDir = "C:\\Print360\\jobs";

// ---- Suren isin durumu (baglanti basina bir tane) ----
struct IsDurumu
{
    HANDLE      dosya;
    std::wstring partYol;     // <hedef>.part
    std::wstring sonYol;      // <hedef>
    DWORD       id, toplam, yazilan, blok;
    IsDurumu() : dosya(INVALID_HANDLE_VALUE), id(0), toplam(0), yazilan(0), blok(0) {}
};

static DWORD OkuDword(const BYTE* p) { DWORD v; memcpy(&v, p, 4); return v; }

// Sunucu is adini UTF-8 gonderir. Eskiden ANSI sanilip CreateFileA'ya veriliyordu:
// Turkce karakterli belge adlari diskte bozuk olusuyordu.
static std::wstring Genis(const std::string& s, UINT kodSayfasi)
{
    if (s.empty()) return std::wstring();
    int n = MultiByteToWideChar(kodSayfasi, 0, s.c_str(), (int)s.size(), NULL, 0);
    if (n <= 0) return std::wstring();
    std::wstring w(n, L'\0');
    MultiByteToWideChar(kodSayfasi, 0, s.c_str(), (int)s.size(), &w[0], n);
    return w;
}

static void IsiKapat(IsDurumu& s, bool basarili)
{
    if (s.dosya != INVALID_HANDLE_VALUE) { CloseHandle(s.dosya); s.dosya = INVALID_HANDLE_VALUE; }
    if (basarili && !s.partYol.empty())
        MoveFileExW(s.partYol.c_str(), s.sonYol.c_str(), MOVEFILE_REPLACE_EXISTING);
    else if (!s.partYol.empty())
        DeleteFileW(s.partYol.c_str());   // yarim is basilmasin
    s.partYol.clear(); s.sonYol.clear();
    s.id = 0; s.toplam = 0; s.yazilan = 0; s.blok = 0;
}

static void DosyaAdiTemizle(std::string& ad)
{
    for (size_t i = 0; i < ad.size(); i++)
    {
        char c = ad[i];
        if (c == '\\' || c == '/' || c == ':' || c == '*' || c == '?' ||
            c == '"'  || c == '<' || c == '>' || c == '|' || (unsigned char)c < 32)
            ad[i] = '_';
    }
    if (ad.empty() || ad == "." || ad == "..") ad = "is.pdf";
}

// ---- TEK CERCEVEYI ISLE (test harness dogrudan bunu cagirir) ----
// Donus: true = cerceve gecerli ve islendi
bool P360_CerceveIsle(IsDurumu& s, const BYTE* f, size_t len)
{
    if (len < BASLIK_BOYU) return false;
    if (!(f[0] == 'P' && f[1] == '3' && f[2] == '6' && f[3] == '0')) return false;
    if (f[4] != 2) return false;                       // surum
    BYTE tip     = f[5];
    DWORD isId   = OkuDword(f + 8);
    DWORD blokNo = OkuDword(f + 12);
    DWORD uzn    = OkuDword(f + 16);
    if (uzn > len - BASLIK_BOYU) return false;         // eksik cerceve (tasma olmadan)
    const BYTE* veri = f + BASLIK_BOYU;

    if (tip == TIP_BASLA)
    {
        if (s.dosya != INVALID_HANDLE_VALUE) IsiKapat(s, false);  // onceki yarim is
        if (uzn < 8) return false;
        DWORD toplam = OkuDword(veri);
        DWORD adLen  = OkuDword(veri + 4);
        // "8 + adLen > uzn" 32 bitte TASARDI: dev bir adLen denetimi gecip bellek disini okuturdu
        if (adLen > uzn - 8 || adLen > EN_UZUN_AD) return false;
        std::string ad((const char*)(veri + 8), adLen);
        DosyaAdiTemizle(ad);

        CreateDirectoryA(g_kok.c_str(), NULL);
        CreateDirectoryA(g_jobsDir.c_str(), NULL);
        std::wstring genisAd = Genis(ad, CP_UTF8);
        if (genisAd.empty()) genisAd = L"is.pdf";
        s.sonYol  = Genis(g_jobsDir, CP_ACP) + L"\\" + genisAd;
        s.partYol = s.sonYol + L".part";
        s.dosya = CreateFileW(s.partYol.c_str(), GENERIC_WRITE, 0, NULL,
                              CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        if (s.dosya == INVALID_HANDLE_VALUE) { s.partYol.clear(); s.sonYol.clear(); return false; }
        s.id = isId; s.toplam = toplam; s.yazilan = 0; s.blok = 0;
        return true;
    }

    if (tip == TIP_VERI)
    {
        if (s.dosya == INVALID_HANDLE_VALUE || isId != s.id) return false;
        if (blokNo != s.blok) { IsiKapat(s, false); return false; }   // sira bozuldu
        if (uzn > s.toplam - s.yazilan) { IsiKapat(s, false); return false; }   // bildirilenden fazla veri
        DWORD yazilan = 0;
        if (uzn > 0 && !WriteFile(s.dosya, veri, uzn, &yazilan, NULL)) { IsiKapat(s, false); return false; }
        s.yazilan += yazilan;
        s.blok++;
        return true;
    }

    if (tip == TIP_BITTI)
    {
        if (s.dosya == INVALID_HANDLE_VALUE || isId != s.id) return false;
        DWORD beklenenBlok = (uzn >= 4) ? OkuDword(veri) : s.blok;
        bool tamam = (s.yazilan == s.toplam) && (s.blok == beklenenBlok);
        IsiKapat(s, tamam);      // eksikse .part silinir, yarim is BASILMAZ
        return tamam;
    }
    return false;
}

// Test harness'in hedef klasoru degistirmesi icin
void P360_JobsDirAyarla(const char* d) { g_jobsDir = d; }

#ifndef P360_TEST
// ============================================================
//                  GERCEK RDP EKLENTISI
//
//  Bu kod mstsc.exe'nin (ya da RDP denetimini barindiran programin) ICINDE
//  calisir: buradaki bir bellek hatasi RDP istemcisini COKERTIR. Kurallar:
//   1. CHANNEL_ENTRY_POINTS, VirtualChannelEntry donunce GECERSIZDIR: kopyalanir.
//      (Eskiden gostericisi saklaniyordu.)
//   2. VirtualChannelWrite ESZAMANSIZDIR: tampon, WRITE_COMPLETE / WRITE_CANCELLED
//      olayina kadar yasamalidir. Eskiden yerel bir vector veriliyor ve hemen yok
//      ediliyordu: mstsc serbest birakilmis bellegi okuyordu (COKME SEBEBI). Kanal
//      modunda kalp atisi dakikada bir bu yoldan gider, yani her oturumda tekrarlanir.
//   3. Ayni surecte birden fazla RDP baglantisi olabilir (sekmeli RDP yoneticileri):
//      durum baglanti basina tutulur, tek kilitle korunur.
//   4. Geri cagrilardan disari C++ istisnasi KACMAZ.
//   5. DLL surecte sabitlenir: arka plan is parcacigi calisirken bosaltilamaz.
// ============================================================
#define EN_BUYUK_MESAJ   (4u * 1024 * 1024)   // sunucu cerceveleri ~30 KB'dir
#define EN_BUYUK_OUTBOX  (1u * 1024 * 1024)
#define EN_COK_BEKLEYEN  256

struct Baglanti
{
    CHANNEL_ENTRY_POINTS ep;     // KOPYA
    LPVOID init;
    DWORD  open;
    bool   acik;
    DWORD  sira;                 // baglanma sirasi (en yeni en buyuk)
    bool   atla;                 // asiri buyuk mesaj: sonuna kadar yok say
    std::vector<BYTE> buf;       // host cerceveyi parcalara bolebilir
    IsDurumu is;
    Baglanti() : init(NULL), open(0), acik(false), sira(0), atla(false) { ZeroMemory(&ep, sizeof(ep)); }
};

static CRITICAL_SECTION       g_kilit;
static std::vector<Baglanti*> g_bag;        // surec boyunca yasar
static std::vector<void*>     g_bekleyen;   // tamamlanmasi beklenen yazma tamponlari
static HANDLE g_thread = NULL;
static DWORD  g_sira = 0;
static bool   g_hazir = false;

struct Kilit { Kilit() { EnterCriticalSection(&g_kilit); } ~Kilit() { LeaveCriticalSection(&g_kilit); } };

static std::string Outbox() { return g_kok + "\\vc-outbox"; }
// Kanal acikken olusturulan isaret dosyasi. ClientAgent buna bakarak
// "RDP kanali gercekten calisiyor mu?" sorusunu tahminsiz yanitlar.
static std::string Isaret() { return Outbox() + "\\.vc-aktif"; }

// Tani gunlugu: <kok>\logs\vc-eklenti.log (512 KB'i gecince bastan baslar)
static void Gunluk(const char* fmt, ...)
{
    try
    {
        char m[1100]; va_list a; va_start(a, fmt); wvsprintfA(m, fmt, a); va_end(a);
        SYSTEMTIME t; GetLocalTime(&t);
        char s[1300];
        wsprintfA(s, "%04d-%02d-%02d %02d:%02d:%02d  [surec %u] %s\r\n", t.wYear, t.wMonth, t.wDay,
                  t.wHour, t.wMinute, t.wSecond, GetCurrentProcessId(), m);
        std::string dir = g_kok + "\\logs";
        CreateDirectoryA(g_kok.c_str(), NULL); CreateDirectoryA(dir.c_str(), NULL);
        std::string yol = dir + "\\vc-eklenti.log";
        HANDLE h = CreateFileA(yol.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
                               OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        if (h == INVALID_HANDLE_VALUE) return;
        if (GetFileSize(h, NULL) > 512 * 1024) { SetFilePointer(h, 0, NULL, FILE_BEGIN); SetEndOfFile(h); }
        DWORD w; WriteFile(h, s, lstrlenA(s), &w, NULL);
        CloseHandle(h);
    }
    catch (...) { }
}

// --- g_kilit altinda cagrilanlar ---
static Baglanti* InitIleBul(LPVOID init)
{
    for (size_t i = 0; i < g_bag.size(); i++) if (g_bag[i]->init == init) return g_bag[i];
    // Init cagrisi donmeden olay geldiyse tutamac henuz yazilmamistir
    for (size_t i = 0; i < g_bag.size(); i++) if (g_bag[i]->init == NULL) { g_bag[i]->init = init; return g_bag[i]; }
    return NULL;
}
static Baglanti* AcikBul(DWORD open)
{
    for (size_t i = 0; i < g_bag.size(); i++) if (g_bag[i]->acik && g_bag[i]->open == open) return g_bag[i];
    return NULL;
}
static Baglanti* EnYeniAcik()
{
    Baglanti* b = NULL;
    for (size_t i = 0; i < g_bag.size(); i++)
        if (g_bag[i]->acik && (b == NULL || g_bag[i]->sira > b->sira)) b = g_bag[i];
    return b;
}
static bool BekleyenCikar(void* p)
{
    for (size_t i = 0; i < g_bekleyen.size(); i++)
        if (g_bekleyen[i] == p) { g_bekleyen.erase(g_bekleyen.begin() + i); return true; }
    return false;
}

// Kanal veri olayi: parcalar CHANNEL_FLAG_FIRST/LAST ile gelir, once TAM
// cerceve birlestirilir, sonra islenir.
static VOID VCAPITYPE OpenEvent(DWORD openHandle, UINT event, LPVOID pData,
                                UINT32 dataLength, UINT32 totalLength, UINT32 dataFlags)
{
    try
    {
        if (event == CHANNEL_EVENT_WRITE_COMPLETE || event == CHANNEL_EVENT_WRITE_CANCELLED)
        {
            // pData = Write'a verdigimiz tampon. Artik serbest birakilabilir.
            bool bizim;
            { Kilit k; bizim = BekleyenCikar(pData); }
            if (bizim) HeapFree(GetProcessHeap(), 0, pData);
            return;
        }
        if (event != CHANNEL_EVENT_DATA_RECEIVED || pData == NULL) return;
        Kilit k;
        Baglanti* b = AcikBul(openHandle);
        if (b == NULL) return;
        if (dataFlags & CHANNEL_FLAG_FIRST)
        {
            b->buf.clear();
            b->atla = totalLength > EN_BUYUK_MESAJ;
            if (b->atla) Gunluk("asiri buyuk kanal mesaji yok sayildi (%u bayt)", totalLength);
            else b->buf.reserve(totalLength);
        }
        if (!b->atla)
        {
            if (b->buf.size() + (size_t)dataLength > EN_BUYUK_MESAJ) { b->atla = true; b->buf.clear(); }
            else b->buf.insert(b->buf.end(), (BYTE*)pData, (BYTE*)pData + dataLength);
        }
        if (dataFlags & CHANNEL_FLAG_LAST)
        {
            if (!b->atla && !b->buf.empty()) P360_CerceveIsle(b->is, b->buf.data(), b->buf.size());
            b->buf.clear(); b->atla = false;
        }
    }
    catch (...) { Gunluk("OpenEvent: beklenmeyen istisna (olay %u)", event); }
}

// Ters yon: ClientAgent'in vc-outbox'a biraktigi onay/sayac dosyalarini
// kanaldan sunucuya yaz (istemci -> sunucu). En son acilan baglantiya gider.
static void OutboxTur()
{
    { Kilit k; if (EnYeniAcik() == NULL || g_bekleyen.size() >= EN_COK_BEKLEYEN) return; }
    std::string dir = Outbox();
    WIN32_FIND_DATAA fd;
    HANDLE hf = FindFirstFileA((dir + "\\*.msg").c_str(), &fd);
    if (hf == INVALID_HANDLE_VALUE) return;
    do
    {
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
        std::string yol = dir + "\\" + fd.cFileName;
        HANDLE h = CreateFileA(yol.c_str(), GENERIC_READ, 0, NULL, OPEN_EXISTING, 0, NULL);
        if (h == INVALID_HANDLE_VALUE) continue;
        DWORD sz = GetFileSize(h, NULL);
        if (sz == 0 || sz > EN_BUYUK_OUTBOX) { CloseHandle(h); DeleteFileA(yol.c_str()); continue; }   // gonderilemez: sonsuza dek beklemesin
        // Tampon YIGINDAN ayrilir ve yazma tamamlanana kadar yasar (bkz. kural 2)
        BYTE* p = (BYTE*)HeapAlloc(GetProcessHeap(), 0, sz);
        if (p == NULL) { CloseHandle(h); break; }
        DWORD okundu = 0;
        BOOL okuOk = ReadFile(h, p, sz, &okundu, NULL);
        CloseHandle(h);
        if (!okuOk || okundu != sz) { HeapFree(GetProcessHeap(), 0, p); continue; }

        UINT rc = CHANNEL_RC_NOT_CONNECTED;
        {
            Kilit k;
            Baglanti* b = EnYeniAcik();
            if (b != NULL && b->ep.pVirtualChannelWrite != NULL && g_bekleyen.size() < EN_COK_BEKLEYEN)
            {
                g_bekleyen.push_back(p);            // host tamamlanmayi HEMEN bildirebilir
                rc = b->ep.pVirtualChannelWrite(b->open, p, okundu, p);
                if (rc != CHANNEL_RC_OK) BekleyenCikar(p);
            }
        }
        if (rc == CHANNEL_RC_OK) DeleteFileA(yol.c_str());   // tampon artik OpenEvent'te serbest kalir
        else { HeapFree(GetProcessHeap(), 0, p); break; }    // kanal hazir degil: sonraki turda yeniden
    } while (FindNextFileA(hf, &fd));
    FindClose(hf);
}

static DWORD WINAPI OutboxThread(LPVOID)
{
    for (;;)
    {
        Sleep(1000);
        try { OutboxTur(); } catch (...) { }
    }
}

static VOID VCAPITYPE InitEvent(LPVOID pInitHandle, UINT event, LPVOID pData, UINT dataLength)
{
    (void)pData; (void)dataLength;
    try
    {
        if (event == CHANNEL_EVENT_CONNECTED)
        {
            PVIRTUALCHANNELOPEN ac = NULL;
            { Kilit k; Baglanti* b = InitIleBul(pInitHandle); if (b == NULL) return; ac = b->ep.pVirtualChannelOpen; }
            if (ac == NULL) return;
            DWORD oh = 0;
            UINT rc = ac(pInitHandle, &oh, (PCHAR)KANAL_ADI, OpenEvent);
            if (rc != CHANNEL_RC_OK) { Gunluk("kanal acilamadi (kod %u)", rc); return; }

            Kilit k;
            Baglanti* b = InitIleBul(pInitHandle);
            if (b == NULL) return;
            // Host ayni tutamaci yeniden verdiyse eski kayit artik gecersizdir
            for (size_t i = 0; i < g_bag.size(); i++)
                if (g_bag[i] != b && g_bag[i]->acik && g_bag[i]->open == oh)
                { g_bag[i]->acik = false; IsiKapat(g_bag[i]->is, false); g_bag[i]->buf.clear(); }
            IsiKapat(b->is, false);
            b->open = oh; b->acik = true; b->sira = ++g_sira; b->buf.clear(); b->atla = false;

            // Kanal ACIK: isaret dosyasini birak (ClientAgent kanal moduna gecer)
            CreateDirectoryA(g_kok.c_str(), NULL);
            CreateDirectoryA(Outbox().c_str(), NULL);
            HANDLE m = CreateFileA(Isaret().c_str(), GENERIC_WRITE, FILE_SHARE_READ, NULL,
                                   CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
            if (m != INVALID_HANDLE_VALUE) { DWORD w; WriteFile(m, "1", 1, &w, NULL); CloseHandle(m); }
            if (g_thread == NULL) g_thread = CreateThread(NULL, 0, OutboxThread, NULL, 0, NULL);
            Gunluk("kanal acildi (tutamac %u)", oh);
        }
        else if (event == CHANNEL_EVENT_DISCONNECTED || event == CHANNEL_EVENT_TERMINATED)
        {
            PVIRTUALCHANNELCLOSE kapat = NULL; DWORD oh = 0;
            {
                Kilit k;
                Baglanti* b = InitIleBul(pInitHandle);
                if (b == NULL) return;
                if (b->acik) { b->acik = false; oh = b->open; kapat = b->ep.pVirtualChannelClose; }
                IsiKapat(b->is, false);                       // yarim is temizlensin
                std::vector<BYTE>().swap(b->buf);
                if (EnYeniAcik() == NULL) DeleteFileA(Isaret().c_str());   // kanal kalmadi -> HTTPS'e dusulsun
            }
            if (kapat != NULL && event == CHANNEL_EVENT_DISCONNECTED) { kapat(oh); Gunluk("kanal kapandi (tutamac %u)", oh); }
        }
    }
    catch (...) { Gunluk("InitEvent: beklenmeyen istisna (olay %u)", event); }
}

static void BirKezHazirla()
{
    if (g_hazir) return;
    g_hazir = true;
    // DLL'i surecte sabitle: arka plan is parcacigi ve gec gelen geri cagrilar
    // bosaltilmis koda dusmesin.
    HMODULE m;
    GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN, (LPCSTR)&BirKezHazirla, &m);
}

// VARSAYILAN: ETKISIZ. Bu DLL uzak masaustu istemcisinin icinde calisir; oradaki her hata
// RDP'yi etkiler. Kanal yalnizca <kok>\\vc-etkin.txt dosyasi varsa (bilincli secim) acilir.
// Dosya yoksa giris noktasi hicbir sey kaydetmeden doner: is parcacigi, dosya ve kanal YOK.
static bool Etkin()
{
    char kok[200];
    DWORD n = GetEnvironmentVariableA("PRINT360_VC_KOK", kok, sizeof(kok));
    std::string k = (n > 0 && n < sizeof(kok)) ? std::string(kok) : std::string("C:\\Print360");
    if (GetFileAttributesA((k + "\\vc-etkin.txt").c_str()) == INVALID_FILE_ATTRIBUTES) return false;
    Kilit kl;
    g_kok = k; g_jobsDir = k + "\\jobs";
    return true;
}

// mstsc.exe DLL'i yukleyince cagirdigi giris noktasi (SVC).
extern "C" BOOL VCAPITYPE VirtualChannelEntry(PCHANNEL_ENTRY_POINTS pEntryPoints)
{
    try
    {
        if (pEntryPoints == NULL || pEntryPoints->pVirtualChannelInit == NULL) return FALSE;
        if (!Etkin()) return FALSE;            // varsayilan: eklenti hicbir sey yapmaz
        Baglanti* b = new Baglanti();
        b->ep = *pEntryPoints;                 // KOPYA: gosterici donusten sonra gecersiz
        { Kilit k; BirKezHazirla(); g_bag.push_back(b); }

        CHANNEL_DEF cd;
        ZeroMemory(&cd, sizeof(cd));
        lstrcpynA(cd.name, KANAL_ADI, sizeof(cd.name));
        cd.options = CHANNEL_OPTION_INITIALIZED | CHANNEL_OPTION_ENCRYPT_RDP;

        LPVOID init = NULL;
        UINT rc = b->ep.pVirtualChannelInit(&init, &cd, 1, VIRTUAL_CHANNEL_VERSION_WIN2000, InitEvent);
        { Kilit k; if (b->init == NULL) b->init = init; }
        if (rc != CHANNEL_RC_OK)
        {
            Gunluk("kanal kaydi basarisiz (kod %u)", rc);
            Kilit k;
            for (size_t i = 0; i < g_bag.size(); i++) if (g_bag[i] == b) { g_bag.erase(g_bag.begin() + i); break; }
            delete b;
            return FALSE;
        }
        return TRUE;
    }
    catch (...) { return FALSE; }
}

BOOL WINAPI DllMain(HINSTANCE, DWORD neden, LPVOID)
{
    if (neden == DLL_PROCESS_ATTACH) InitializeCriticalSection(&g_kilit);
    return TRUE;
}

#else
// ============================================================
//   TEST HARNESS  (build-vc.cmd test)
//   Kanal olmadan protokolu dogrular: sunucunun urettigi cerceve
//   dosyasini okur, IsiYaz mantigini calistirir, sonucu diske yazar.
//   Kullanim: Print360.VCTest.exe <cerceve-dosyasi> <hedef-klasor>
// ============================================================
#include <stdio.h>
int main(int argc, char** argv)
{
    if (argc < 3) { printf("Kullanim: %s <cerceve.bin> <hedef-klasor>\n", argv[0]); return 2; }
    P360_JobsDirAyarla(argv[2]);
    IsDurumu durum;

    HANDLE h = CreateFileA(argv[1], GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
    if (h == INVALID_HANDLE_VALUE) { printf("Cerceve dosyasi acilamadi\n"); return 3; }
    DWORD sz = GetFileSize(h, NULL);
    std::vector<BYTE> tum(sz ? sz : 1);
    DWORD okundu = 0;
    ReadFile(h, &tum[0], sz, &okundu, NULL);
    CloseHandle(h);

    // Dosya bicimi: her cerceve  [cerceveUzunlugu:4][cerceve]
    size_t ofs = 0; int adet = 0, hata = 0;
    while (ofs + 4 <= okundu)
    {
        DWORD cLen; memcpy(&cLen, &tum[ofs], 4); ofs += 4;
        if (ofs + cLen > okundu) { printf("HATA: eksik cerceve @%u\n", (unsigned)ofs); hata++; break; }
        if (!P360_CerceveIsle(durum, &tum[ofs], cLen)) {
            // BITTI cercevesi false donerse dogrulama basarisiz demektir
            printf("  cerceve #%d islenmedi (tip=%u)\n", adet, (unsigned)tum[ofs + 5]);
            hata++;
        }
        ofs += cLen; adet++;
    }
    printf("Islenen cerceve: %d, hata: %d\n", adet, hata);
    return hata == 0 ? 0 : 1;
}
#endif
