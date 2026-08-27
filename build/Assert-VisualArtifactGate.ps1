param(
    [Parameter(Mandatory=$true)] [string]$BuildRoot,
    [Parameter(Mandatory=$false)] [string]$TemplateReportRoot
)

$ErrorActionPreference = 'Stop'

function Resolve-ReportPagesRoot {
    param([Parameter(Mandatory=$true)][string]$Root)
    $candidates = @(Get-ChildItem -Path $Root -Recurse -Directory -Filter '*.Report' |
        ForEach-Object {
            $pages = Join-Path $_.FullName 'definition\pages'
            if (Test-Path $pages -PathType Container) { Get-Item $pages }
        } | Sort-Object FullName)
    if ($candidates.Count -ne 1) { throw ("Visual artifact gate failed: expected exactly one report pages root under '{0}'; found {1}." -f $Root, $candidates.Count) }
    $candidates[0].FullName
}

function Get-VisualMap {
    param([Parameter(Mandatory=$true)][string]$PagesRoot)
    $map = @{}
    foreach ($file in @(Get-ChildItem $PagesRoot -Recurse -Filter 'visual.json' -File | Sort-Object FullName)) {
        $map[[IO.Path]::GetRelativePath($PagesRoot, $file.FullName)] = $file.FullName
    }
    $map
}

function Get-CanonicalMeasureNames {
    param([Parameter(Mandatory=$true)][string]$BuildRoot)
    $files = @(Get-ChildItem $BuildRoot -Recurse -File -Filter '_Measure Table.tmdl' | Sort-Object FullName)
    if ($files.Count -ne 1) { throw ("VISUAL-PRESERVATION-GATE failed: expected exactly one canonical '_Measure Table.tmdl'; found {0}." -f $files.Count) }
    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in [IO.File]::ReadAllLines($files[0].FullName)) {
        if ($line -match "^\s*measure\s+'((?:''|[^'])+)'\s*=") { [void]$names.Add(($matches[1] -replace "''", "'")) }
    }
    if ($names.Count -eq 0) { throw "VISUAL-PRESERVATION-GATE failed: canonical _Measure Table contains no measures." }
    $names
}

function Test-AllowedMeasureTransformation {
    param(
        [Parameter(Mandatory=$true)][string]$TemplateValue,
        [Parameter(Mandatory=$true)][string]$GeneratedValue,
        [Parameter(Mandatory=$true)][System.Collections.Generic.HashSet[string]]$MeasureNames
    )
    if ($TemplateValue -eq $GeneratedValue) { return $true }
    $templateMatch = [regex]::Match($TemplateValue, '^(?:_Measures|Fact_Pipeline_SampleData)\.(?<name>.+)$')
    $generatedMatch = [regex]::Match($GeneratedValue, '^_Measure Table\.(?<name>.+)$')
    if ($templateMatch.Success -and $generatedMatch.Success) {
        return $templateMatch.Groups['name'].Value -eq $generatedMatch.Groups['name'].Value -and $MeasureNames.Contains($generatedMatch.Groups['name'].Value)
    }
    return $false
}

function Copy-JsonNode {
    param($Node)
    if ($null -eq $Node) { return $null }
    if ($Node -is [System.Collections.IDictionary]) {
        $copy = [ordered]@{}
        foreach ($key in $Node.Keys) { $copy[[string]$key] = Copy-JsonNode $Node[$key] }
        return $copy
    }
    if ($Node -is [System.Array]) {
        $copy = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Node) { [void]$copy.Add((Copy-JsonNode $item)) }
        return ,$copy.ToArray()
    }
    $Node
}

function Normalize-KnownMeasureObject {
    param(
        $Node,
        [Parameter(Mandatory=$true)][System.Collections.Generic.HashSet[string]]$MeasureNames
    )
    if ($null -eq $Node -or $Node -isnot [System.Collections.IDictionary]) { return $Node }
    if (!$Node.Contains('Expression')) { return $Node }
    $expression = $Node['Expression']
    if ($expression -isnot [System.Collections.IDictionary] -or !$expression.Contains('SourceRef')) { return $Node }
    $sourceRef = $expression['SourceRef']
    if ($sourceRef -isnot [System.Collections.IDictionary] -or !$sourceRef.Contains('Entity') -or !$sourceRef.Contains('Property')) { return $Node }
    $property = [string]$sourceRef['Property']
    if (!$MeasureNames.Contains($property)) { return $Node }
    $entity = [string]$sourceRef['Entity']
    if ($entity -notin @('_Measures','Fact_Pipeline_SampleData','_Measure Table')) { return $Node }
    $normalized = Copy-JsonNode $Node
    $normalized['Expression']['SourceRef']['Entity'] = '_Measure Table'
    return $normalized
}

