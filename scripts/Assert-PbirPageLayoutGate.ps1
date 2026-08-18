[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReportRootPath,
    [switch]$ThrowOnError
)

$ErrorActionPreference = 'Stop'

function Fail-Gate([string]$Message) {
    Write-Host "PBIR-PAGE-LAYOUT-GATE|FAIL"
    Write-Host "Reason=$Message"
    if ($ThrowOnError) { throw $Message }
    exit 1
}

function Read-Json([string]$Path) {
    try { return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json) }
    catch { Fail-Gate "Invalid JSON: $Path :: $($_.Exception.Message)" }
}

$definition = Join-Path $ReportRootPath 'definition'
$pagesRoot = Join-Path $definition 'pages'
$pagesFile = Join-Path $pagesRoot 'pages.json'

if (-not (Test-Path -LiteralPath $pagesFile -PathType Leaf)) {
    Fail-Gate "Missing pages.json: $pagesFile"
}

$pages = Read-Json $pagesFile
$pageDirs = @(Get-ChildItem -LiteralPath $pagesRoot -Directory -ErrorAction SilentlyContinue)
$pageOrder = @($pages.pageOrder)

$pagesCount = $pageDirs.Count
if ($pagesCount -ne $pageOrder.Count) {
    Fail-Gate "Page count/order mismatch: directories=$pagesCount pageOrder=$($pageOrder.Count)"
}

$pageOrderResolved = 0
$pageDefinitionsResolved = 0
$visualFoldersResolved = 0
$visualDefinitionsResolved = 0
$visualNamesResolved = 0
$visualPositionsResolved = 0
$duplicateVisualIdentities = 0
$parentGroupReferencesResolved = 0
$seenVisualNames = @{}

foreach ($pageId in $pageOrder) {
    $pageDir = Join-Path $pagesRoot ([string]$pageId)
    if (-not (Test-Path -LiteralPath $pageDir -PathType Container)) {
        Fail-Gate "PageOrder entry has no matching directory: $pageId"
    }
    $pageOrderResolved++

    $pageFile = Join-Path $pageDir 'page.json'
    if (-not (Test-Path -LiteralPath $pageFile -PathType Leaf)) {
        Fail-Gate "Missing page.json: $pageFile"
    }
    $page = Read-Json $pageFile
    if ([string]$page.name -ne [string]$pageId) {
        Fail-Gate "page.json name mismatch: page=$pageId name=$($page.name)"
    }
    $pageDefinitionsResolved++

    $visualsRoot = Join-Path $pageDir 'visuals'
    $visualDirs = @(Get-ChildItem -LiteralPath $visualsRoot -Directory -ErrorAction SilentlyContinue)
    foreach ($visualDir in $visualDirs) {
        $visualFoldersResolved++
        $visualFile = Join-Path $visualDir.FullName 'visual.json'
        if (-not (Test-Path -LiteralPath $visualFile -PathType Leaf)) {
            Fail-Gate "Visual folder has no visual.json: $($visualDir.FullName)"
        }
        $visual = Read-Json $visualFile
        $visualDefinitionsResolved++

        if ([string]$visual.name -ne [string]$visualDir.Name) {
            Fail-Gate "Visual name mismatch: folder=$($visualDir.Name) name=$($visual.name)"
        }
        $visualNamesResolved++

        if ($seenVisualNames.ContainsKey([string]$visual.name)) {
            $duplicateVisualIdentities++
        } else {
            $seenVisualNames[[string]$visual.name] = $true
        }

        $position = $visual.position
        $requiredPositionFields = @('x','y','z','height','width','tabOrder')
        foreach ($field in $requiredPositionFields) {
            if ($null -eq $position.$field) {
                Fail-Gate "Visual position field missing: page=$pageId visual=$($visual.name) field=$field"
            }
        }
        $visualPositionsResolved++

        if ($visual.parentGroupName) {
            $parentGroupReferencesResolved++
        }

        if ([string]$visual.name -notmatch '^[0-9a-f]{20}$') {
            Write-Host 'PBIR-PAGE-LAYOUT-WARN'
            Write-Host "Page=$pageId"
            Write-Host "Visual=$($visual.name)"
            Write-Host 'Issue=NonCanonicalVisualId'
            Write-Host 'ExpectedPattern=^[0-9a-f]{20}$'
        }
    }
}

if ($duplicateVisualIdentities -ne 0) {
    Fail-Gate "DuplicateVisualIdentities=$duplicateVisualIdentities"
}

$expectedPages = 3
$expectedVisuals = 28
$contractPass = ($pagesCount -eq $expectedPages -and
    $pageOrderResolved -eq $expectedPages -and
    $pageDefinitionsResolved -eq $expectedPages -and
    $visualFoldersResolved -eq $expectedVisuals -and
    $visualDefinitionsResolved -eq $expectedVisuals -and
    $visualNamesResolved -eq $expectedVisuals -and
    $visualPositionsResolved -eq $expectedVisuals -and
    $duplicateVisualIdentities -eq 0)

if (-not $contractPass) {
    Fail-Gate "DesktopLayoutMaterializationContract=FAIL"
}

Write-Host 'PBIR-PAGE-LAYOUT-GATE|PASS'
Write-Host "Pages=$pagesCount"
Write-Host "PageOrderResolved=$pageOrderResolved/$expectedPages"
Write-Host "PageDefinitionsResolved=$pageDefinitionsResolved/$expectedPages"
Write-Host "VisualFoldersResolved=$visualFoldersResolved/$expectedVisuals"
Write-Host "VisualDefinitionsResolved=$visualDefinitionsResolved/$expectedVisuals"
Write-Host "VisualNamesResolved=$visualNamesResolved/$expectedVisuals"
Write-Host "VisualPositionsResolved=$visualPositionsResolved/$expectedVisuals"
Write-Host "DuplicateVisualIdentities=$duplicateVisualIdentities"
Write-Host "ParentGroupReferencesResolved=$parentGroupReferencesResolved"
Write-Host 'DesktopLayoutMaterializationContract=PASS'
