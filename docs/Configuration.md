# Configuration Guide — Version 1.0.0

This guide describes the current Power BI Pipeline SLA Tracker configuration.

## Data

Default sample files:

```text
data/Fact_Pipeline_SampleData.csv
data/Dim_Category.csv
```

The semantic model uses `Fact_Pipeline_SampleData` as the fact table and `Dim_Category` as the dimension.

Run validation after data changes:

```powershell
.\build\validate.ps1
```

## Semantic model

The authoritative PBIP semantic model is under:

```text
pbip/Pipeline_SLA_Tracker.SemanticModel/
```

The main relationship is:

```text
Dim_Category[CategoryName]  (1) -> (*)  Fact_Pipeline_SampleData[Category]
```

Keep the relationship active and single-directional unless the model contract is intentionally changed.

## Measures

Measure definitions are maintained in:

```text
scripts/metadata/MeasureDefinitions.json
```

Generate the Tabular Editor 2.28 script with:

```powershell
.\scripts\tools\GenerateMetadata.ps1
```

The generated script is:

```text
scripts/GenerateMeasures.csx
```

For manual model work, run that script in Tabular Editor 2.28. For CI/local automated builds, `build/build.ps1` materializes the authoritative measure set itself and does not require Tabular Editor on the runner.

Do not treat `scripts/GenerateMeasures.csx` or generated PBIP output as the metadata source of truth; edit `MeasureDefinitions.json` first.

## Report

The Version 1 report is:

```text
pbip/Pipeline_SLA_Tracker.Report/
```

Current pages:

- Home
- Executive Overview
- SLA Exceptions

The report template remains authoritative for layout, visual metadata, and registered resources.

## DataFolder

The semantic model uses the `DataFolder` expression for the sample-data location. The build must leave this portable and resolve it relative to the PBIP artifact context.

Do not introduce a runner-specific absolute path such as `C:\...` into the generated artifact.

## Theme and resources

Theme and registered resources are maintained with the report template. Version 1 requires the final artifact to carry its registered report resources through publication.

## CI/CD

Workflows:

```text
.github/workflows/validate.yml
.github/workflows/build.yml
.github/workflows/release.yml
```

The build workflow validates the exact artifact that is packaged. It includes PBIR validation and the 28/28 visual JSON gate.

## Local build

```powershell
.\build\validate.ps1
.\build\build.ps1
```

Outputs are written to `BuildResult/` and `artifacts/` as defined by the build script.
