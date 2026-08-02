## Data Model

### Fact Table

Fact_Pipeline

Contains:

- Pipeline
- Workspace
- Start Time
- End Time
- Status
- Duration

### Dimension Tables

DimDate

DimWorkspace

DimPipeline

### Relationships

DimDate
    1 ───── *
Fact_Pipeline

DimWorkspace
    1 ───── *
Fact_Pipeline

DimPipeline
    1 ───── *
Fact_Pipeline

## Dashboard Layout

### Page 1

Pipeline Overview

Visuals

- KPI Cards
- SLA %
- Average Duration
- Success Rate

### Page 2

Pipeline Details

Visuals

- Table
- Matrix
- Filters

### Page 3

Failures

Visuals

- Failed Pipelines
- Error Counts
- Trend Chart

## Build Process

Build steps:

1. Validate folder structure
2. Validate required files
3. Validate Power Query exports
4. Validate semantic model
5. Produce build artifacts

## Deployment

Deployment Steps

1. Clone repository
2. Open Power BI project
3. Refresh data
4. Publish report
5. Configure scheduled refresh
6. Validate dashboard

## Screenshots

### Dashboard Overview

![Dashboard Overview](screenshots/dashboard-overview.png)

### Data Model

![Data Model](screenshots/data-model.png)

### Relationships

![Relationships](screenshots/relationships.png)