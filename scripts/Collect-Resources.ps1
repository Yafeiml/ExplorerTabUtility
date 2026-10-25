#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateRange(0, 1440)][int]$DurationMinutes = 60,
    [ValidateRange(1, 3600)][int]$SampleSeconds = 30,
    [string]$OutputPath = (Join-Path $PSScriptRoot "../artifacts/diagnostics/resource-$(Get-Date -Format 'yyyyMMdd-HHmmss').csv")
)

$ErrorActionPreference = 'Stop'
if (-not ('ExplorerTabUtility.Diagnostics.ResourceProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace ExplorerTabUtility.Diagnostics {
    public static class ResourceProbe {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetGuiResources(IntPtr process, uint flags);
    }
}
'@
}
$OutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) { throw "Output already exists: $OutputPath" }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath))
$finish = [DateTime]::UtcNow.AddMinutes($DurationMinutes)
do {
    foreach ($process in (Get-Process -Name ExplorerTabUtility, explorer -ErrorAction SilentlyContinue)) {
        try {
            $row = [ordered]@{
                TimestampUtc = [DateTime]::UtcNow.ToString('o'); ProcessName = $process.ProcessName; ProcessId = $process.Id
                StartTimeUtc = $null; FileVersion = $null; Handles = $null; PrivateBytes = $null; WorkingSetBytes = $null
                Threads = $null; CPUSeconds = $null; GDIObjects = $null; USERObjects = $null
            }
            try { $process.Refresh() } catch {}
            try { $row.StartTimeUtc = $process.StartTime.ToUniversalTime().ToString('o') } catch {}
            try { $row.FileVersion = $process.MainModule.FileVersionInfo.FileVersion } catch {}
            foreach ($metric in @{ Handles = 'HandleCount'; PrivateBytes = 'PrivateMemorySize64'; WorkingSetBytes = 'WorkingSet64' }.GetEnumerator()) {
                try { $row[$metric.Key] = $process.($metric.Value) } catch {}
            }
            try { $row.Threads = $process.Threads.Count } catch {}
            try { $row.CPUSeconds = $process.TotalProcessorTime.TotalSeconds } catch {}
            try {
                $handle = $process.Handle
                foreach ($metric in @{ GDIObjects = 0; USERObjects = 1 }.GetEnumerator()) {
                    $count = [ExplorerTabUtility.Diagnostics.ResourceProbe]::GetGuiResources($handle, $metric.Value)
                    if ($count -ne 0 -or [Runtime.InteropServices.Marshal]::GetLastWin32Error() -eq 0) {
                        $row[$metric.Key] = $count
                    }
                }
            } catch {}
            [pscustomobject]$row | Export-Csv -LiteralPath $OutputPath -Append -NoTypeInformation -Encoding utf8
        }
        finally { $process.Dispose() }
    }
    if ($DurationMinutes -eq 0) { break }
    $remaining = ($finish - [DateTime]::UtcNow).TotalMilliseconds
    if ($remaining -le 0) { break }
    Start-Sleep -Milliseconds ([int][Math]::Min($SampleSeconds * 1000, $remaining))
} while ([DateTime]::UtcNow -lt $finish)
if (-not (Test-Path -LiteralPath $OutputPath)) { throw 'ExplorerTabUtility and explorer were not running; no samples were collected.' }
Write-Output $OutputPath
