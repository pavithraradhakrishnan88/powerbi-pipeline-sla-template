# Testing Guide

This document explains the validation process, automated checks, and testing workflow for the **Power BI Pipeline SLA Tracker Template**.

The testing approach ensures that:

* Source data files follow the required schema.
* Power BI model files remain valid.
* DAX measures and metadata changes do not introduce errors.
* Build artifacts are generated successfully.
* GitHub Actions workflows validate changes automatically.

---

# Local Validation

Local validation can be performed before committing changes to the repository.

## Prerequisites

Ensure the following tools are installed:

* Power BI Desktop
* Git
* PowerShell
* Tabular Editor 2
* .NET SDK (required for automation scripts)

---

## Validate Data Files

Run the validation script:

```powershell
.\build\validate.ps1
```

The validation checks:

* CSV files exist.
* Required columns are available.
* Data types are valid.
* Empty or invalid files are detected.
* Schema changes are identified.

Expected output:

```
Validation completed successfully.
No errors found.
```

---

## Validate Repository Structure

Confirm the expected folders exist:

```
data/
model/
powerquery/
theme/
src/
scripts/
build/
docs/
```

Missing required folders or files may cause build failures.

---

# Build Validation

The build process validates and packages the Power BI project.

Run:

```powershell
.\build\build.ps1
```

The build process performs:

1. CSV validation
2. Metadata validation
3. Semantic model updates
4. DAX measure generation
5. Tabular Editor processing
6. Power BI project validation
7. Artifact generation

---

## Successful Build Output

A successful build should create:

```
artifacts/
 ├── Pipeline-SLA-Tracker.zip
 ├── validation-results/
 └── build-logs/
```

The generated package can be used for deployment or release distribution.

---

# Power BI Validation

Power BI validation ensures the generated report opens correctly and all report components function as expected.

## Open PBIP Project

Open:

```
reports/
 └── PipelineDashboard.pbip
```

using Power BI Desktop.

---

## Validate Semantic Model

Check:

* Tables load successfully.
* Relationships are active.
* Measures calculate without errors.
* Date relationships work correctly.
* Formatting and display folders are applied.

---

## Validate Report Pages

Verify:

### Pipeline Overview

Check:

* Environment slicer filtering
* Pipeline status indicators
* Runtime calculations
* Last refresh information

---

### SLA Monitoring

Check:

* Floating bar chart displays duration correctly.
* SLA breach colors are applied.
* Success and failure visuals match source data.
* Filters update all visuals.

---

## Validate DAX Measures

Confirm:

* No measure calculation errors.
* KPI values match source CSV data.
* Percentage measures return expected results.

Important measures:

* Active Pipelines
* Average Runtime
* SLA Breach %
* Successful Runs
* Failed Runs

---

# GitHub Actions

Automated validation runs through GitHub Actions workflows.

Workflows are located in:

```
.github/workflows/
```

---

## Validate Workflow

File:

```
.github/workflows/validate.yml
```

Runs on:

* Pull requests
* Push events

Purpose:

* Validate CSV schema.
* Detect invalid changes early.
* Prevent broken merges.

---

## Build Workflow

File:

```
.github/workflows/build.yml
```

Runs when changes are made to:

```
data/**
model/**
powerquery/**
theme/**
src/**
build/**
```

Purpose:

* Run validation.
* Generate Power BI artifacts.
* Upload build outputs.

---

## Release Workflow

File:

```
.github/workflows/release.yml
```

Runs when:

* A GitHub release is published.
* Manually triggered.

Purpose:

* Package final release artifacts.
* Publish deployable versions.

---

# Expected Results

A successful pipeline should show:

## Validation

```
✔ CSV validation passed
✔ Schema validation passed
✔ Metadata validation passed
```

---

## Build

```
✔ Model generation completed
✔ Measures generated successfully
✔ PBIP validation completed
✔ Artifact created
```

---

## GitHub Actions

All workflow jobs should display:

```
✓ Completed successfully
```

Expected artifacts:

* Build package
* Validation logs
* Release package (for releases)

---

# Troubleshooting Failed Builds

## CSV Validation Failure

### Symptoms

```
Missing column
Invalid schema
File not found
```

### Resolution

Check:

* CSV file names.
* Required columns.
* Column spelling.
* Data types.

Review:

```
docs/Configuration.md
```

---

## Power BI Model Errors

### Symptoms

* Semantic model fails to load.
* Measures show errors.
* Relationships are missing.

### Resolution

Check:

* TMDL files.
* Table names.
* Column names.
* Measure definitions.

Regenerate metadata if required:

```powershell
.\scripts\tools\GenerateMetadata.ps1
```

---

## DAX Measure Failures

### Symptoms

* Calculation errors.
* Blank KPI values.
* Incorrect percentages.

### Resolution

Review:

```
src/measures.dax
scripts/MeasureDefinitions.json
```

Validate:

* Table references.
* Column names.
* Filter context.

---

## GitHub Actions Failure

### Symptoms

Build fails in GitHub Actions but works locally.

### Resolution

Check:

1. Workflow logs.
2. Required tools installed in runner.
3. File paths.
4. Artifact folders.

Common causes:

* Incorrect relative paths.
* Missing generated files.
* Permission issues.
* Build script errors.

---

## Artifact Upload Failure

Example:

```
No files were found with the provided path
```

Resolution:

Verify the build process creates:

```
artifacts/
```

before the upload step executes.

---

## Clean Build

If issues persist, perform a clean rebuild:

```powershell
git clean -xfd
git pull

.\build\build.ps1
```

This removes generated files and rebuilds the project from a clean state.

---

# Testing Best Practices

* Run validation before every commit.
* Test Power BI changes locally before pushing.
* Avoid modifying generated files manually.
* Keep documentation updated with automation changes.
* Review GitHub Actions results before merging pull requests.