#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$output = Join-Path ([IO.Path]::GetTempPath()) "etu-resources-$([Guid]::NewGuid().ToString('N')).csv"
try {
    & (Join-Path $PSScriptRoot '../scripts/Collect-Resources.ps1') -DurationMinutes 0 -OutputPath $output | Out-Null
    $rows = @(Import-Csv -LiteralPath $output)
    $expected = @('TimestampUtc', 'ProcessName', 'ProcessId', 'StartTimeUtc', 'FileVersion', 'Handles', 'PrivateBytes', 'WorkingSetBytes', 'Threads', 'CPUSeconds', 'GDIObjects', 'USERObjects')
    if (($rows[0].PSObject.Properties.Name -join ',') -ne ($expected -join ',')) { throw 'Unexpected CSV headers.' }
    foreach ($name in @('ExplorerTabUtility', 'explorer')) {
        $matches = @($rows | Where-Object ProcessName -eq $name)
        if ($matches.Count -eq 0) { throw "Missing process sample: $name" }
        foreach ($row in $matches) {
            if ([int]$row.ProcessId -le 0) { throw 'Invalid process ID.' }
            [void][DateTime]::Parse($row.TimestampUtc)
            [void][DateTime]::Parse($row.StartTimeUtc)
            foreach ($field in @('Handles', 'Threads', 'GDIObjects', 'USERObjects')) {
                $number = [uint32]0
                if (-not [uint32]::TryParse($row.$field, [ref]$number)) { throw "Invalid $field metric for $name." }
            }
            if ([int64]$row.PrivateBytes -le 0 -or [int64]$row.WorkingSetBytes -le 0) { throw "Invalid memory metrics for $name." }
            if ([string]::IsNullOrWhiteSpace($row.FileVersion)) { throw "Missing process version for $name." }
        }
    }
    Write-Output 'PASS: resource CSV headers, ETU/explorer rows, timestamps, PID, GUI counts, memory and version.'
}
finally {
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output }
}
