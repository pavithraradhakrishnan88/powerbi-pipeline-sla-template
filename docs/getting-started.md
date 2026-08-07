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

Clone the repository:

```bash
git clone https://github.com/pavithraradhakrishnan88/powerbi-pipeline-sla-template.git
```

Navigate into the project:

```powershell
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

Open the PBIP project:

```
pbip/
└── Pipeline SLA.pbip
```

Open it using Power BI Desktop.

---

## Load Sample Data

The repository includes sample pipeline data.

Default files:

```
data/
├── Fact_Pipeline_SampleData.csv
└── Dim_Category.csv
```

To use your own data:

1. Replace the sample CSV files.
2. Maintain the required column structure.
3. Refresh the Power BI model.

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
2. Select **Publish**.
3. Choose the target Power BI workspace.
4. Verify dataset and report deployment.

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