function Convert-JsonNode {
    param($Node)
    if ($null -eq $Node) { return $null }
    if ($Node -is [System.Management.Automation.PSCustomObject]) {
        $result = [ordered]@{}
        foreach ($property in ($Node.PSObject.Properties | Sort-Object Name)) { $result[$property.Name] = Convert-JsonNode $property.Value }
        return $result
    }
    if ($Node -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in ($Node.Keys | ForEach-Object {[string]$_} | Sort-Object)) { $result[$key] = Convert-JsonNode $Node[$key] }
        return $result
    }
    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        $result = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Node) { [void]$result.Add((Convert-JsonNode $item)) }
        return ,$result.ToArray()
    }
    $Node
}

function Compare-JsonNode {
    param($TemplateNode, $GeneratedNode, [string]$Path, [System.Collections.Generic.HashSet[string]]$MeasureNames)
    $differences = New-Object System.Collections.Generic.List[string]
    if ($null -eq $TemplateNode -or $null -eq $GeneratedNode) {
        if ($null -ne $TemplateNode -or $null -ne $GeneratedNode) { $differences.Add(("{0}: value differs (template={1}; generated={2})" -f $Path,$TemplateNode,$GeneratedNode)) }
        return $differences
    }

    # Normalize only the complete PBIR Measure object. Generic SourceRef.Entity
    # values are never normalized because they lack the exact measure identity.
    if ($Path -match '\.Measure$' -and $TemplateNode -is [System.Collections.IDictionary] -and $GeneratedNode -is [System.Collections.IDictionary]) {
        $TemplateNode = Normalize-KnownMeasureObject $TemplateNode $MeasureNames
        $GeneratedNode = Normalize-KnownMeasureObject $GeneratedNode $MeasureNames
    }

    if ($TemplateNode -is [string] -and $GeneratedNode -is [string]) {
        if ($TemplateNode -ne $GeneratedNode -and !(Test-AllowedMeasureTransformation $TemplateNode $GeneratedNode $MeasureNames)) { $differences.Add(("{0}: string differs (template='{1}'; generated='{2}')" -f $Path,$TemplateNode,$GeneratedNode)) }
        return $differences
    }
    $td = $TemplateNode -is [System.Collections.IDictionary]
    $gd = $GeneratedNode -is [System.Collections.IDictionary]
    if ($td -or $gd) {
        if (!($td -and $gd)) { $differences.Add("${Path}: node type differs"); return $differences }
        $tk = @($TemplateNode.Keys | ForEach-Object {[string]$_} | Sort-Object)
        $gk = @($GeneratedNode.Keys | ForEach-Object {[string]$_} | Sort-Object)
        foreach ($key in @($tk | Where-Object {$_ -notin $gk})) { $differences.Add(("{0}.{1}: missing in generated visual" -f $Path,$key)) }
        foreach ($key in @($gk | Where-Object {$_ -notin $tk})) { $differences.Add(("{0}.{1}: extra in generated visual" -f $Path,$key)) }
        foreach ($key in $tk) { if ($key -in $gk) { foreach ($d in (Compare-JsonNode $TemplateNode[$key] $GeneratedNode[$key] ("{0}.{1}" -f $Path,$key) $MeasureNames)) { $differences.Add($d) } } }
        return $differences
    }
    $ta = $TemplateNode -is [System.Array]
    $ga = $GeneratedNode -is [System.Array]
    if ($ta -or $ga) {
        if (!($ta -and $ga)) { $differences.Add("${Path}: node type differs"); return $differences }
        if ($TemplateNode.Count -ne $GeneratedNode.Count) { $differences.Add(("{0}: array length differs (template={1}; generated={2})" -f $Path,$TemplateNode.Count,$GeneratedNode.Count)); return $differences }
        for ($i=0; $i -lt $TemplateNode.Count; $i++) { foreach ($d in (Compare-JsonNode $TemplateNode[$i] $GeneratedNode[$i] ("{0}[{1}]" -f $Path,$i) $MeasureNames)) { $differences.Add($d) } }
        return $differences
    }
    if ($TemplateNode.GetType() -ne $GeneratedNode.GetType() -or $TemplateNode -ne $GeneratedNode) { $differences.Add(("{0}: value differs (template={1}; generated={2})" -f $Path,$TemplateNode,$GeneratedNode)) }
    $differences
}

