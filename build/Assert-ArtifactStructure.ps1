[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('report','semantic-model','pbip')][string]$ArtifactType,
    [Parameter(Mandatory=$true)][string]$ArtifactRoot
)
$ErrorActionPreference = 'Stop'
$pbipName = 'Pipeline_SLA_Tracker'
$root = [IO.Path]::GetFullPath($ArtifactRoot)
switch ($ArtifactType) {
    'report' {
        $report = Join-Path $root "$pbipName.Report"
        $definition = Join-Path $report 'definition.pbir'
        $platform = Join-Path $report '.platform'
        if (!(Test-Path $report -PathType Container)) { throw 'STRUCTURE-REPORT-GATE failed: report artifact is missing.' }
        if (!(Test-Path $definition -PathType Leaf)) { throw 'STRUCTURE-REPORT-GATE failed: definition.pbir is missing.' }
        if (!(Test-Path $platform -PathType Leaf)) { throw 'STRUCTURE-REPORT-GATE failed: report .platform is missing.' }
        $pbir = Get-Content -Raw $definition | ConvertFrom-Json
        if ($pbir.'$schema' -ne 'https://developer.microsoft.com/json-schemas/fabric/item/report/definitionProperties/2.0.0/schema.json') { throw 'STRUCTURE-REPORT-GATE failed: invalid definitionProperties schema.' }
        if ([string]$pbir.version -ne '4.0') { throw 'STRUCTURE-REPORT-GATE failed: expected PBIR version 4.0.' }
        $visuals = @(Get-ChildItem (Join-Path $report 'definition') -Recurse -Filter 'visual.json' -File)
        if ($visuals.Count -ne 28) { throw "STRUCTURE-REPORT-GATE failed: expected 28 visual.json files, found $($visuals.Count)." }
        foreach ($v in $visuals) {
            $bytes = [IO.File]::ReadAllBytes($v.FullName)
            if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { throw "STRUCTURE-REPORT-GATE failed: UTF-8 BOM remains in $($v.FullName)." }
            Get-Content -Raw $v.FullName | ConvertFrom-Json | Out-Null
        }
        Write-Host 'STRUCTURE-REPORT-GATE|PASS|definition.pbir=valid|platform=present|visuals=28/28|json=valid'
    }
    'semantic-model' {
        $model = Join-Path $root "$pbipName.SemanticModel"
        $definition = Join-Path $model 'definition'
        $platform = Join-Path $model '.platform'
        $fact = Join-Path $definition 'tables\Fact_Pipeline_SampleData.tmdl'
        $relationships = Join-Path $definition 'relationships.tmdl'
        if (!(Test-Path $model -PathType Container)) { throw 'STRUCTURE-SEMANTIC-GATE failed: semantic-model artifact is missing.' }
        if (!(Test-Path $platform -PathType Leaf)) { throw 'STRUCTURE-SEMANTIC-GATE failed: semantic-model .platform is missing.' }
        if (!(Test-Path $fact -PathType Leaf)) { throw 'STRUCTURE-SEMANTIC-GATE failed: Fact_Pipeline_SampleData.tmdl is missing.' }
        if (!(Test-Path $relationships -PathType Leaf)) { throw 'STRUCTURE-SEMANTIC-GATE failed: relationships.tmdl is missing.' }
        if (Test-Path (Join-Path $definition 'expressions.tmdl')) { throw 'STRUCTURE-SEMANTIC-GATE failed: expressions.tmdl must not be packaged.' }
        if (Test-Path (Join-Path $definition 'tables\_Measures.tmdl')) { throw 'STRUCTURE-SEMANTIC-GATE failed: _Measures.tmdl must not be packaged.' }
        $factText = Get-Content -Raw $fact
        $relText = Get-Content -Raw $relationships
        if ($factText -match '(?i)[A-Z]:\\[^\r\n"]*\\_work\\|/home/runner/|/opt/hostedtoolcache/') { throw 'STRUCTURE-SEMANTIC-GATE failed: runner-specific path remains in Fact TMDL.' }
        if ($relText -notmatch '(?s)relationship\s+[^\r\n]+\r?\n\s*fromColumn:\s*Fact_Pipeline_SampleData\.Category\r?\n\s*toColumn:\s*Dim_Category\.CategoryName') { throw 'STRUCTURE-SEMANTIC-GATE failed: Dim_Category -> Fact_Pipeline_SampleData relationship is missing.' }
        $tmdls = @(Get-ChildItem $definition -Recurse -Filter '*.tmdl' -File)
        if ($tmdls.Count -lt 5) { throw "STRUCTURE-SEMANTIC-GATE failed: expected semantic-model TMDL set, found only $($tmdls.Count) files." }
        Write-Host "STRUCTURE-SEMANTIC-GATE|PASS|platform=present|FactTmdl=present|Relationships=present|TmdlFiles=$($tmdls.Count)|runnerPaths=0"
    }
    'pbip' {
        $pbip = Join-Path $root "$pbipName.pbip"
        if (!(Test-Path $pbip -PathType Leaf)) { throw 'STRUCTURE-PBIP-GATE failed: .pbip project file is missing.' }
        $project = Get-Content -Raw $pbip | ConvertFrom-Json
        if ($null -eq $project) { throw 'STRUCTURE-PBIP-GATE failed: .pbip is not valid JSON.' }
        $text = Get-Content -Raw $pbip
        if ($text -notmatch [regex]::Escape("$pbipName.Report")) { throw 'STRUCTURE-PBIP-GATE failed: report artifact reference is missing.' }
        if ($text -notmatch [regex]::Escape("$pbipName.SemanticModel")) { throw 'STRUCTURE-PBIP-GATE failed: semantic-model artifact reference is missing.' }
        Write-Host 'STRUCTURE-PBIP-GATE|PASS|pbip=json-valid|reportRef=present|semanticModelRef=present'
    }
}
