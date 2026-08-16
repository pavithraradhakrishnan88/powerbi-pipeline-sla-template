# User Guide — Version 1.0.0

The Pipeline SLA Tracker is an interactive Power BI report for monitoring pipeline execution, SLA compliance, runtime, and operational exceptions.

## Report pages

### Home

Landing page for the Version 1 report. Use the configured KPI and summary visuals to understand current pipeline health.

### Executive Overview

Provides the executive KPI and operational overview, including pipeline counts, success/failure rates, runtime indicators, and SLA measures.

### SLA Exceptions

Focuses on pipelines that require investigation because of SLA breaches or execution exceptions. Use the available filters to narrow the exception set.

## Key measures

Version 1 includes measures such as:

- Active Pipelines
- Successful Runs
- Failed Runs
- Success Rate %
- Failure Rate %
- SLA Breach %
- Total Runs
- Breached Count
- SLA Compliance %
- Average Runtime
- Timeline Base
- Floating Bar Duration
- Floating Bar Status

The measure source of truth is `scripts/metadata/MeasureDefinitions.json`.

## Filters and slicers

Where configured on the report, use slicers to filter the report by environment, category, time window, or status.

After changing a slicer, the expected visuals should visibly update. If a slicer does not change the intended visuals, treat it as a Desktop UAT defect and verify the relationship/field binding.

## SLA interpretation

- **SLA Compliance %** represents the proportion of runs that met the SLA.
- **SLA Breach %** represents the proportion of runs that breached the SLA.
- **Breached Count** is the number of runs classified as SLA breaches.

Always interpret these measures in the current filter context.

## Floating-bar visualization

The floating-bar experience uses timeline-base and duration measures to position the runtime window. The report template controls the visual field bindings and formatting; users should not recreate the visual manually for normal Version 1 use.

## Replacing sample data

1. Replace the files in `data/` while preserving the required schema.
2. Run `build/validate.ps1`.
3. Run `build/build.ps1`.
4. Open `pbip/Pipeline_SLA_Tracker.pbip` in Power BI Desktop.
5. Refresh/validate the model.
6. Check slicers, KPI values, visuals, and registered resources.

The build keeps `DataFolder` portable; do not hardcode a local or CI-runner absolute path.

## Desktop UAT

Before publishing Version 1, confirm:

- KPI hierarchy and measure bindings render correctly.
- Slicers visibly filter visuals.
- All three pages render correctly.
- Floating-bar and SLA visuals display expected values.
- Registered report images/resources display.

Automated CI validation proves structural/semantic integrity, not every Desktop interaction.

## Troubleshooting

### Missing or incorrect KPI

Check the selected filter context and the measure definition in `scripts/metadata/MeasureDefinitions.json`.

### Slicer has no effect

Check the model relationship and the visual's field bindings. Reopen the authoritative PBIP after rebuilding.

### Missing image/resource

Confirm the registered resource exists in the final report artifact and that the exact published artifact passed the resource/visual gates.

### Build failure

Run `build/validate.ps1` first, then inspect the build logs and the failed gate named by `build/build.ps1`.
