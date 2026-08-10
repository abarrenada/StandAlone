<#
.SYNOPSIS
    Imports SAP schedule order CSV files (El Paso format) into itemdet.csv.

.DESCRIPTION
    Reads one or more SAP MRP schedule export CSVs and appends rows to itemdet.csv.
    Each schedule row maps to one itemdet row with schedule-specific fields populated
    (LastScheduleOrder, OpenQty, ScheduleDate); all itemdet master columns are left
    as empty placeholders to be filled in separately.

    Expected SAP schedule columns (by name):
        "Material Number"                   -> ItemNumber
        "Order"                             -> LastScheduleOrder
        "Open Qty. (ZZOPENQTY) (ZZALTUOM)" -> OpenQty  (commas stripped, FT2 units)
        "Basic finish date"                 -> ScheduleDate (M/D/YYYY -> YYYY-MM-DD)

.PARAMETER ScheduleFiles
    One or more paths to SAP schedule CSV files to import.
    Accepts wildcards, e.g. "$dataDir\Elpaso Schedule *.csv"

.PARAMETER ItemdetPath
    Full path to itemdet.csv. Defaults to .\data\itemdet.csv relative to this script.

.PARAMETER WhatIf
    Preview how many rows would be added without writing to itemdet.csv.

.EXAMPLE
    .\Import-ScheduleToItemdet.ps1 -ScheduleFiles "data\Elpaso Schedule 08_16.csv","data\Elapso Schdule 08_23.csv"

.EXAMPLE
    .\Import-ScheduleToItemdet.ps1 -ScheduleFiles "data\Elpaso Schedule *.csv" -WhatIf
#>

param(
    [Parameter(Mandatory)]
    [string[]] $ScheduleFiles,

    [string] $ItemdetPath = (Join-Path $PSScriptRoot '..\data\itemdet.csv'),

    [switch] $WhatIf
)

function ConvertDate([string]$d) {
    if ($d -match '^(\d+)/(\d+)/(\d+)$') {
        return '{0:D4}-{1:D2}-{2:D2}' -f [int]$Matches[3], [int]$Matches[1], [int]$Matches[2]
    }
    return $d
}

function Make-ItemdetRow([string]$matNum, [string]$order, [string]$openQty, [string]$schedDate) {
    # 47 columns: IRef(0) through ScheduleDate(46)
    # Columns 2-43 are itemdet master data — left as 0/"" placeholders
    $cols = @(
        '0', $matNum,
        '0','0','""','0',
        '0','0','0','0',
        '""','0','0','""','""',
        '0','0','false',
        '""','""','""',
        '""','""','""',
        '""','""','""','""','""',
        '0','""',
        '0','0','0','0',
        '0','0','0',
        '""','""','""','""','""',
        '0',
        $order, $openQty, $schedDate
    )
    return ($cols -join ',')
}

# Resolve wildcards
$resolvedFiles = $ScheduleFiles | ForEach-Object { Resolve-Path $_ -ErrorAction SilentlyContinue } | Select-Object -ExpandProperty Path

if (-not $resolvedFiles) {
    Write-Error "No schedule files found matching the provided paths."
    exit 1
}

$newLines = [System.Collections.Generic.List[string]]::new()

foreach ($file in $resolvedFiles) {
    Write-Host "Reading: $file"
    $rows = Import-Csv -Path $file

    foreach ($row in $rows) {
        $matNum    = $row.'Material Number'.Trim()
        $order     = $row.'Order'.Trim()
        $openQty   = ($row.'Open Qty. (ZZOPENQTY) (ZZALTUOM)' -replace ',', '').Trim()
        $schedDate = ConvertDate $row.'Basic finish date'.Trim()

        if (-not $matNum) { continue }

        $newLines.Add((Make-ItemdetRow $matNum $order $openQty $schedDate))
    }
}

if ($WhatIf) {
    Write-Host "WhatIf: would append $($newLines.Count) rows to $ItemdetPath"
    $newLines | Select-Object -First 3 | ForEach-Object { Write-Host "  $_" }
    exit 0
}

if (-not (Test-Path $ItemdetPath)) {
    Write-Error "itemdet.csv not found at: $ItemdetPath"
    exit 1
}

Add-Content -Path $ItemdetPath -Value ($newLines -join "`r`n") -Encoding UTF8
Write-Host "Done — appended $($newLines.Count) rows to $ItemdetPath"
