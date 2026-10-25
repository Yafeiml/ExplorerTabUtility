$ErrorActionPreference = 'Stop'
$testRoot = Join-Path $PSScriptRoot ('..\artifacts\update-test-' + [Guid]::NewGuid().ToString('N'))
$sourceDir = Join-Path $testRoot 'source'
$installDir = Join-Path $testRoot 'installed'
New-Item -ItemType Directory -Path $sourceDir, $installDir -Force | Out-Null
Set-Content -LiteralPath (Join-Path $sourceDir 'ExplorerTabUtility.exe') -Value 'new'
Set-Content -LiteralPath (Join-Path $sourceDir 'ExplorerTabUtility.runtimeconfig.json') -Value '{}'
Set-Content -LiteralPath (Join-Path $installDir 'ExplorerTabUtility.exe') -Value 'old'

# Isolate process detection; the test never stops or changes the real installation.
function Get-Process { param([string]$Name, $ErrorAction) }
$updateScript = Join-Path $PSScriptRoot '..\scripts\Update-Local.ps1'
& $updateScript -SourceDir $sourceDir -InstallDir $installDir -WhatIf
if ((Get-Content -LiteralPath (Join-Path $installDir 'ExplorerTabUtility.exe')) -ne 'old') {
    throw 'WhatIf modified the installation.'
}
& $updateScript -SourceDir $sourceDir -InstallDir $installDir
$backups = @(Get-ChildItem -LiteralPath $testRoot -Directory -Filter 'ExplorerTabUtility.rollback-*')
if ((Get-Content -LiteralPath (Join-Path $installDir 'ExplorerTabUtility.exe')) -ne 'new' -or
    $backups.Count -ne 1 -or
    (Get-Content -LiteralPath (Join-Path $backups[0].FullName 'ExplorerTabUtility.exe')) -ne 'old') {
    throw 'Updating failed to preserve the old installation.'
}
try {
    & $updateScript -SourceDir $sourceDir -InstallDir ([IO.Path]::GetPathRoot($installDir)) -WhatIf
    throw 'The disk root was accepted.'
}
catch {
    if ($_.Exception.Message -eq 'The disk root was accepted.') { throw }
}
Write-Output 'PASS: update staging, file hashes, WhatIf, rollback copy and root path rejection.'
