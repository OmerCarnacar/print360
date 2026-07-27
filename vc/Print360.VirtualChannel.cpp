// ============================================================
//  Print360 - RDP Yazdirma ve Yonetim Cozumu
//  Gelistirici : Omer CARNACAR  <omer.carnacar@outlook.com.tr>
//  LinkedIn    : https://www.linkedin.com/in/omercarnacar/
//  Lisans      : UCRETSIZ SURUM - para ile satilamaz (bkz. LICENSE)
//  Telif       : (c) 2026 Omer CARNACAR
// ============================================================
//  Print360 - RDP Static Virtual Channel istemci eklentisi (TSPrint mantigi)
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
#define OUTBOX "C:\\Print360\\vc-outbox"
// Kanal acikken olusturulan isaret dosyasi. ClientAgent buna bakarak
// "RDP kanali gercekten calisiyor mu?" sorusunu tahminsiz yanitlar:
//   dosya VAR  -> mstsc eklentisi yuklu ve kanal acik  -> TSPrint modu
//   dosya YOK  -> eklenti yok / RDP kapali             -> HTTPS'e dus
#define AKTIF_ISARET "C:\\Print360\\vc-outbox\\.vc-aktif"

// Cerceve sabitleri
#define BASLIK_BOYU 20
#define TIP_BASLA 1
#define TIP_VERI  2
#define TIP_BITTI 3

// Isin yazilacagi klasor (test harness bunu degistirebilir)
static std::string g_jobsDir = "C:\\Print360\\jobs";

// ---- Suren isin durumu ----
static HANDLE      g_isDosya   = INVALID_HANDLE_VALUE;
static std::string g_isPartYol;      // <hedef>.part
static std::string g_isSonYol;       // <hedef>
static DWORD       g_isId       = 0;
static DWORD       g_isToplam   = 0; // beklenen toplam bayt
static DWORD       g_isYazilan  = 0;
static DWORD       g_isBlok     = 0; // alinan blok sayisi

static DWORD OkuDword(const BYTE* p) { DWORD v; memcpy(&v, p, 4); return v; }

static void IsiKapat(bool basarili)
{
    if (g_isDosya != INVALID_HANDLE_VALUE) { CloseHandle(g_isDosya); g_isDosya = INVALID_HANDLE_VALUE; }
    if (basarili && !g_isPartYol.empty())
        MoveFileExA(g_isPartYol.c_str(), g_isSonYol.c_str(), MOVEFILE_REPLACE_EXISTING);
    else if (!g_isPartYol.empty())
        DeleteFileA(g_isPartYol.c_str());   // yarim is basilmasin
    g_isPartYol.clear(); g_isSonYol.clear();
    g_isId = 0; g_isToplam = 0; g_isYazilan = 0; g_isBlok = 0;
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
    if (ad.empty()) ad = "is.pdf";
}

