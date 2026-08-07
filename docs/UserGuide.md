# User Guide

## Report Overview

The **Power BI Pipeline SLA Tracker** provides an interactive dashboard to monitor pipeline execution performance, SLA compliance, runtime trends, and operational health.

The report is designed for operations teams, data engineers, and business users who need visibility into:

- Pipeline execution status
- SLA compliance
- Runtime performance
- Failed and successful executions
- Environment-level monitoring

The dashboard uses a combination of KPI cards, charts, slicers, and timeline visualizations to help identify delayed or failed processes quickly.

---

# Dashboard Pages

## Pipeline Overview

The **Pipeline Overview** page provides a high-level summary of pipeline health.

### Key Visuals

#### KPI Cards

The summary cards display important operational metrics:

- **Active Pipelines**
  - Total number of pipelines included in the selected filter context.
  - Helps users understand the current pipeline workload.

- **Average Runtime**
  - Shows the average execution duration across selected pipelines.
  - Useful for identifying performance changes over time.

- **SLA Breach %**
  - Displays the percentage of pipeline executions that exceeded their SLA target.
  - Higher values indicate potential operational issues.

- **Success Rate**
  - Represents the percentage of successful pipeline executions.

---

### Pipeline Execution Summary

This visual provides an overview of pipeline execution results.

Users can identify:

- Successful pipeline runs
- Failed pipeline runs
- SLA compliance trends
- Execution patterns across environments

---

## SLA Monitoring

The **SLA Monitoring** page focuses on detailed SLA tracking and exception analysis.

### Floating Bar Chart

The floating bar chart displays pipeline execution duration compared with SLA expectations.

It helps users quickly identify:

- Pipelines completing within SLA
- Pipelines exceeding SLA targets
- Long-running processes requiring investigation

### Environment Failure Analysis

This visual compares failures across environments:

- Development (Dev)
- Testing (Test)
- Production (Prod)

Use this view to identify whether failures are isolated to a specific environment.

### Success vs Failure Distribution

The status distribution chart shows the proportion of:

- Successful runs
- Failed runs

This provides a quick health check of pipeline reliability.

---

# Using Filters

Filters allow users to analyze specific pipeline scenarios.

## Environment

The Environment filter allows users to view pipeline activity by deployment environment.

Available examples:

- Dev
- Test
- Prod

Use this filter to compare operational stability between environments.

---

## Status

The Status filter allows users to focus on specific execution outcomes.

Typical values:

- Success
- Failed

Use this filter to investigate failed runs or validate successful processing.

---

## Time Window

The Time Window filter controls the reporting period.

Use this filter to analyze:

- Recent pipeline executions
- Historical performance
- SLA trends over a selected period

---

# Understanding SLA Metrics

## Active Pipelines

**Active Pipelines** represents the number of pipelines included in the current report selection.

Example:

If the Environment filter is set to Production, the metric shows only active production pipelines.

---

## Average Runtime

Average Runtime measures the typical execution duration of pipelines.

Formula:


Average Runtime = Total Runtime / Number of Pipeline Runs


Use this metric to identify:

- Increasing execution times
- Performance degradation
- Optimization opportunities

---

## SLA Breach %

SLA Breach % identifies how frequently pipelines exceed their expected completion time.

Formula:


SLA Breach % = SLA Breached Runs / Total Pipeline Runs


Interpretation:

| Value | Meaning |
|---|---|
| Low percentage | Pipelines are meeting SLA expectations |
| High percentage | Pipelines require investigation |

---

## Success vs Failure

This metric compares completed successful runs against failed executions.

Use it to monitor:

- Reliability
- Operational stability
- Recurring pipeline issues

---

# Floating Bar Chart Explanation

The Floating Bar Chart is the main timeline visualization used in the SLA Tracker.

It displays:

- Pipeline name on the Y-axis
- Scheduled start time as the starting position
- Runtime duration as the floating bar length

Each bar represents the execution window of a pipeline.

### SLA Status Colors

The chart highlights SLA performance:

- Green → Pipeline completed within SLA
- Red → Pipeline exceeded SLA target

This allows users to identify SLA violations without reviewing individual records.

---

# Replacing Sample Data

The dashboard includes sample pipeline execution data for demonstration purposes.

To connect your own pipeline data:

1. Replace the sample CSV files in the `data/` folder.

Example:


data/
├── Fact_Pipeline_SampleData.csv
└── Dim_Category.csv


2. Maintain the required column structure.

The main fact table should include fields such as:

| Column | Description |
|---|---|
| PipelineName | Pipeline or process name |
| Environment | Execution environment |
| Status | Success or Failed |
| StartDate | Actual execution start time |
| EndDate | Actual execution completion time |
| Runtime | Execution duration |
| SLA Target | Expected completion duration |

3. Refresh the Power BI model.

4. Validate that all visuals update correctly.

---

# Troubleshooting

## Dashboard Does Not Refresh

Check:

- Source files exist in the expected folder.
- CSV column names match the required schema.
- Data types are correct.

---

## Missing Visual Data

Possible causes:

- Filters are limiting available records.
- Sample data has been replaced incorrectly.
- Required columns are missing.

---

## SLA Metrics Show Incorrect Values

Verify:

- SLA target values are populated.
- Runtime calculations are correct.
- Status values match expected categories.

---

## Build or Validation Errors

Run the validation script:

```powershell
.\build\validate.ps1

Check:

CSV structure
Required files
Model configuration

For build issues, review the generated logs under the build artifacts folder.

Best Practices

For reliable SLA monitoring:

Keep pipeline names consistent.
Maintain accurate SLA targets.
Refresh data regularly.
Review failed pipelines and SLA breaches regularly.
Use environment filtering before investigating issues.