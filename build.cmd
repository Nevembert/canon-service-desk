@echo off
setlocal
cd /d "%~dp0"
set "CANON_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CANON_CSC%" set "CANON_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CANON_CSC%" (
  echo .NET Framework C# compiler was not found.
  pause
  exit /b 1
)
"%CANON_CSC%" /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ /out:CanonServiceDesk.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll src\*.cs
if errorlevel 1 (
  pause
  exit /b 1
)
echo Build succeeded: CanonServiceDesk.exe
pause