function Read-CanonicalJson {
    param([Parameter(Mandatory=$true)][string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { throw "UTF-8 BOM: $Path" }
    try { Convert-JsonNode (([Text.Encoding]::UTF8.GetString($bytes)) | ConvertFrom-Json) }
    catch { throw ("Invalid JSON: {0}: {1}" -f $Path,$_.Exception.Message) }
}

if (!(Test-Path $BuildRoot -PathType Container)) { throw "Visual artifact gate failed: BuildRoot does not exist: $BuildRoot" }
if ([string]::IsNullOrWhiteSpace($TemplateReportRoot)) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $TemplateReportRoot = Join-Path $repoRoot 'pbip\Pipeline_SLA_Tracker.Report'
}
if (!(Test-Path $TemplateReportRoot -PathType Container)) { throw "Visual artifact gate failed: authoritative template report does not exist: $TemplateReportRoot" }
$generatedPages = Resolve-ReportPagesRoot (Resolve-Path $BuildRoot).Path
$templatePages = Join-Path (Resolve-Path $TemplateReportRoot).Path 'definition\pages'
if (!(Test-Path $templatePages -PathType Container)) { throw "Visual artifact gate failed: template definition/pages does not exist: $templatePages" }
$measureNames = Get-CanonicalMeasureNames $BuildRoot
$generated = Get-VisualMap $generatedPages
$template = Get-VisualMap $templatePages
$errors = New-Object System.Collections.Generic.List[string]
Write-Host 'VISUAL-PRESERVATION-GATE'
Write-Host ("Visual IDs: generated={0} template={1}" -f $generated.Count,$template.Count)
if ($generated.Count -ne 28 -or $template.Count -ne 28) { throw ("VISUAL-PRESERVATION-GATE failed: expected 28/28 visual files; generated={0}, template={1}." -f $generated.Count,$template.Count) }
foreach ($path in @($template.Keys | Sort-Object)) {
    if (!$generated.ContainsKey($path)) { $errors.Add("Missing generated visual: $path"); continue }
    try { $templateObject = Read-CanonicalJson $template[$path] } catch { $errors.Add($_.Exception.Message); continue }
    try { $generatedObject = Read-CanonicalJson $generated[$path] } catch { $errors.Add($_.Exception.Message); continue }
    foreach ($difference in (Compare-JsonNode $templateObject $generatedObject '$' $measureNames)) { $errors.Add("${path}: $difference") }
}
foreach ($path in @($generated.Keys | Sort-Object)) { if (!$template.ContainsKey($path)) { $errors.Add("Unexpected generated visual: $path") } }
$visualDirs = @(Get-ChildItem $generatedPages -Recurse -Directory | Where-Object {$_.Parent.Name -eq 'visuals'})
foreach ($dir in $visualDirs) { if (!(Test-Path (Join-Path $dir.FullName 'visual.json') -PathType Leaf)) { $errors.Add("Orphaned visual folder: $($dir.FullName)") } }
if ($errors.Count -gt 0) {
    Write-Host ("VISUAL-PRESERVATION-GATE|FAIL|Issues={0}" -f $errors.Count)
    $errors | ForEach-Object { Write-Host "VISUAL-PRESERVATION-GATE|ERROR|$_" }
    throw 'VISUAL-PRESERVATION-GATE failed.'
}
Write-Host 'VISUAL-PRESERVATION-GATE|PASS|Visuals=28/28|RecursiveJsonDiff=PASS|CanonicalMeasureMigration=PASS|UnexpectedVisualMutations=0|OrphanFolders=0'
