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

function Resolve-TemplateReportPagesRoot {
    param([Parameter(Mandatory=$true)][string]$Root)

    $templateReport = Join-Path $Root 'pbip\Pipeline_SLA_Tracker.Report'
    $pages = Join-Path $templateReport 'definition\pages'
    if (!(Test-Path $pages -PathType Container)) {
        throw "Visual artifact gate failed: authoritative template pages root does not exist: $pages"
    }
    return (Resolve-Path $pages).Path
}

function Convert-JsonNode {
    param([Parameter(Mandatory=$true)]$Node)

    if ($null -eq $Node) { return $null }
    if ($Node -is [System.Management.Automation.PSCustomObject]) {
        $result = [ordered]@{}
        foreach ($property in ($Node.PSObject.Properties | Sort-Object Name)) {
            $result[$property.Name] = Convert-JsonNode -Node $property.Value
        }
        return $result
    }
    if ($Node -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in ($Node.Keys | Sort-Object)) {
            $result[[string]$key] = Convert-JsonNode -Node $Node[$key]
        }
        return $result
    }
    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        $result = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Node) { [void]$result.Add((Convert-JsonNode -Node $item)) }
        return ,$result.ToArray()
    }
    return $Node
}

function Test-AllowedStringTransformation {
    param(
        [Parameter(Mandatory=$true)][string]$TemplateValue,
        [Parameter(Mandatory=$true)][string]$GeneratedValue
    )

    if ($TemplateValue -eq '_Measures' -and $GeneratedValue -eq 'Fact_Pipeline_SampleData') {
        return $true
    }

    if ($TemplateValue -match '^_Measures\.([A-Za-z_][A-Za-z0-9_]*)$') {
        $measureName = $Matches[1]
        return $GeneratedValue -eq "Fact_Pipeline_SampleData.$measureName"
    }

    return $false
}

function Compare-JsonNode {
    param(
        [Parameter(Mandatory=$false)]$TemplateNode,
        [Parameter(Mandatory=$false)]$GeneratedNode,
        [Parameter(Mandatory=$true)][string]$Path
    )

    $differences = New-Object System.Collections.Generic.List[string]

    if ($null -eq $TemplateNode -or $null -eq $GeneratedNode) {
        if ($null -ne $TemplateNode -or $null -ne $GeneratedNode) {
            $differences.Add("$Path: value differs (template=$TemplateNode; generated=$GeneratedNode)")
        }
        return $differences
    }

    if ($TemplateNode -is [string] -and $GeneratedNode -is [string]) {
        if ($TemplateNode -ne $GeneratedNode -and !(Test-AllowedStringTransformation -TemplateValue $TemplateNode -GeneratedValue $GeneratedNode)) {
            $differences.Add("$Path: string differs (template='$TemplateNode'; generated='$GeneratedNode')")
        }
        return $differences
    }

    $templateIsDictionary = $TemplateNode -is [System.Collections.IDictionary]
    $generatedIsDictionary = $GeneratedNode -is [System.Collections.IDictionary]
    if ($templateIsDictionary -or $generatedIsDictionary) {
        if (!($templateIsDictionary -and $generatedIsDictionary)) {
            $differences.Add("$Path: node type differs")
            return $differences
        }

        $templateKeys = @($TemplateNode.Keys | ForEach-Object { [string]$_ } | Sort-Object)
        $generatedKeys = @($GeneratedNode.Keys | ForEach-Object { [string]$_ } | Sort-Object)
        $missingKeys = @($templateKeys | Where-Object { $_ -notin $generatedKeys })
        $extraKeys = @($generatedKeys | Where-Object { $_ -notin $templateKeys })

        foreach ($key in $missingKeys) { $differences.Add("$Path.$key: missing in generated visual") }
        foreach ($key in $extraKeys) { $differences.Add("$Path.$key: extra in generated visual") }

        foreach ($key in $templateKeys) {
            if ($key -in $generatedKeys) {
                $child = Compare-JsonNode -TemplateNode $TemplateNode[$key] -GeneratedNode $GeneratedNode[$key] -Path "$Path.$key"
                foreach ($difference in $child) { $differences.Add($difference) }
            }
        }
        return $differences
    }

    $templateIsArray = $TemplateNode -is [System.Array]
    $generatedIsArray = $GeneratedNode -is [System.Array]
    if ($templateIsArray -or $generatedIsArray) {
        if (!($templateIsArray -and $generatedIsArray)) {
            $differences.Add("$Path: node type differs")
            return $differences
        }
        if ($TemplateNode.Count -ne $GeneratedNode.Count) {
            $differences.Add("$Path: array length differs (template=$($TemplateNode.Count); generated=$($GeneratedNode.Count))")
            return $differences
        }
        for ($i = 0; $i -lt $TemplateNode.Count; $i++) {
            $child = Compare-JsonNode -TemplateNode $TemplateNode[$i] -GeneratedNode $GeneratedNode[$i] -Path "$Path[$i]"
            foreach ($difference in $child) { $differences.Add($difference) }
        }
        return $differences
    }

    if ($TemplateNode.GetType() -ne $GeneratedNode.GetType() -or $TemplateNode -ne $GeneratedNode) {
        $differences.Add("$Path: value differs (template=$TemplateNode; generated=$GeneratedNode)")
    }

    return $differences
}

