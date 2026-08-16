# Getting Started

This guide explains how to set up, build, validate, and publish the **Power BI Pipeline SLA Tracker Template**.

---

## Prerequisites

Install the following tools before starting:

* Microsoft Power BI Desktop
* Git
* PowerShell 7+
* Tabular Editor 2

Recommended:

* .NET SDK (required for automation tooling)
* Power BI Service workspace access

---

## Clone Repository

Clone this repo and navigate to it:

```bash
git clone https://github.com/pavithraradhakrishnan88/powerbi-pipeline-sla-template.git
cd powerbi-pipeline-sla-template
```

---

## Repository Structure

The main folders are:

```
powerbi-pipeline-sla-template
│
├── data/              # Sample pipeline datasets
├── model/             # Semantic model files
├── pbip/              # Power BI Project files
├── powerquery/        # Power Query scripts
├── src/               # DAX and model definitions
├── theme/             # Power BI theme files
├── build/             # Validation and build automation
├── docs/              # Documentation
└── .github/workflows/ # CI/CD workflows
```

---

## Open Power BI Project

Open the PBIP project from the `pbip/` folder using Power BI Desktop.

### Configure the local sample-data path

The semantic model exposes a required `DataFolder` Power Query parameter. The checked-in PBIP intentionally keeps its default value as the portable placeholder `data`; it must **not** be replaced by a CI-runner path.

Power Query's `File.Contents()` consumes a filesystem path and does not automatically interpret the PBIP `data/` folder as a project-relative root. Therefore, for a local Desktop refresh, set `DataFolder` to the absolute path of this repository's `data` directory:

1. In Power BI Desktop select **Transform data → Manage Parameters**.
2. Select `DataFolder`.
3. Set it to the local repository path ending in `\data`, for example:
   `C:\work\powerbi-pipeline-sla-template\data`
4. Apply/refresh the model.

Do **not** copy a GitHub Actions path such as `D:\a\...` into the PBIP. CI validates the bundled artifact structurally, while the local absolute path is intentionally an environment-specific Desktop setting.

---

## Load Sample Data

The repository includes sample pipeline data:

```
data/
├── Fact_Pipeline_SampleData.csv
└── Dim_Category.csv
```

The Power Query definitions consume these files through the `DataFolder` parameter.

To use your own data:

1. Replace the sample CSV files.
2. Maintain the required column structure.
3. Set `DataFolder` to the local directory containing those files.
4. Refresh the Power BI model.

---

## Build

Run the automated build process:

```powershell
.\build\build.ps1
```

The build process performs:

* Validation checks
* Model updates
* Power BI artifact generation
* Build output creation
* PBIR/visual integrity validation

---

## Validate

Run validation manually:

```powershell
.\build\validate.ps1
```

Validation checks:

* Required files
* CSV schema
* Data availability
* Project structure

---

## GitHub Actions Validation

The repository includes automated workflows:

```
.github/workflows/

├── validate.yml
├── build.yml
└── release.yml
```

These workflows validate changes automatically during commits and releases.

---

## Publish to Power BI Service

After successful validation:

1. Open the PBIP project in Power BI Desktop.
2. Confirm `DataFolder` points to the intended local data directory.
3. Select **Publish**.
4. Choose the target Power BI workspace.
5. Verify dataset and report deployment.

---

## Configure Scheduled Refresh

After publishing:

1. Open the dataset settings in Power BI Service.
2. Configure data source credentials.
3. Configure refresh schedule.
4. Confirm successful refresh.

---

## Next Steps

Continue with:

* [User Guide](UserGuide.md)
* [Configuration Guide](Configuration.md)
* [Testing Guide](Testing.md)
* [Changelog](../CHANGELOG.md)
