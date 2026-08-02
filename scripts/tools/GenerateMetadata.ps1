# ==========================================================
# GenerateMetadata.ps1
# Pipeline SLA Tracker
#
# Generates:
#   scripts/GenerateMeasures.csx
#
# Source:
#   scripts/metadata/MeasureDefinitions.json
#
# Compatible:
#   Tabular Editor 2.28
# ==========================================================

param(

    [string]
    $JsonFile = (Join-Path $PSScriptRoot "..\metadata\MeasureDefinitions.json"),

    [string]
    $OutputFile = (Join-Path $PSScriptRoot "..\GenerateMeasures.csx")

)


Write-Host "======================================"
Write-Host " Generating GenerateMeasures.csx"
Write-Host "======================================"


if (!(Test-Path $JsonFile)) {

    Write-Error "JSON metadata not found:"
    Write-Error $JsonFile
    exit 1
}
# ----------------------------------------------------------
# Helper: Escape text for C# string literals
# ----------------------------------------------------------

function ConvertTo-CSharpString {
    param(
        [string]$Text,
        [switch]$Verbatim
    )

    if ([string]::IsNullOrEmpty($Text)) {
        return ""
    }

    if ($Verbatim.IsPresent) {
        # C# verbatim string: only double quotes need escaping by doubling.
        return $Text.Replace('"', '""')
    }

    $backslash = [char]92
    $quote = [char]34

    $escaped = $Text.Replace($backslash.ToString(), "$backslash$backslash")
    $escaped = $escaped.Replace($quote.ToString(), "$backslash$quote")
    $escaped = $escaped.Replace([Environment]::NewLine, '\\n')
    $escaped = $escaped.Replace("`n", '\\n')
    $escaped = $escaped.Replace("`r", '\\r')

    return $escaped
}
function Get-String {
    param([psobject]$Value)

    if ($null -eq $Value) {
        return ""
    }

    return ([string]$Value).Trim()
}

function Get-Bool {
    param(
        [psobject]$Value,
        [bool]$Default = $false
    )

    if ($null -eq $Value) {
        return $Default
    }

    if ($Value -is [string]) {
        switch -Wildcard ($Value.Trim().ToLowerInvariant()) {
            "true" { return $true }
            "false" { return $false }
            "1" { return $true }
            "0" { return $false }
            default { return $Default }
        }
    }

    return [bool]$Value
}

function Get-Array {
    param([psobject]$Value)

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [System.Array]) {
        return $Value
    }

    return @($Value)
}

function Get-AdditionalAnnotations {
    param(
        [psobject]$Measure,
        [string]$MeasureId,
        [string[]]$ExcludedProperties
    )

    $annotations = [System.Collections.Generic.List[string]]::new()

    foreach ($prop in $Measure.PSObject.Properties) {
        if ($ExcludedProperties -contains $prop.Name) {
            continue
        }

        if ($null -eq $prop.Value) {
            continue
        }

        if ($prop.Value -is [System.Array]) {
            $valueText = ($prop.Value | ForEach-Object { ([string]$_).Trim() }) -join ';'
        }
        else {
            $valueText = [string]$prop.Value
        }

        $valueText = Get-String $valueText
        if ([string]::IsNullOrWhiteSpace($valueText)) {
            continue
        }

        $escapedKey = ConvertTo-CSharpString $prop.Name
        $escapedValue = ConvertTo-CSharpString $valueText
        $annotations.Add('    measure_' + $MeasureId + '.SetAnnotation("' + $escapedKey + '", "' + $escapedValue + '");')
    }

    return $annotations -join "`n"
}

function Get-ModelColumns {
    param([string]$TableName)

    $cols = @()
    $colFolder = Join-Path -Path (Join-Path $PSScriptRoot "..\..\model\tables") -ChildPath $TableName
    $colFolder = Join-Path $colFolder "columns"
    if (Test-Path $colFolder) {
        Get-ChildItem -Path $colFolder -Filter *.json -File -ErrorAction SilentlyContinue | ForEach-Object {
            $cols += ([IO.Path]::GetFileNameWithoutExtension($_.Name))
        }
    }

    return $cols
}

# ----------------------------------------------------------
# Load Metadata
# ----------------------------------------------------------
try {
    $metadata = Get-Content $JsonFile -Raw -ErrorAction Stop |
                 ConvertFrom-Json -ErrorAction Stop
}
catch {
    Write-Error "Failed to parse JSON metadata:"
    Write-Error $_.Exception.Message
    exit 1
}


