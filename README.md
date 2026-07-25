# Power BI Pipeline SLA Tracker Template

A ready-to-use Power BI template for tracking pipeline/process SLA compliance, featuring a custom **floating bar chart** technique (built with a disconnected `GENERATESERIES` spacing table) to visualize start-to-end duration against SLA targets at a glance.

![Dashboard Preview](assets/preview.png)
*(add a screenshot or GIF of the report here before publishing)*

---

## What's included

| File | Description |
|---|---|
| `data/Fact_Pipeline_SampleData.csv` | Sample fact table — swap with your own pipeline/process data |
| `data/Dim_Category.csv` | Category dimension table |
| `src/measures.dax` | All DAX measures used in the report |
| `PipelineTheme.json` | Custom Power BI theme (green/red SLA compliance palette) |
| `Pipeline_SLA_Tracker_Build_Guide.md` | Full step-by-step build guide — data model, DAX, floating bar chart setup, report layout |

## Features

- **SLA Compliance %** tracking with card visuals
- **Floating bar chart** — visualizes each pipeline's actual start/end window against its SLA target, using a transparent base segment + colored duration segment
- **Conditional formatting** — bars turn red on SLA breach, green on compliance
- **Breach Log page** — sortable table + category heatmap of breach rates
- Category slicer for quick filtering

## Setup

1. Clone this repo
2. Open **Power BI Desktop**
3. Get Data → Text/CSV → import both files from `/data`
4. Follow `Pipeline_SLA_Tracker_Build_Guide.md` for the relationship, DAX measures, and floating bar chart configuration
5. Apply the theme: **View → Themes → Browse for themes** → select `PipelineTheme.json`
6. Replace the sample data with your own pipeline data (see "Data Source Swap" section in the build guide)

## Requirements

- Power BI Desktop (latest version recommended)
- Basic familiarity with Power Query and DAX to customize for your data source

## License

This template is provided for personal and internal business use. Redistribution or resale of the source files (as-is) is not permitted. If you purchased this template, refer to your license terms on the platform of purchase (Gumroad/Etsy).

## Author

Built by Pavithra Radhakrishnan — Power BI Developer & BI Analyst.
