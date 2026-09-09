@echo off
setlocal
cd /d "%~dp0"
set "CANON_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CANON_CSC%" set "CANON_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CANON_CSC%" /nologo /codepage:65001 /target:exe /out:CoreTests.exe /r:System.Drawing.dll /r:System.Web.Extensions.dll ..\src\Core.cs ..\src\NativeUsb.cs ..\src\ServiceCore.cs ..\src\PrintSpooler.cs CoreTests.cs ServiceTests.cs
if errorlevel 1 goto failed
CoreTests.exe
if errorlevel 1 goto failed
echo C# tests passed. Python and Node tests are documented in VALIDATION.txt.
pause
exit /b 0
:failed
echo Tests failed. See output above.
pause
exit /b 1
