# Getting Started — Version 1.0.0

This guide covers the supported Version 1 PBIP workflow for the Power BI Pipeline SLA Tracker.

## Prerequisites

- Power BI Desktop
- Git
- PowerShell 7+
- .NET SDK
- Tabular Editor 2.28 for the manual measure-generation/inspection workflow

## Clone and open

```bash
git clone https://github.com/pavithraradhakrishnan88/powerbi-pipeline-sla-template.git
cd powerbi-pipeline-sla-template
```

Open this PBIP in Power BI Desktop:

```text
pbip/Pipeline_SLA_Tracker.pbip
```

Do not use the older `reports/PipelineDashboard.pbip` path; it is not the Version 1 project.

## Generate measures metadata

The source of truth is:

```text
scripts/metadata/MeasureDefinitions.json
```

Generate the Tabular Editor script:

```powershell
.\scripts\tools\GenerateMetadata.ps1
```

This writes `scripts/GenerateMeasures.csx`, which is compatible with Tabular Editor 2.28.

## Manual Tabular Editor workflow

1. Open the Version 1 semantic model in Tabular Editor 2.28.
2. Run `scripts/GenerateMeasures.csx`.
3. Confirm the measures on `Fact_Pipeline_SampleData`.
4. Save and inspect the model in Power BI Desktop.

## Automated build

Run:

```powershell
.\build\validate.ps1
.\build\build.ps1
```

The automated build is template-first and does not require Tabular Editor on the CI runner. It materializes the authoritative measures from the repository contract, validates the semantic model, validates PBIR metadata, checks the `DataFolder` path, and applies the exact published-artifact visual gate.

## Version 1 acceptance checks

Before publishing:

- Build succeeds.
- Expected measures are present inline on `Fact_Pipeline_SampleData`.
- No generated `_Measures.tmdl` is required by the build.
- `DataFolder` remains portable and does not contain a CI-runner absolute path.
- PBIR definition is valid.
- Exactly 28 `visual.json` files exist and all parse as JSON without BOMs.
- Power BI Desktop opens the PBIP.
- Slicers visibly filter visuals.
- All pages/visuals render.
- Registered images/resources display.

## Publish

After those checks, open the PBIP in Power BI Desktop and publish it to the intended Power BI workspace.

Continue with [Testing](Testing.md), [Configuration](Configuration.md), and the root [Build Guide](../Pipeline_SLA_Tracker_Build_Guide.md).
