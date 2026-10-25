#Requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDir = 'D:\app\ExplorerTabUtility',
    [string]$SourceDir = (Join-Path $PSScriptRoot '..\publish\net10.0-windows\x64')
)

$ErrorActionPreference = 'Stop'
$sourcePath = (Resolve-Path -LiteralPath $SourceDir).Path
$installPath = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
$parentPath = Split-Path -Parent $installPath
if (!$parentPath -or $installPath -eq [IO.Path]::GetPathRoot($installPath).TrimEnd('\') -or
    $sourcePath -eq $installPath -or $sourcePath.StartsWith($installPath + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $installPath.StartsWith($sourcePath + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw '安装目录必须是独立的应用目录，不能是磁盘根目录或发布目录。'
}
$sourceExe = Join-Path $sourcePath 'ExplorerTabUtility.exe'
if (!(Test-Path -LiteralPath $sourceExe -PathType Leaf) -or
    !(Test-Path -LiteralPath (Join-Path $sourcePath 'ExplorerTabUtility.runtimeconfig.json') -PathType Leaf)) {
    throw '找不到完整的 .NET 10 x64 发布目录，请先完成构建。'
}
if (Test-Path -LiteralPath $installPath) {
    if (!(Test-Path -LiteralPath (Join-Path $installPath 'ExplorerTabUtility.exe') -PathType Leaf)) {
        throw '目标目录不是现有的 ExplorerTabUtility 安装目录。'
    }
}
if (!$PSCmdlet.ShouldProcess($installPath, '安装新版本并保留完整旧版本用于回滚')) { return }
if (Get-Process -Name ExplorerTabUtility -ErrorAction SilentlyContinue) {
    throw '请先从托盘菜单退出 ExplorerTabUtility，再执行更新；脚本不会强制结束进程。'
}

$suffix = [Guid]::NewGuid().ToString('N')
$stagePath = Join-Path $parentPath ('ExplorerTabUtility.stage-' + $suffix)
$backupPath = Join-Path $parentPath ('ExplorerTabUtility.rollback-' + $suffix)
New-Item -ItemType Directory -Path $parentPath -Force | Out-Null
Copy-Item -LiteralPath $sourcePath -Destination $stagePath -Recurse
$sourceFiles = Get-ChildItem -LiteralPath $sourcePath -File -Recurse
foreach ($file in $sourceFiles) {
    $stagedFile = Join-Path $stagePath ([IO.Path]::GetRelativePath($sourcePath, $file.FullName))
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $stagedFile).Hash) {
        throw "暂存文件校验失败，原安装未修改：$stagedFile"
    }
}
$hadInstall = Test-Path -LiteralPath $installPath
if ($hadInstall) { Move-Item -LiteralPath $installPath -Destination $backupPath }
try {
    Move-Item -LiteralPath $stagePath -Destination $installPath
}
catch {
    if ($hadInstall -and !(Test-Path -LiteralPath $installPath)) {
        Move-Item -LiteralPath $backupPath -Destination $installPath
    }
    throw
}
Write-Output "已更新：$installPath"
if ($hadInstall) { Write-Output "旧版本备份：$backupPath" }
Write-Output '用户设置和快捷键继续保存在 AppData，未改动。请启动安装目录中的 ExplorerTabUtility.exe。'
