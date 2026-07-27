@echo off
:: Print360 RDP Virtual Channel eklentisini derler (Print360.VC.dll)
:: GEREKSINIM: MSVC (Visual Studio "Desktop development with C++") + Windows SDK
::
:: YONTEM 1 - Developer Command Prompt icinden (en kolay):
::   Developer Command Prompt for VS'i acin, bu klasore gelin, "build-vc.cmd" yazin.
::   (Asagidaki blok INCLUDE/LIB zaten tanimliysa dogrudan cl calistirir.)
::
:: YONTEM 2 - Normal komut isteminden: asagidaki MSVC_VER / SDK_VER / SDK_DIR
::   degiskenlerini kendi surumlerinize gore duzeltin (Developer Prompt gerekmez).

setlocal

:: --- Ortam zaten kuruluysa (Developer Prompt) dogrudan derle ---
if defined INCLUDE goto :derle

:: --- Aksi halde MSVC + SDK yollarini elle kur (surumleri duzenleyin) ---
set "VSROOT=C:\Program Files\Microsoft Visual Studio\18\Professional"
for /d %%V in ("%VSROOT%\VC\Tools\MSVC\*") do set "MSVC=%%V"
set "SDK_DIR=C:\Program Files (x86)\Windows Kits\10"
for /d %%S in ("%SDK_DIR%\Include\*") do set "SDK_VER=%%~nxS"

set "INCLUDE=%MSVC%\include;%SDK_DIR%\Include\%SDK_VER%\ucrt;%SDK_DIR%\Include\%SDK_VER%\um;%SDK_DIR%\Include\%SDK_VER%\shared"
set "LIB=%MSVC%\lib\x64;%SDK_DIR%\Lib\%SDK_VER%\ucrt\x64;%SDK_DIR%\Lib\%SDK_VER%\um\x64"
set "CL_EXE=%MSVC%\bin\Hostx64\x64\cl.exe"
goto :derleManuel

:derle
set "CL_EXE=cl.exe"
:derleManuel
echo Derleyici: %CL_EXE%
"%CL_EXE%" /nologo /LD /O2 /EHsc Print360.VirtualChannel.cpp ^
   /Fe:Print360.VC.dll ^
   /link /DEF:Print360.VirtualChannel.def user32.lib kernel32.lib

if %ERRORLEVEL%==0 (
  echo.
  echo BASARILI: Print360.VC.dll uretildi.
  echo Istemci kurulumu bu DLL'i C:\Print360\Print360.VC.dll'e kopyalayip
  echo registry AddIns kaydini yapmalidir ^(bkz. VIRTUALCHANNEL.md^).
) else (
  echo DERLEME BASARISIZ - MSVC/SDK yollari dogru mu? cchannel.h bulunuyor mu?
)
endlocal
