param(
    [Parameter(Mandatory=$true)] [string]$BuildRoot,
    [Parameter(Mandatory=$false)] [string]$TemplateReportRoot
)

$ErrorActionPreference = "Stop"

function Resolve-ReportPagesRoot {
    param([Parameter(Mandatory=$true)][string]$Root)
    $candidates = @(Get-ChildItem -Path $Root -Recurse -Directory -Filter '*.Report' |
        ForEach-Object {
            $pages = Join-Path $_.FullName 'definition\pages'
            if (Test-Path $pages -PathType Container) { Get-Item $pages }
        } | Sort-Object FullName)
    if ($candidates.Count -ne 1) {
        throw ("Visual artifact gate failed: expected exactly one report pages root under '{0}'; found {1}." -f $Root, $candidates.Count)
    }
    return $candidates[0].FullName
}

function Get-VisualMap {
    param([Parameter(Mandatory=$true)][string]$PagesRoot)
    $map = @{}
    foreach ($file in @(Get-ChildItem $PagesRoot -Recurse -Filter 'visual.json' -File | Sort-Object FullName)) {
        $relative = [IO.Path]::GetRelativePath($PagesRoot, $file.FullName)
        $map[$relative] = $file.FullName
    }
    return $map
}

function Convert-JsonNode {
    param([Parameter(Mandatory=$false)]$Node)
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
        foreach ($key in ($Node.Keys | ForEach-Object { [string]$_ } | Sort-Object)) {
            $result[$key] = Convert-JsonNode -Node $Node[$key]
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

function Test-AllowedMeasureTransformation {
    param(
        [Parameter(Mandatory=$true)][string]$TemplateValue,
        [Parameter(Mandatory=$true)][string]$GeneratedValue
    )
    if ($TemplateValue -eq '_Measures' -and $GeneratedValue -eq 'Fact_Pipeline_SampleData') { return $true }
    if ($TemplateValue.StartsWith('_Measures.') -and
        $GeneratedValue.StartsWith('Fact_Pipeline_SampleData.') -and
        $GeneratedValue.Substring('Fact_Pipeline_SampleData.'.Length) -eq $TemplateValue.Substring('_Measures.'.Length)) {
        return $true
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
            $differences.Add(("{0}: value differs (template={1}; generated={2})" -f $Path, $TemplateNode, $GeneratedNode))
        }
        return $differences
    }
    if ($TemplateNode -is [string] -and $GeneratedNode -is [string]) {
        if ($TemplateNode -ne $GeneratedNode -and !(Test-AllowedMeasureTransformation -TemplateValue $TemplateNode -GeneratedValue $GeneratedNode)) {
            $differences.Add(("{0}: string differs (template='{1}'; generated='{2}')" -f $Path, $TemplateNode, $GeneratedNode))
        }
        return $differences
    }
    $templateIsDictionary = $TemplateNode -is [System.Collections.IDictionary]
    $generatedIsDictionary = $GeneratedNode -is [System.Collections.IDictionary]
    if ($templateIsDictionary -or $generatedIsDictionary) {
        if (!($templateIsDictionary -and $generatedIsDictionary)) {
            $differences.Add(("{0}: node type differs" -f $Path))
            return $differences
        }
        $templateKeys = @($TemplateNode.Keys | ForEach-Object { [string]$_ } | Sort-Object)
        $generatedKeys = @($GeneratedNode.Keys | ForEach-Object { [string]$_ } | Sort-Object)
        foreach ($key in @($templateKeys | Where-Object { $_ -notin $generatedKeys })) {
            $differences.Add(("{0}.{1}: missing in generated visual" -f $Path, $key))
        }
        foreach ($key in @($generatedKeys | Where-Object { $_ -notin $templateKeys })) {
            $differences.Add(("{0}.{1}: extra in generated visual" -f $Path, $key))
        }
        foreach ($key in $templateKeys) {
            if ($key -in $generatedKeys) {
                $child = Compare-JsonNode -TemplateNode $TemplateNode[$key] -GeneratedNode $GeneratedNode[$key] -Path ("{0}.{1}" -f $Path, $key)
                foreach ($difference in $child) { $differences.Add($difference) }
            }
        }
        return $differences
    }
    $templateIsArray = $TemplateNode -is [System.Array]
    $generatedIsArray = $GeneratedNode -is [System.Array]
    if ($templateIsArray -or $generatedIsArray) {
        if (!($templateIsArray -and $generatedIsArray)) {
            $differences.Add(("{0}: node type differs" -f $Path))
            return $differences
        }
        if ($TemplateNode.Count -ne $GeneratedNode.Count) {
            $differences.Add(("{0}: array length differs (template={1}; generated={2})" -f $Path, $TemplateNode.Count, $GeneratedNode.Count))
            return $differences
        }
        for ($i = 0; $i -lt $TemplateNode.Count; $i++) {
            $child = Compare-JsonNode -TemplateNode $TemplateNode[$i] -GeneratedNode $GeneratedNode[$i] -Path ("{0}[{1}]" -f $Path, $i)
            foreach ($difference in $child) { $differences.Add($difference) }
        }
        return $differences
    }
    if ($TemplateNode.GetType() -ne $GeneratedNode.GetType() -or $TemplateNode -ne $GeneratedNode) {
        $differences.Add(("{0}: value differs (template={1}; generated={2})" -f $Path, $TemplateNode, $GeneratedNode))
    }
    return $differences
}

function Read-CanonicalJson {
    param([Parameter(Mandatory=$true)][string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw ("UTF-8 BOM: {0}" -f $Path)
    }
    try {
        $text = [Text.Encoding]::UTF8.GetString($bytes)
        return Convert-JsonNode -Node ($text | ConvertFrom-Json)
    }
    catch {
        throw ("Invalid JSON: {0}: {1}" -f $Path, $_.Exception.Message)
    }
}

if (!(Test-Path $BuildRoot -PathType Container)) {
    throw ("Visual artifact gate failed: BuildRoot does not exist: {0}" -f $BuildRoot)
}

if ([string]::IsNullOrWhiteSpace($TemplateReportRoot)) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $TemplateReportRoot = Join-Path $repoRoot 'pbip\Pipeline_SLA_Tracker.Report'
}
if (!(Test-Path $TemplateReportRoot -PathType Container)) {
    throw ("Visual artifact gate failed: authoritative template report does not exist: {0}" -f $TemplateReportRoot)
}

$generatedPages = Resolve-ReportPagesRoot -Root (Resolve-Path $BuildRoot).Path
$templatePages = Join-Path (Resolve-Path $TemplateReportRoot).Path 'definition\pages'
if (!(Test-Path $templatePages -PathType Container)) {
    throw ("Visual artifact gate failed: template definition/pages does not exist: {0}" -f $templatePages)
}

$generated = Get-VisualMap -PagesRoot $generatedPages
$template = Get-VisualMap -PagesRoot $templatePages
$errors = New-Object System.Collections.Generic.List[string]
Write-Host "VISUAL-PRESERVATION-GATE"
Write-Host ("Visual IDs: generated={0} template={1}" -f $generated.Count, $template.Count)
if ($generated.Count -ne 28 -or $template.Count -ne 28) {
    throw ("VISUAL-PRESERVATION-GATE failed: expected 28/28 visual files; generated={0}, template={1}." -f $generated.Count, $template.Count)
}

foreach ($path in @($template.Keys | Sort-Object)) {
    if (!$generated.ContainsKey($path)) {
        $errors.Add(("Missing generated visual: {0}" -f $path))
        continue
    }
    try { $templateObject = Read-CanonicalJson -Path $template[$path] } catch { $errors.Add($_.Exception.Message); continue }
    try { $generatedObject = Read-CanonicalJson -Path $generated[$path] } catch { $errors.Add($_.Exception.Message); continue }
    $differences = Compare-JsonNode -TemplateNode $templateObject -GeneratedNode $generatedObject -Path '$'
    foreach ($difference in $differences) { $errors.Add(("{0}: {1}" -f $path, $difference)) }
}
foreach ($path in @($generated.Keys | Sort-Object)) {
    if (!$template.ContainsKey($path)) { $errors.Add(("Unexpected generated visual: {0}" -f $path)) }
}
$visualDirs = @(Get-ChildItem $generatedPages -Recurse -Directory | Where-Object { $_.Parent.Name -eq 'visuals' })
foreach ($dir in $visualDirs) {
    $visualJson = Join-Path $dir.FullName 'visual.json'
    if (!(Test-Path $visualJson -PathType Leaf)) { $errors.Add(("Orphaned visual folder: {0}" -f $dir.FullName)) }
}
if ($errors.Count -gt 0) {
    Write-Host ("VISUAL-PRESERVATION-GATE|FAIL|Issues={0}" -f $errors.Count)
    $errors | ForEach-Object { Write-Host ("VISUAL-PRESERVATION-GATE|ERROR|{0}" -f $_) }
    throw "VISUAL-PRESERVATION-GATE failed."
}
Write-Host "VISUAL-PRESERVATION-GATE|PASS|Visuals=28/28|RecursiveJsonDiff=PASS|Canonicalization=PASS|AllowedMeasureTransformationsOnly=PASS|OrphanFolders=0"
