$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$source = Get-Content (Join-Path $repoRoot 'src\Core.cs') -Raw
if ($source -notmatch 'const string Version="([0-9.]+)"') { throw 'Application version was not found' }
$version = $Matches[1]
$packageRoot = Join-Path $repoRoot 'dist\CanonServiceDesk'
New-Item -Path $packageRoot -ItemType Directory -Force | Out-Null
foreach ($name in @('CanonServiceDesk.exe','CanonServiceDesk.exe.config','README.md','README-RU.txt','LICENSE.txt','VALIDATION.txt','CHANGELOG.md','build.cmd')) { Copy-Item (Join-Path $repoRoot $name) $packageRoot }
foreach ($name in @('src','tests','trace','docs')) {
    $target = Join-Path $packageRoot $name
    New-Item -Path $target -ItemType Directory -Force | Out-Null
    Get-ChildItem (Join-Path $repoRoot $name) -File | Where-Object { $_.Extension -in @('.cs','.py','.js','.cmd','.txt','.md') } | Copy-Item -Destination $target
}
"Version: $version`r`nSource commit: $env:GITHUB_SHA`r`nBuilt with Microsoft .NET Framework on Windows Server 2022.`r`nPhysical printer operations are not validated by CI." | Set-Content (Join-Path $packageRoot 'BUILD.txt') -Encoding UTF8
$archive = Join-Path $repoRoot ("dist\CanonServiceDesk-v" + $version + '-Windows.zip')
Compress-Archive -Path $packageRoot -DestinationPath $archive -Force
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content (Join-Path $repoRoot 'dist\SHA256SUMS.txt') -Encoding ASCII
Write-Output "Packaged $archive SHA256=$hash"
