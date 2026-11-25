@echo off
setlocal
cd /d "%~dp0"

REM 32-bit (x86) derleme - zenon OPC DA sunucusu 32-bit zenOpcSrv'dir.
set CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe

if not exist "%CSC%" (
  echo csc.exe bulunamadi: %CSC%
  echo .NET Framework 4.8 Developer Pack / targeting pack veya Visual Studio gerekir.
  echo Alternatif: Visual Studio ile GopOpcOkuyucu.csproj ac (Platform = x86).
  exit /b 1
)

if not exist "bin\Release" mkdir "bin\Release"

echo Derleniyor (x86, .NET Framework 4.8)...
"%CSC%" /nologo /target:exe /platform:x86 /optimize+ /out:bin\Release\GopOpcOkuyucu.exe /reference:System.dll /reference:System.Core.dll Program.cs OpcDaClient.cs OpcDaInterfaces.cs OpcDaBrowse.cs Properties\AssemblyInfo.cs
if errorlevel 1 (
  echo Derleme basarisiz.
  exit /b 1
)

echo.
echo Tamam: bin\Release\GopOpcOkuyucu.exe
echo Calistirma ornegi: bin\Release\GopOpcOkuyucu.exe --tags taglar.txt
echo.
exit /b 0