// ---- TEK CERCEVEYI ISLE (test harness dogrudan bunu cagirir) ----
// Donus: true = cerceve gecerli ve islendi
bool P360_CerceveIsle(const BYTE* f, size_t len)
{
    if (len < BASLIK_BOYU) return false;
    if (!(f[0] == 'P' && f[1] == '3' && f[2] == '6' && f[3] == '0')) return false;
    if (f[4] != 2) return false;                       // surum
    BYTE tip     = f[5];
    DWORD isId   = OkuDword(f + 8);
    DWORD blokNo = OkuDword(f + 12);
    DWORD uzn    = OkuDword(f + 16);
    if ((size_t)BASLIK_BOYU + uzn > len) return false; // eksik cerceve
    const BYTE* veri = f + BASLIK_BOYU;

    if (tip == TIP_BASLA)
    {
        if (g_isDosya != INVALID_HANDLE_VALUE) IsiKapat(false);  // onceki yarim is
        if (uzn < 8) return false;
        DWORD toplam = OkuDword(veri);
        DWORD adLen  = OkuDword(veri + 4);
        if (8 + adLen > uzn) return false;
        std::string ad((const char*)(veri + 8), adLen);
        DosyaAdiTemizle(ad);

        CreateDirectoryA("C:\\Print360", NULL);
        CreateDirectoryA(g_jobsDir.c_str(), NULL);
        g_isSonYol  = g_jobsDir + "\\" + ad;
        g_isPartYol = g_isSonYol + ".part";
        g_isDosya = CreateFileA(g_isPartYol.c_str(), GENERIC_WRITE, 0, NULL,
                                CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        if (g_isDosya == INVALID_HANDLE_VALUE) { g_isPartYol.clear(); return false; }
        g_isId = isId; g_isToplam = toplam; g_isYazilan = 0; g_isBlok = 0;
        return true;
    }

    if (tip == TIP_VERI)
    {
        if (g_isDosya == INVALID_HANDLE_VALUE || isId != g_isId) return false;
        if (blokNo != g_isBlok) { IsiKapat(false); return false; }   // sira bozuldu
        DWORD yazilan = 0;
        if (uzn > 0 && !WriteFile(g_isDosya, veri, uzn, &yazilan, NULL)) { IsiKapat(false); return false; }
        g_isYazilan += yazilan;
        g_isBlok++;
        return true;
    }

    if (tip == TIP_BITTI)
    {
        if (g_isDosya == INVALID_HANDLE_VALUE || isId != g_isId) return false;
        DWORD beklenenBlok = (uzn >= 4) ? OkuDword(veri) : g_isBlok;
        bool tamam = (g_isYazilan == g_isToplam) && (g_isBlok == beklenenBlok);
        IsiKapat(tamam);      // eksikse .part silinir, yarim is BASILMAZ
        return tamam;
    }
    return false;
}

// Test harness'in hedef klasoru degistirmesi icin
void P360_JobsDirAyarla(const char* d) { g_jobsDir = d; }

#ifndef P360_TEST
// ============================================================
//                  GERCEK RDP EKLENTISI
// ============================================================
static PCHANNEL_ENTRY_POINTS g_ep = NULL;
static LPVOID g_initHandle = NULL;
static DWORD  g_openHandle = 0;
static std::vector<BYTE> g_buffer;   // WTS katmani cerceveyi 1600'luk parcalara bolebilir
static volatile bool g_calisiyor = false;

// Kanal veri olayi: parcalar CHANNEL_FLAG_FIRST/LAST ile gelir, once TAM
// cerceve birlestirilir, sonra islenir.
static VOID VCAPITYPE OpenEvent(DWORD openHandle, UINT event, LPVOID pData,
                                UINT32 dataLength, UINT32 totalLength, UINT32 dataFlags)
{
    (void)openHandle;
    if (event != CHANNEL_EVENT_DATA_RECEIVED) return;
    if (dataFlags & CHANNEL_FLAG_FIRST) { g_buffer.clear(); g_buffer.reserve(totalLength); }
    g_buffer.insert(g_buffer.end(), (BYTE*)pData, (BYTE*)pData + dataLength);
    if (dataFlags & CHANNEL_FLAG_LAST)
    {
        if (!g_buffer.empty()) P360_CerceveIsle(g_buffer.data(), g_buffer.size());
        g_buffer.clear();
    }
}

// Ters yon: ClientAgent'in vc-outbox'a birakttigi onay/sayac dosyalarini
// kanaldan sunucuya yaz (istemci -> sunucu).
static DWORD WINAPI OutboxThread(LPVOID)
{
    CreateDirectoryA("C:\\Print360", NULL);
    CreateDirectoryA(OUTBOX, NULL);
    while (g_calisiyor)
    {
        WIN32_FIND_DATAA fd;
        HANDLE hf = FindFirstFileA(OUTBOX "\\*.msg", &fd);
        if (hf != INVALID_HANDLE_VALUE)
        {
            do
            {
                if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
                std::string yol = std::string(OUTBOX) + "\\" + fd.cFileName;
                HANDLE h = CreateFileA(yol.c_str(), GENERIC_READ, 0, NULL, OPEN_EXISTING, 0, NULL);
                if (h == INVALID_HANDLE_VALUE) continue;
                DWORD sz = GetFileSize(h, NULL);
                std::vector<BYTE> buf(sz ? sz : 1);
                DWORD okundu = 0;
                if (sz > 0) ReadFile(h, &buf[0], sz, &okundu, NULL);
                CloseHandle(h);
                if (okundu > 0 && g_openHandle)
                {
                    UINT rc = g_ep->pVirtualChannelWrite(g_openHandle, &buf[0], okundu, &buf[0]);
                    if (rc == CHANNEL_RC_OK) DeleteFileA(yol.c_str());
                }
            } while (FindNextFileA(hf, &fd) && g_calisiyor);
            FindClose(hf);
        }
        Sleep(1000);
    }
    return 0;
}

static VOID VCAPITYPE InitEvent(LPVOID pInitHandle, UINT event, LPVOID pData, UINT dataLength)
{
    (void)pData; (void)dataLength;
    if (event == CHANNEL_EVENT_CONNECTED)
    {
        UINT rc = g_ep->pVirtualChannelOpen(pInitHandle, &g_openHandle, (PCHAR)KANAL_ADI, OpenEvent);
        if (rc == CHANNEL_RC_OK)
        {
            // Kanal ACIK: isaret dosyasini birak (ClientAgent TSPrint moduna gecer)
            CreateDirectoryA("C:\\Print360", NULL);
            CreateDirectoryA(OUTBOX, NULL);
            HANDLE m = CreateFileA(AKTIF_ISARET, GENERIC_WRITE, FILE_SHARE_READ, NULL,
                                   CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
            if (m != INVALID_HANDLE_VALUE) { DWORD w; WriteFile(m, "1", 1, &w, NULL); CloseHandle(m); }
            if (!g_calisiyor) { g_calisiyor = true; CreateThread(NULL, 0, OutboxThread, NULL, 0, NULL); }
        }
    }
    else if (event == CHANNEL_EVENT_DISCONNECTED)
    {
        g_calisiyor = false;
        DeleteFileA(AKTIF_ISARET);                                // kanal kapandi -> HTTPS'e dusulsun
        if (g_isDosya != INVALID_HANDLE_VALUE) IsiKapat(false);   // yarim is temizlensin
        if (g_openHandle) { g_ep->pVirtualChannelClose(g_openHandle); g_openHandle = 0; }
        g_buffer.clear();
    }
}

// mstsc.exe DLL'i yukleyince cagirdigi giris noktasi (SVC).
extern "C" BOOL VCAPITYPE VirtualChannelEntry(PCHANNEL_ENTRY_POINTS pEntryPoints)
{
    g_ep = pEntryPoints;
    CHANNEL_DEF cd;
    ZeroMemory(&cd, sizeof(cd));
    lstrcpynA(cd.name, KANAL_ADI, sizeof(cd.name));
    cd.options = CHANNEL_OPTION_INITIALIZED | CHANNEL_OPTION_ENCRYPT_RDP;

    UINT rc = g_ep->pVirtualChannelInit(&g_initHandle, &cd, 1,
                                        VIRTUAL_CHANNEL_VERSION_WIN2000, InitEvent);
    return (rc == CHANNEL_RC_OK) ? TRUE : FALSE;
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
        if (!P360_CerceveIsle(&tum[ofs], cLen)) {
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
