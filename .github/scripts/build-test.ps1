$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) { throw '.NET Framework compiler not found' }

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$appPath = Join-Path $repoRoot 'CanonServiceDesk.exe'
$testPath = Join-Path $repoRoot 'tests\CoreTests.exe'
$coreSource = Join-Path $repoRoot 'src\Core.cs'
$usbSource = Join-Path $repoRoot 'src\NativeUsb.cs'
$appSource = Join-Path $repoRoot 'src\Program.cs'
$testSource = Join-Path $repoRoot 'tests\CoreTests.cs'

$appArgs = @(
    '/nologo', '/codepage:65001', '/warnaserror', '/target:winexe',
    '/platform:anycpu', '/optimize+', ('/out:' + $appPath),
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    '/r:System.Web.Extensions.dll', '/r:System.IO.Compression.dll',
    '/r:System.IO.Compression.FileSystem.dll', $coreSource, $usbSource, $appSource
)
& $compiler @appArgs
if ($LASTEXITCODE -ne 0) { throw 'Application build failed' }

$testArgs = @(
    '/nologo', '/codepage:65001', '/warnaserror', '/target:exe',
    ('/out:' + $testPath), '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll',
    $coreSource, $usbSource, $testSource
)
& $compiler @testArgs
if ($LASTEXITCODE -ne 0) { throw 'Tests build failed' }
& $testPath
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
