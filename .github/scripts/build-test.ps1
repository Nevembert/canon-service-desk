$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) { throw '.NET Framework compiler not found' }

& $compiler /nologo /codepage:65001 /warnaserror /target:winexe /platform:anycpu /optimize+ /out:CanonServiceDesk.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll src/Core.cs src/NativeUsb.cs src/Program.cs
if ($LASTEXITCODE -ne 0) { throw 'Application build failed' }

& $compiler /nologo /codepage:65001 /warnaserror /target:exe /out:tests/CoreTests.exe /r:System.Drawing.dll /r:System.Web.Extensions.dll src/Core.cs src/NativeUsb.cs tests/CoreTests.cs
if ($LASTEXITCODE -ne 0) { throw 'Tests build failed' }
& .\tests\CoreTests.exe
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
