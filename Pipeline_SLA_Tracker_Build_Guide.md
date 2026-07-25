# Pipeline SLA Tracker — Power BI Build Guide

## 1. Load the data

1. Open Power BI Desktop → **Get Data → Text/CSV**
2. Import `Fact_Pipeline_SampleData.csv` → name it **Fact_Pipeline**
3. Import `Dim_Category.csv` → name it **Dim_Category**
4. In Power Query, set data types:
   - `StartDate`, `EndDate` → Date/Time
   - `SLA_Target_Hrs` → Whole Number
   - `PipelineID`, `Category`, `Status` → Text

## 2. Create the disconnected spacing table (floating bar trick)

New Table (Modeling → New Table):

```
Dim_Spacing = GENERATESERIES(0, 200, 1)
```

Rename the column to `Index`. This table has **no relationship** to any other table — it exists purely to drive the X-axis category order for the floating bar chart. Leave it disconnected.

## 3. Relationships

- `Dim_Category[CategoryName]` (1) → `Fact_Pipeline[Category]` (many)

## 4. DAX Measures

Paste these into a new measure table called `_Measures`:

```dax
Actual Duration Hrs =
DIVIDE(
    DATEDIFF(SELECTEDVALUE(Fact_Pipeline[StartDate]), SELECTEDVALUE(Fact_Pipeline[EndDate]), MINUTE),
    60
)

SLA Breach Flag =
IF([Actual Duration Hrs] > SELECTEDVALUE(Fact_Pipeline[SLA_Target_Hrs]), 1, 0)

Total Pipelines = COUNTROWS(Fact_Pipeline)

Breached Count = SUMX(Fact_Pipeline, [SLA Breach Flag])

SLA Compliance % =
DIVIDE([Total Pipelines] - [Breached Count], [Total Pipelines])

Avg Duration by Category =
AVERAGEX(Fact_Pipeline, [Actual Duration Hrs])

-- Floating bar measures (for a bar chart using StartDate as the invisible base)
Bar Base (Start) = SELECTEDVALUE(Fact_Pipeline[StartDate])
Bar Length (Duration) = [Actual Duration Hrs]
```

## 5. Floating Bar Chart Setup

This is the technique for making bars appear to "float" instead of starting at zero — useful for showing each pipeline's actual start-to-end window against its SLA target.

1. Use a **Stacked Bar Chart** visual
2. Axis: `PipelineID` (or `Dim_Spacing[Index]` if you want custom manual ordering)
3. Values (in this order):
   - First value: a hidden/invisible measure representing the "base" (e.g. hours from the earliest StartDate in the dataset to this row's StartDate) — set its color to **transparent/no fill**
   - Second value: `Bar Length (Duration)` — this is the visible floating segment
4. Add a **conditional formatting rule** on the second segment: red if `SLA Breach Flag = 1`, green otherwise
5. Add a reference line at the SLA target value using the Analytics pane

## 6. Report Pages

**Page 1 — SLA Overview**
- Card visuals: `SLA Compliance %`, `Total Pipelines`, `Breached Count`
- Floating bar chart (per pipeline, built above) as the centerpiece
- Slicer: `Dim_Category[CategoryName]`

**Page 2 — Breach Log**
- Table visual: PipelineID, Category, StartDate, EndDate, Actual Duration Hrs, SLA_Target_Hrs, SLA Breach Flag
- Sort descending by `Actual Duration Hrs - SLA_Target_Hrs`
- Category heatmap (matrix with conditional formatting) showing breach rate by category

## 7. Before listing on Gumroad/Etsy

- Replace sample data with a clearly-labeled "swap this table" instruction in your setup doc
- Strip any real client data
- Export a short GIF/demo video of the floating bar interaction — this is the visual hook that sells the template
