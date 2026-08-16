param(
    [Parameter(Mandatory=$true)]
    [string]$BuildRoot
)

$ErrorActionPreference = "Stop"

function Resolve-ReportPagesRoot {
    param([Parameter(Mandatory=$true)][string]$Root)

    $candidates = @(Get-ChildItem -Path $Root -Recurse -Directory -Filter '*.Report' |
        ForEach-Object {
            $pages = Join-Path $_.FullName 'definition\pages'
            if (Test-Path $pages -PathType Container) { Get-Item $pages }
        } | Sort-Object FullName)

    if ($candidates.Count -eq 0) { throw "Visual artifact gate failed: found zero *.Report/definition/pages candidates under '$Root'." }
    if ($candidates.Count -gt 1) {
        throw "Visual artifact gate failed: found multiple *.Report/definition/pages candidates under '$Root': $($candidates.FullName -join '; ')"
    }
    return $candidates[0].FullName
}

if (!(Test-Path $BuildRoot -PathType Container)) {
    Write-Error "Visual artifact gate failed: BuildRoot does not exist: $BuildRoot"
    exit 2
}

try {
    $pagesRoot = Resolve-ReportPagesRoot -Root (Resolve-Path $BuildRoot).Path
    $visualDirs = @(Get-ChildItem $pagesRoot -Recurse -Directory | Where-Object { $_.Parent.Name -eq 'visuals' })
    $visualFiles = @(Get-ChildItem $pagesRoot -Recurse -Filter 'visual.json' -File | Sort-Object FullName)

    Write-Host "VISUAL-ARTIFACT-GATE|BuildRoot=$BuildRoot|PagesRoot=$pagesRoot|VisualCount=$($visualFiles.Count)|Expected=28"

    if ($visualFiles.Count -ne 28) {
        throw "Visual artifact gate failed: expected exactly 28 visual.json files, found $($visualFiles.Count)."
    }

    $errors = New-Object System.Collections.Generic.List[string]
    foreach ($file in $visualFiles) {
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
            $errors.Add("UTF-8 BOM: $($file.FullName)")
        }
        try {
            $jsonText = [Text.Encoding]::UTF8.GetString($bytes)
            $jsonText | ConvertFrom-Json | Out-Null
            if ($jsonText -match '(?i)_Measures') { $errors.Add("_Measures reference: $($file.FullName)") }
        } catch {
            $errors.Add("Invalid JSON: $($file.FullName): $($_.Exception.Message)")
        }
    }

    foreach ($dir in $visualDirs) {
        $visualJson = Join-Path $dir.FullName 'visual.json'
        if (!(Test-Path $visualJson -PathType Leaf)) {
            $errors.Add("Orphaned visual folder: $($dir.FullName)")
        }
    }

    if ($errors.Count -gt 0) {
        Write-Host "VISUAL-ARTIFACT-GATE|FAIL|Issues=$($errors.Count)"
        $errors | ForEach-Object { Write-Host "VISUAL-ARTIFACT-GATE|ERROR|$_" }
        exit 1
    }

    Write-Host "VISUAL-ARTIFACT-GATE|PASS|Visuals=28/28|Json=PASS|BOM=ABSENT|MeasuresEntity=PASS|OrphanFolders=0"
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
