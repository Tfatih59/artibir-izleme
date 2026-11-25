@echo off
setlocal
cd /d "%~dp0"
echo GOP OPC-DA Okuyucu v0.3 - surekli mod (10 sn)
echo Durdurmak icin Ctrl+C
echo.
if not exist "GopOpcOkuyucu.exe" (
  echo HATA: GopOpcOkuyucu.exe bu klasorde yok.
  echo Su anki klasor: %CD%
  echo.
  pause
  exit /b 1
)
if not exist "taglar.txt" (
  echo HATA: taglar.txt bu klasorde yok.
  echo.
  pause
  exit /b 1
)
echo Etiket dosyasi: taglar.txt
echo Klasor: %CD%
echo.
GopOpcOkuyucu.exe --tags taglar.txt --interval 10
echo.
pause