$measures = $metadata.measures

Write-Host "Measures loaded:"
Write-Host $measures.Count

# ----------------------------------------------------------
# Validate Metadata
# ----------------------------------------------------------

if ($null -eq $measures -or $measures.Count -eq 0) {
    Write-Warning "No measures were found in metadata."
}

# ----------------------------------------------------------
# Generate Header
# ----------------------------------------------------------

$generatedOn = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
$generatorVersion = '1.0.0'

$script = @"
// ==========================================================
// GenerateMeasures.csx
// AUTO GENERATED
//
// Generated: $generatedOn
// Generator: GenerateMetadata.ps1 v$generatorVersion
// Source: $JsonFile
// Tabular Editor 2.28
// ==========================================================

using System.Linq;

int created = 0;
int updated = 0;
int skipped = 0;


"@

# ----------------------------------------------------------
# Generate Measures
# ----------------------------------------------------------

foreach ($m in $measures) {
    $id = Get-String $m.MeasureID

    $name = ConvertTo-CSharpString (Get-String $m.Name)
    $table = ConvertTo-CSharpString (Get-String $m.Table)
    $folder = ConvertTo-CSharpString (Get-String $m.Folder)
    $expressionRaw = Get-String $m.Expression

    # Validate referenced table[column] usages exist in the model
    $missingColumn = $false
    $matches = [regex]::Matches($expressionRaw, '([A-Za-z0-9_]+)\[([^\]]+)\]')
    foreach ($mt in $matches) {
        $refTable = $mt.Groups[1].Value
        $refCol = $mt.Groups[2].Value
        $modelCols = Get-ModelColumns $refTable
        if ($modelCols.Count -eq 0) {
            Write-Warning "Referenced table '$refTable' not found in model for measure $($m.MeasureID)."
            $missingColumn = $true
            break
        }
        if (-not ($modelCols -contains $refCol)) {
            Write-Warning "Referenced column '$refCol' not found on table '$refTable' for measure $($m.MeasureID)."
            $missingColumn = $true
            break
        }
    }
    if ($missingColumn) {
        Write-Warning "Skipping $($m.MeasureID) due to missing model columns referenced by its expression."
        $skipped++
        continue
    }

    $expression = ConvertTo-CSharpString -Text $expressionRaw -Verbatim
    $format = ConvertTo-CSharpString (Get-String $m.Format)
    $description = ConvertTo-CSharpString (Get-String $m.Description)
    $owner = ConvertTo-CSharpString (Get-String $m.Owner)
    $category = ConvertTo-CSharpString (Get-String $m.Category)
    $version = ConvertTo-CSharpString (Get-String $m.Version)
    $displayOrder = ConvertTo-CSharpString (Get-String $m.DisplayOrder)
    $template = ConvertTo-CSharpString (Get-String $m.Template)
    $status = ConvertTo-CSharpString (Get-String $m.Status)
    $tags = ConvertTo-CSharpString ((Get-Array $m.Tags) -join ";")
    $synonyms = ConvertTo-CSharpString ((Get-Array $m.Synonyms) -join ";")
    $hidden = if (Get-Bool $m.Hidden) { "true" } else { "false" }

    if ([string]::IsNullOrWhiteSpace($id)) {
        Write-Warning "Skipping measure with missing MeasureID."
        $skipped++
        continue
    }

    if ([string]::IsNullOrWhiteSpace($table)) {
        Write-Warning "Skipping $id because Table is empty."
        $skipped++
        continue
    }

    if ([string]::IsNullOrWhiteSpace($name)) {
        Write-Warning "Skipping $id because Name is empty."
        $skipped++
        continue
    }

    if ([string]::IsNullOrWhiteSpace($expression)) {
        Write-Warning "Skipping $id because Expression is empty."
        $skipped++
        continue
    }

    $measureBlock = @'

// ----------------------------------------------------------
// {{Name}}
// ID: {{ID}}
// Category: {{Category}}
// ----------------------------------------------------------

var table_{{ID}} = Model.Tables["{{Table}}"];

if(table_{{ID}} != null)
{
    var measure_{{ID}} = table_{{ID}}.Measures.FirstOrDefault(m => m.Name == "{{Name}}");

    if(measure_{{ID}} == null)
    {
        measure_{{ID}} = table_{{ID}}.AddMeasure(
            "{{Name}}",
            @"{{Expression}}"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_{{ID}}.Expression = @"{{Expression}}";
    measure_{{ID}}.DisplayFolder = "{{Folder}}";
    measure_{{ID}}.Description = "{{Description}}";
    measure_{{ID}}.FormatString = "{{Format}}";
    measure_{{ID}}.IsHidden = {{Hidden}};

    measure_{{ID}}.SetAnnotation("Owner", "{{Owner}}");
    measure_{{ID}}.SetAnnotation("Category", "{{Category}}");
    measure_{{ID}}.SetAnnotation("Version", "{{Version}}");
    measure_{{ID}}.SetAnnotation("DisplayOrder", "{{DisplayOrder}}");
    measure_{{ID}}.SetAnnotation("Template", "{{Template}}");
    measure_{{ID}}.SetAnnotation("Status", "{{Status}}");
    measure_{{ID}}.SetAnnotation("Tags", "{{Tags}}");
    measure_{{ID}}.SetAnnotation("Synonyms", "{{Synonyms}}");
{{AdditionalAnnotations}}
}
else
{
    Console.WriteLine("Table '{{Table}}' not found.");
}
'@

    $measureBlock = $measureBlock.Replace('{{ID}}', $id)
    $measureBlock = $measureBlock.Replace('{{Name}}', $name)
    $measureBlock = $measureBlock.Replace('{{Table}}', $table)
    $measureBlock = $measureBlock.Replace('{{Expression}}', $expression)
    $measureBlock = $measureBlock.Replace('{{Folder}}', $folder)
    $measureBlock = $measureBlock.Replace('{{Description}}', $description)
    $measureBlock = $measureBlock.Replace('{{Format}}', $format)
    $measureBlock = $measureBlock.Replace('{{Hidden}}', $hidden)
    $measureBlock = $measureBlock.Replace('{{Owner}}', $owner)
    $measureBlock = $measureBlock.Replace('{{Category}}', $category)
    $measureBlock = $measureBlock.Replace('{{Version}}', $version)
    $measureBlock = $measureBlock.Replace('{{DisplayOrder}}', $displayOrder)
    $measureBlock = $measureBlock.Replace('{{Template}}', $template)
    $additionalAnnotations = Get-AdditionalAnnotations $m $id @(
        'MeasureID',
        'Name',
        'Table',
        'Expression',
        'Folder',
        'Format',
        'Description',
        'Owner',
        'Category',
        'Version',
        'DisplayOrder',
        'Template',
        'Status',
        'Tags',
        'Synonyms',
        'Hidden'
    )

    $measureBlock = $measureBlock.Replace('{{Status}}', $status)
    $measureBlock = $measureBlock.Replace('{{Tags}}', $tags)
    $measureBlock = $measureBlock.Replace('{{Synonyms}}', $synonyms)
    $measureBlock = $measureBlock.Replace('{{AdditionalAnnotations}}', $additionalAnnotations)

    $script += $measureBlock
}

# ----------------------------------------------------------
# Generate Footer
# ----------------------------------------------------------

$script += @"

// ==========================================================
// Completed
// ==========================================================

Console.WriteLine("Measures created: " + created);
Console.WriteLine("Measures updated: " + updated);
Console.WriteLine("Measures skipped: " + skipped);

"@

# ----------------------------------------------------------
# Backup existing output
# ----------------------------------------------------------

$backupFile = $null
if (Test-Path $OutputFile) {
    $outputName = Split-Path $OutputFile -Leaf
    $backupFile = Join-Path (Split-Path $OutputFile -Parent) ("{0}.{1}.bak" -f $outputName, (Get-Date -Format 'yyyyMMddHHmmss'))
    Copy-Item -Path $OutputFile -Destination $backupFile -Force
    Write-Host "Backup created:" $backupFile
}

# ----------------------------------------------------------
# Write GenerateMeasures.csx
# ----------------------------------------------------------

$script | Set-Content -Path $OutputFile -Encoding UTF8

Write-Host ""
Write-Host "======================================"
Write-Host "GenerateMeasures.csx created successfully"
Write-Host "Output:"
Write-Host $OutputFile
Write-Host "Source metadata:"
Write-Host $JsonFile
Write-Host "Measures loaded:"
Write-Host $measures.Count
if ($backupFile) {
    Write-Host "Backup file:"
    Write-Host $backupFile
}
Write-Host "======================================"
