$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$test = Join-Path $repoRoot 'tests\QueueIntegration.exe'
$compileArgs = @('/nologo','/codepage:65001','/warnaserror','/target:exe',('/out:' + $test),'/r:System.Drawing.dll','/r:System.Web.Extensions.dll')
foreach ($source in @('src\Core.cs','src\ServiceCore.cs','src\PrintSpooler.cs','tests\QueueIntegration.cs')) { $compileArgs += Join-Path $repoRoot $source }
& $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'Queue integration build failed' }
$queue = 'Canon Service Desk CI'
if (Get-Printer -Name $queue -ErrorAction SilentlyContinue) { throw 'Test queue already exists' }
try {
    Add-PrinterDriver -Name 'Generic / Text Only'
    Add-Printer -Name $queue -DriverName 'Generic / Text Only' -PortName 'FILE:'
    & $test (Join-Path $env:RUNNER_TEMP 'CanonQueueIntegration')
    if ($LASTEXITCODE -ne 0) { throw 'Queue integration failed' }
} finally {
    if (Get-Printer -Name $queue -ErrorAction SilentlyContinue) { Remove-Printer -Name $queue }
}
