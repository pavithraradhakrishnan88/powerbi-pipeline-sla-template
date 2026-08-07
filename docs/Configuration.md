# Configuration Guide

This guide explains how developers and maintainers can customize the **Power BI Pipeline SLA Tracker Template**.

The configuration areas covered include data sources, semantic model settings, DAX measures, report theme, Power Query transformations, CI/CD automation, and local development setup.

---

# Data Configuration

The Pipeline SLA Tracker is designed to work with pipeline/process execution data stored in CSV format.

Default sample data location:

```
data/
├── Fact_Pipeline_SampleData.csv
└── Dim_Category.csv
```

To use your own data:

1. Replace the sample CSV files with your pipeline execution data.
2. Maintain the required column names and data types.
3. Run validation:

```powershell
.\build\validate.ps1
```

4. Rebuild the Power BI project:

```powershell
.\build\build.ps1
```

---

# CSV Schema Requirements

## Fact Pipeline Data

The main fact table contains pipeline execution details.

Expected schema:

| Column             | Description                 | Data Type    |
| ------------------ | --------------------------- | ------------ |
| PipelineID         | Unique pipeline identifier  | Text/Integer |
| PipelineName       | Pipeline or process name    | Text         |
| CategoryID         | Category reference          | Integer      |
| Environment        | Execution environment       | Text         |
| Status             | Run status                  | Text         |
| StartDate          | Actual execution start time | DateTime     |
| EndDate            | Actual execution end time   | DateTime     |
| ScheduledStart     | Planned execution start     | DateTime     |
| RuntimeMinutes     | Execution duration          | Decimal      |
| SLA_Target_Minutes | SLA threshold               | Decimal      |
| SLA_Breach_Flag    | SLA compliance indicator    | Integer      |

Additional columns can be added if required, but existing column names should remain unchanged to avoid breaking model relationships and measures.

---

## Dimension Data

The category dimension provides descriptive information.

Example:

```
data/Dim_Category.csv
```

Expected columns:

| Column       | Description          |
| ------------ | -------------------- |
| CategoryID   | Unique category key  |
| CategoryName | Category description |

---

# Semantic Model Configuration

The Power BI semantic model follows a star schema approach.

Recommended structure:

```
Fact_Pipeline_SampleData
        |
        |
Dim_Category
```

## Relationships

Configure relationships using:

| From                                 | To                       | Type        |
| ------------------------------------ | ------------------------ | ----------- |
| Fact_Pipeline_SampleData[CategoryID] | Dim_Category[CategoryID] | Many-to-One |

Recommended relationship settings:

* Cross-filter direction: Single
* Dimension tables should contain unique keys
* Fact tables should contain transactional records

---

## Model Files

Semantic model definitions are maintained in:

```
model/
```

Typical structure:

```
model/
├── tables/
├── relationships/
├── measures/
└── model.tmdl
```

When modifying the model:

1. Update TMDL files.
2. Validate syntax.
3. Rebuild the PBIP project.

---

# DAX Measure Configuration

DAX measures are maintained separately for easier management.

Location:

```
src/
└── measures.dax
```

Generated measures are maintained through:

```
scripts/
├── GenerateMeasures.csx
└── MeasureDefinitions.json
```

Example KPI categories:

```
measures/
├── SLA.tmdl
├── Duration.tmdl
└── KPIs.tmdl
```

Common measures include:

* Active Pipelines
* Average Runtime
* Successful Runs
* Failed Runs
* SLA Breach %
* Average Start Offset
* Timeline Coverage %

---

## Adding New Measures

To add a measure:

1. Add definition to:

```
scripts/MeasureDefinitions.json
```

2. Run the measure generator:

```powershell
.\scripts\tools\GenerateMetadata.ps1
```

3. Validate generated output.
4. Commit updated model files.

---

# Theme Configuration

The report theme is controlled using:

```
theme/
└── PipelineTheme.json
```

The theme controls:

* Report colors
* Visual styling
* KPI indicators
* SLA status colors

Current SLA color logic:

| Status     | Indicator |
| ---------- | --------- |
| SLA Met    | Green     |
| SLA Breach | Red       |

To customize branding:

1. Update `PipelineTheme.json`.
2. Import the theme in Power BI Desktop.
3. Validate all report pages.

---

# Power Query Configuration

Power Query transformations are stored in:

```
powerquery/
```

Power Query is responsible for:

* Loading CSV files
* Cleaning source data
* Applying transformations
* Preparing tables for the semantic model

When changing data sources:

1. Update connection paths.
2. Validate column mappings.
3. Refresh the dataset.
4. Confirm model relationships.

Recommended practices:

* Avoid hardcoded file paths.
* Keep transformation steps documented.
* Validate data types after changes.

---

# CI/CD Configuration

GitHub Actions workflows automate validation, building, and releases.

Location:

```
.github/workflows/
```

Current workflows:

```
.github/workflows/
├── validate.yml
├── build.yml
└── release.yml
```

---

## Validation Workflow

Purpose:

* Validate CSV files
* Check schema consistency
* Detect configuration issues

Runs on:

* Pull requests
* Code changes

---

## Build Workflow

Purpose:

* Validate project files
* Generate artifacts
* Build Power BI package

Triggered by changes in:

```
data/
model/
powerquery/
theme/
src/
```

Build script:

```powershell
.\build\build.ps1
```

---

## Release Workflow

Purpose:

* Package production release artifacts
* Create release packages

Triggered by:

* GitHub releases
* Manual execution

---

# Environment Setup

## Required Software

Install:

* Power BI Desktop
* Git
* PowerShell
* Tabular Editor 2
* .NET SDK

---

## Clone Repository

```bash
git clone https://github.com/pavithraradhakrishnan88/powerbi-pipeline-sla-template.git
```

Navigate:

```bash
cd powerbi-pipeline-sla-template
```

---

## Open Project

Open:

```
reports/
└── PipelineDashboard.pbip
```

---

## Build Locally

Run:

```powershell
.\build.ps1
```

or:

```powershell
.\build\build.ps1
```

The build process will:

1. Validate source files.
2. Generate model artifacts.
3. Apply configuration updates.
4. Create build outputs.

---

# Maintenance Guidelines

For safe customization:

* Keep sample data structure unchanged.
* Add new columns carefully.
* Document schema changes.
* Test DAX changes before deployment.
* Validate builds before merging pull requests.
* Keep documentation synchronized with configuration changes.

This ensures the Pipeline SLA Tracker remains reusable as a production-ready Power BI template.