function Read-CanonicalJson {
    param([Parameter(Mandatory=$true)][string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw "UTF-8 BOM: $Path"
    }

    try {
        $text = [Text.Encoding]::UTF8.GetString($bytes)
        $parsed = $text | ConvertFrom-Json
        return Convert-JsonNode -Node $parsed
    }
    catch {
        throw "Invalid JSON: $Path: $($_.Exception.Message)"
    }
}

if (!(Test-Path $BuildRoot -PathType Container)) {
    Write-Error "Visual artifact gate failed: BuildRoot does not exist: $BuildRoot"
    exit 2
}

try {
    $resolvedBuildRoot = (Resolve-Path $BuildRoot).Path
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $pagesRoot = Resolve-ReportPagesRoot -Root $resolvedBuildRoot
    $templatePagesRoot = Resolve-TemplateReportPagesRoot -Root $repoRoot

    $visualFiles = @(Get-ChildItem $pagesRoot -Recurse -Filter 'visual.json' -File | Sort-Object FullName)
    $templateVisualFiles = @(Get-ChildItem $templatePagesRoot -Recurse -Filter 'visual.json' -File | Sort-Object FullName)

    Write-Host "VISUAL-ARTIFACT-GATE|BuildRoot=$resolvedBuildRoot|PagesRoot=$pagesRoot|TemplatePagesRoot=$templatePagesRoot|VisualCount=$($visualFiles.Count)|TemplateVisualCount=$($templateVisualFiles.Count)|Expected=28"

    if ($visualFiles.Count -ne 28) {
        throw "Visual artifact gate failed: expected exactly 28 generated visual.json files, found $($visualFiles.Count)."
    }
    if ($templateVisualFiles.Count -ne 28) {
        throw "Visual artifact gate failed: expected exactly 28 template visual.json files, found $($templateVisualFiles.Count)."
    }

    $generatedByRelativePath = @{}
    foreach ($file in $visualFiles) {
        $relative = [IO.Path]::GetRelativePath($pagesRoot, $file.FullName)
        $generatedByRelativePath[$relative] = $file
    }

    $templateByRelativePath = @{}
    foreach ($file in $templateVisualFiles) {
        $relative = [IO.Path]::GetRelativePath($templatePagesRoot, $file.FullName)
        $templateByRelativePath[$relative] = $file
    }

    $errors = New-Object System.Collections.Generic.List[string]

    foreach ($relativePath in @($templateByRelativePath.Keys | Sort-Object)) {
        if (!$generatedByRelativePath.ContainsKey($relativePath)) {
            $errors.Add("Missing generated visual: $relativePath")
            continue
        }

        $templateFile = $templateByRelativePath[$relativePath]
        $generatedFile = $generatedByRelativePath[$relativePath]

        try {
            $templateJson = Read-CanonicalJson -Path $templateFile.FullName
        }
        catch {
            $errors.Add($_.Exception.Message)
            continue
        }

        try {
            $generatedJson = Read-CanonicalJson -Path $generatedFile.FullName
        }
        catch {
            $errors.Add($_.Exception.Message)
            continue
        }

        $differences = Compare-JsonNode -TemplateNode $templateJson -GeneratedNode $generatedJson -Path '$'
        foreach ($difference in $differences) {
            $errors.Add("$relativePath: $difference")
        }
    }

    foreach ($relativePath in @($generatedByRelativePath.Keys | Sort-Object)) {
        if (!$templateByRelativePath.ContainsKey($relativePath)) {
            $errors.Add("Unexpected generated visual: $relativePath")
        }
    }

    $visualDirs = @(Get-ChildItem $pagesRoot -Recurse -Directory | Where-Object { $_.Parent.Name -eq 'visuals' })
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

    Write-Host "VISUAL-ARTIFACT-GATE|PASS|Visuals=28/28|RecursiveJsonDiff=PASS|Canonicalization=PASS|AllowedMeasureRenameOnly=PASS|OrphanFolders=0"
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
