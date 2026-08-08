// ==========================================================
// GenerateMeasures.csx
// AUTO GENERATED
//
// Generated: 2026-08-08 11:25:59
// Generator: GenerateMetadata.ps1 v1.0.0
// Source: C:\Users\pavit\powerbi-pipeline-sla-template-git\scripts\tools\..\metadata\MeasureDefinitions.json
// Tabular Editor 2.28
// ==========================================================

using System.Linq;

int created = 0;
int updated = 0;
int skipped = 0;


// ----------------------------------------------------------
// Active Pipelines
// ID: M001
// Category: KPI
// ----------------------------------------------------------

var table_M001 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M001 != null)
{
    var measure_M001 = table_M001.Measures.FirstOrDefault(m => m.Name == "Active Pipelines");

    if(measure_M001 == null)
    {
        measure_M001 = table_M001.AddMeasure(
            "Active Pipelines",
            @"COUNTROWS(Fact_Pipeline_SampleData)"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M001.Expression = @"COUNTROWS(Fact_Pipeline_SampleData)";
    measure_M001.DisplayFolder = "01 Landing Page";
    measure_M001.Description = "Total pipeline executions.";
    measure_M001.FormatString = "#,##0";
    measure_M001.IsHidden = false;

    measure_M001.SetAnnotation("Owner", "BI Team");
    measure_M001.SetAnnotation("Category", "KPI");
    measure_M001.SetAnnotation("Version", "1.0.0");
    measure_M001.SetAnnotation("DisplayOrder", "10");
    measure_M001.SetAnnotation("Template", "CountRows");
    measure_M001.SetAnnotation("Status", "Active");
    measure_M001.SetAnnotation("Tags", "LandingPage;Executive");
    measure_M001.SetAnnotation("Synonyms", "Pipeline Count;Runs");
    measure_M001.SetAnnotation("KPI", "True");
    measure_M001.SetAnnotation("DataType", "Whole Number");
    measure_M001.SetAnnotation("LastUpdated", "2026-07-31");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Successful Runs
// ID: M002
// Category: KPI
// ----------------------------------------------------------

var table_M002 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M002 != null)
{
    var measure_M002 = table_M002.Measures.FirstOrDefault(m => m.Name == "Successful Runs");

    if(measure_M002 == null)
    {
        measure_M002 = table_M002.AddMeasure(
            "Successful Runs",
            @"CALCULATE(COUNTROWS(Fact_Pipeline_SampleData),Fact_Pipeline_SampleData[Status]=""Success"")"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M002.Expression = @"CALCULATE(COUNTROWS(Fact_Pipeline_SampleData),Fact_Pipeline_SampleData[Status]=""Success"")";
    measure_M002.DisplayFolder = "01 Landing Page";
    measure_M002.Description = "Successful pipeline executions.";
    measure_M002.FormatString = "#,##0";
    measure_M002.IsHidden = false;

    measure_M002.SetAnnotation("Owner", "BI Team");
    measure_M002.SetAnnotation("Category", "KPI");
    measure_M002.SetAnnotation("Version", "1.0.0");
    measure_M002.SetAnnotation("DisplayOrder", "20");
    measure_M002.SetAnnotation("Template", "CountRows");
    measure_M002.SetAnnotation("Status", "Active");
    measure_M002.SetAnnotation("Tags", "LandingPage");
    measure_M002.SetAnnotation("Synonyms", "Successful Pipelines");
    measure_M002.SetAnnotation("KPI", "True");
    measure_M002.SetAnnotation("DataType", "Whole Number");
    measure_M002.SetAnnotation("DependsOn", "Active Pipelines");
    measure_M002.SetAnnotation("LastUpdated", "2026-07-31");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Failed Runs
// ID: M003
// Category: KPI
// ----------------------------------------------------------

var table_M003 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M003 != null)
{
    var measure_M003 = table_M003.Measures.FirstOrDefault(m => m.Name == "Failed Runs");

    if(measure_M003 == null)
    {
        measure_M003 = table_M003.AddMeasure(
            "Failed Runs",
            @"CALCULATE(COUNTROWS(Fact_Pipeline_SampleData),Fact_Pipeline_SampleData[Status]=""Failed"")"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M003.Expression = @"CALCULATE(COUNTROWS(Fact_Pipeline_SampleData),Fact_Pipeline_SampleData[Status]=""Failed"")";
    measure_M003.DisplayFolder = "01 Landing Page";
    measure_M003.Description = "Failed pipeline executions.";
    measure_M003.FormatString = "#,##0";
    measure_M003.IsHidden = false;

    measure_M003.SetAnnotation("Owner", "BI Team");
    measure_M003.SetAnnotation("Category", "KPI");
    measure_M003.SetAnnotation("Version", "1.0.0");
    measure_M003.SetAnnotation("DisplayOrder", "30");
    measure_M003.SetAnnotation("Template", "CountRows");
    measure_M003.SetAnnotation("Status", "Active");
    measure_M003.SetAnnotation("Tags", "LandingPage");
    measure_M003.SetAnnotation("Synonyms", "Failed Pipelines");
    measure_M003.SetAnnotation("KPI", "True");
    measure_M003.SetAnnotation("DataType", "Whole Number");
    measure_M003.SetAnnotation("DependsOn", "Active Pipelines");
    measure_M003.SetAnnotation("LastUpdated", "2026-07-31");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Running Pipelines
// ID: M004
// Category: KPI
// ----------------------------------------------------------

var table_M004 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M004 != null)
{
    var measure_M004 = table_M004.Measures.FirstOrDefault(m => m.Name == "Running Pipelines");

    if(measure_M004 == null)
    {
        measure_M004 = table_M004.AddMeasure(
            "Running Pipelines",
            @"CALCULATE(COUNTROWS(Fact_Pipeline_SampleData),Fact_Pipeline_SampleData[Status]=""Running"")"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M004.Expression = @"CALCULATE(COUNTROWS(Fact_Pipeline_SampleData),Fact_Pipeline_SampleData[Status]=""Running"")";
    measure_M004.DisplayFolder = "01 Landing Page";
    measure_M004.Description = "Running pipelines.";
    measure_M004.FormatString = "#,##0";
    measure_M004.IsHidden = false;

    measure_M004.SetAnnotation("Owner", "BI Team");
    measure_M004.SetAnnotation("Category", "KPI");
    measure_M004.SetAnnotation("Version", "1.0.0");
    measure_M004.SetAnnotation("DisplayOrder", "40");
    measure_M004.SetAnnotation("Template", "CountRows");
    measure_M004.SetAnnotation("Status", "Active");
    measure_M004.SetAnnotation("Tags", "Operations");
    measure_M004.SetAnnotation("Synonyms", "Running");
    measure_M004.SetAnnotation("KPI", "False");
    measure_M004.SetAnnotation("DataType", "Whole Number");
    measure_M004.SetAnnotation("DependsOn", "Active Pipelines");
    measure_M004.SetAnnotation("LastUpdated", "2026-07-31");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Queued Pipelines
// ID: M005
// Category: KPI
// ----------------------------------------------------------

var table_M005 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M005 != null)
{
    var measure_M005 = table_M005.Measures.FirstOrDefault(m => m.Name == "Queued Pipelines");

    if(measure_M005 == null)
    {
        measure_M005 = table_M005.AddMeasure(
            "Queued Pipelines",
            @"CALCULATE(COUNTROWS(Fact_Pipeline_SampleData),Fact_Pipeline_SampleData[Status]=""Queued"")"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M005.Expression = @"CALCULATE(COUNTROWS(Fact_Pipeline_SampleData),Fact_Pipeline_SampleData[Status]=""Queued"")";
    measure_M005.DisplayFolder = "01 Landing Page";
    measure_M005.Description = "Queued pipelines.";
    measure_M005.FormatString = "#,##0";
    measure_M005.IsHidden = false;

    measure_M005.SetAnnotation("Owner", "BI Team");
    measure_M005.SetAnnotation("Category", "KPI");
    measure_M005.SetAnnotation("Version", "1.0.0");
    measure_M005.SetAnnotation("DisplayOrder", "50");
    measure_M005.SetAnnotation("Template", "CountRows");
    measure_M005.SetAnnotation("Status", "Active");
    measure_M005.SetAnnotation("Tags", "Operations");
    measure_M005.SetAnnotation("Synonyms", "Queued");
    measure_M005.SetAnnotation("KPI", "False");
    measure_M005.SetAnnotation("DataType", "Whole Number");
    measure_M005.SetAnnotation("DependsOn", "Active Pipelines");
    measure_M005.SetAnnotation("LastUpdated", "2026-07-31");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Success Rate %
// ID: M006
// Category: SLA
// ----------------------------------------------------------

var table_M006 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M006 != null)
{
    var measure_M006 = table_M006.Measures.FirstOrDefault(m => m.Name == "Success Rate %");

    if(measure_M006 == null)
    {
        measure_M006 = table_M006.AddMeasure(
            "Success Rate %",
            @"DIVIDE([Successful Runs],[Active Pipelines])"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M006.Expression = @"DIVIDE([Successful Runs],[Active Pipelines])";
    measure_M006.DisplayFolder = "02 SLA";
    measure_M006.Description = "Percentage of successful pipeline executions.";
    measure_M006.FormatString = "0.00%";
    measure_M006.IsHidden = false;

    measure_M006.SetAnnotation("Owner", "BI Team");
    measure_M006.SetAnnotation("Category", "SLA");
    measure_M006.SetAnnotation("Version", "1.0.0");
    measure_M006.SetAnnotation("DisplayOrder", "10");
    measure_M006.SetAnnotation("Template", "Ratio");
    measure_M006.SetAnnotation("Status", "Active");
    measure_M006.SetAnnotation("Tags", "Executive");
    measure_M006.SetAnnotation("Synonyms", "Success Rate");
    measure_M006.SetAnnotation("KPI", "True");
    measure_M006.SetAnnotation("DataType", "Percentage");
    measure_M006.SetAnnotation("DependsOn", "Successful Runs;Active Pipelines");
    measure_M006.SetAnnotation("LastUpdated", "2026-07-31");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Failure Rate %
// ID: M007
// Category: SLA
// ----------------------------------------------------------

var table_M007 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M007 != null)
{
    var measure_M007 = table_M007.Measures.FirstOrDefault(m => m.Name == "Failure Rate %");

    if(measure_M007 == null)
    {
        measure_M007 = table_M007.AddMeasure(
            "Failure Rate %",
            @"DIVIDE([Failed Runs],[Active Pipelines])"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M007.Expression = @"DIVIDE([Failed Runs],[Active Pipelines])";
    measure_M007.DisplayFolder = "02 SLA";
    measure_M007.Description = "Percentage of failed pipeline executions.";
    measure_M007.FormatString = "0.00%";
    measure_M007.IsHidden = false;

    measure_M007.SetAnnotation("Owner", "BI Team");
    measure_M007.SetAnnotation("Category", "SLA");
    measure_M007.SetAnnotation("Version", "1.0.0");
    measure_M007.SetAnnotation("DisplayOrder", "20");
    measure_M007.SetAnnotation("Template", "Ratio");
    measure_M007.SetAnnotation("Status", "Active");
    measure_M007.SetAnnotation("Tags", "Executive");
    measure_M007.SetAnnotation("Synonyms", "Failure Rate");
    measure_M007.SetAnnotation("KPI", "True");
    measure_M007.SetAnnotation("DataType", "Percentage");
    measure_M007.SetAnnotation("DependsOn", "Failed Runs;Active Pipelines");
    measure_M007.SetAnnotation("LastUpdated", "2026-07-31");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// SLA Breach %
// ID: M008
// Category: SLA
// ----------------------------------------------------------

var table_M008 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M008 != null)
{
    var measure_M008 = table_M008.Measures.FirstOrDefault(m => m.Name == "SLA Breach %");

    if(measure_M008 == null)
    {
        measure_M008 = table_M008.AddMeasure(
            "SLA Breach %",
            @"DIVIDE(CALCULATE(COUNTROWS(Fact_Pipeline_SampleData), Fact_Pipeline_SampleData[SLAStatus] = ""Missed""), [Active Pipelines], 0)"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M008.Expression = @"DIVIDE(CALCULATE(COUNTROWS(Fact_Pipeline_SampleData), Fact_Pipeline_SampleData[SLAStatus] = ""Missed""), [Active Pipelines], 0)";
    measure_M008.DisplayFolder = "02 SLA";
    measure_M008.Description = "Percentage of SLA breaches.";
    measure_M008.FormatString = "0.00%";
    measure_M008.IsHidden = false;

    measure_M008.SetAnnotation("Owner", "BI Team");
    measure_M008.SetAnnotation("Category", "SLA");
    measure_M008.SetAnnotation("Version", "1.0.0");
    measure_M008.SetAnnotation("DisplayOrder", "30");
    measure_M008.SetAnnotation("Template", "Ratio");
    measure_M008.SetAnnotation("Status", "Active");
    measure_M008.SetAnnotation("Tags", "Executive");
    measure_M008.SetAnnotation("Synonyms", "SLA");
    measure_M008.SetAnnotation("KPI", "True");
    measure_M008.SetAnnotation("DataType", "Percentage");
    measure_M008.SetAnnotation("DependsOn", "Active Pipelines");
    measure_M008.SetAnnotation("LastUpdated", "2026-07-31");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Average Runtime
// ID: M009
// Category: Runtime
// ----------------------------------------------------------

var table_M009 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M009 != null)
{
    var measure_M009 = table_M009.Measures.FirstOrDefault(m => m.Name == "Average Runtime");

    if(measure_M009 == null)
    {
        measure_M009 = table_M009.AddMeasure(
            "Average Runtime",
            @"AVERAGE(Fact_Pipeline_SampleData[DurationHours])"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M009.Expression = @"AVERAGE(Fact_Pipeline_SampleData[DurationHours])";
    measure_M009.DisplayFolder = "03 Runtime";
    measure_M009.Description = "Average runtime in hours.";
    measure_M009.FormatString = "0.00";
    measure_M009.IsHidden = false;

    measure_M009.SetAnnotation("Owner", "BI Team");
    measure_M009.SetAnnotation("Category", "Runtime");
    measure_M009.SetAnnotation("Version", "1.0.0");
    measure_M009.SetAnnotation("DisplayOrder", "10");
    measure_M009.SetAnnotation("Template", "Average");
    measure_M009.SetAnnotation("Status", "Active");
    measure_M009.SetAnnotation("Tags", "Performance");
    measure_M009.SetAnnotation("Synonyms", "Average Duration");
    measure_M009.SetAnnotation("KPI", "False");
    measure_M009.SetAnnotation("DataType", "Decimal");
    measure_M009.SetAnnotation("LastUpdated", "2026-07-31");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Total Runtime
// ID: M010
// Category: Runtime
// ----------------------------------------------------------

var table_M010 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M010 != null)
{
    var measure_M010 = table_M010.Measures.FirstOrDefault(m => m.Name == "Total Runtime");

    if(measure_M010 == null)
    {
        measure_M010 = table_M010.AddMeasure(
            "Total Runtime",
            @"SUM(Fact_Pipeline_SampleData[DurationHours])"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M010.Expression = @"SUM(Fact_Pipeline_SampleData[DurationHours])";
    measure_M010.DisplayFolder = "03 Runtime";
    measure_M010.Description = "Total runtime in minutes.";
    measure_M010.FormatString = "#,##0.00";
    measure_M010.IsHidden = false;

    measure_M010.SetAnnotation("Owner", "BI Team");
    measure_M010.SetAnnotation("Category", "Runtime");
    measure_M010.SetAnnotation("Version", "1.0.0");
    measure_M010.SetAnnotation("DisplayOrder", "20");
    measure_M010.SetAnnotation("Template", "Sum");
    measure_M010.SetAnnotation("Status", "Active");
    measure_M010.SetAnnotation("Tags", "Performance");
    measure_M010.SetAnnotation("Synonyms", "Runtime");
    measure_M010.SetAnnotation("KPI", "False");
    measure_M010.SetAnnotation("DataType", "Decimal");
    measure_M010.SetAnnotation("LastUpdated", "2026-07-31");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Maximum Runtime
// ID: M011
// Category: Runtime
// ----------------------------------------------------------

var table_M011 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M011 != null)
{
    var measure_M011 = table_M011.Measures.FirstOrDefault(m => m.Name == "Maximum Runtime");

    if(measure_M011 == null)
    {
        measure_M011 = table_M011.AddMeasure(
            "Maximum Runtime",
            @"MAX(Fact_Pipeline_SampleData[DurationHours])"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M011.Expression = @"MAX(Fact_Pipeline_SampleData[DurationHours])";
    measure_M011.DisplayFolder = "03 Runtime";
    measure_M011.Description = "Maximum runtime.";
    measure_M011.FormatString = "#,##0.00";
    measure_M011.IsHidden = false;

    measure_M011.SetAnnotation("Owner", "BI Team");
    measure_M011.SetAnnotation("Category", "Runtime");
    measure_M011.SetAnnotation("Version", "1.0.0");
    measure_M011.SetAnnotation("DisplayOrder", "30");
    measure_M011.SetAnnotation("Template", "Maximum");
    measure_M011.SetAnnotation("Status", "Active");
    measure_M011.SetAnnotation("Tags", "Performance");
    measure_M011.SetAnnotation("Synonyms", "Longest Runtime");
    measure_M011.SetAnnotation("KPI", "False");
    measure_M011.SetAnnotation("DataType", "Decimal");
    measure_M011.SetAnnotation("LastUpdated", "2026-07-31");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Minimum Runtime
// ID: M012
// Category: Runtime
// ----------------------------------------------------------

var table_M012 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M012 != null)
{
    var measure_M012 = table_M012.Measures.FirstOrDefault(m => m.Name == "Minimum Runtime");

    if(measure_M012 == null)
    {
        measure_M012 = table_M012.AddMeasure(
            "Minimum Runtime",
            @"MIN(Fact_Pipeline_SampleData[DurationHours])"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M012.Expression = @"MIN(Fact_Pipeline_SampleData[DurationHours])";
    measure_M012.DisplayFolder = "03 Runtime";
    measure_M012.Description = "Minimum runtime.";
    measure_M012.FormatString = "#,##0.00";
    measure_M012.IsHidden = false;

    measure_M012.SetAnnotation("Owner", "BI Team");
    measure_M012.SetAnnotation("Category", "Runtime");
    measure_M012.SetAnnotation("Version", "1.0.0");
    measure_M012.SetAnnotation("DisplayOrder", "40");
    measure_M012.SetAnnotation("Template", "Minimum");
    measure_M012.SetAnnotation("Status", "Active");
    measure_M012.SetAnnotation("Tags", "Performance");
    measure_M012.SetAnnotation("Synonyms", "Shortest Runtime");
    measure_M012.SetAnnotation("KPI", "False");
    measure_M012.SetAnnotation("DataType", "Decimal");
    measure_M012.SetAnnotation("LastUpdated", "2026-07-31");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Timeline Base
// ID: M016
// Category: Floating Bar
// ----------------------------------------------------------

var table_M016 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M016 != null)
{
    var measure_M016 = table_M016.Measures.FirstOrDefault(m => m.Name == "Timeline Base");

    if(measure_M016 == null)
    {
        measure_M016 = table_M016.AddMeasure(
            "Timeline Base",
            @"VAR Earliest =
    CALCULATE(
        MIN(Fact_Pipeline_SampleData[ScheduledStart]),
        ALL(Fact_Pipeline_SampleData)
    )
RETURN
DATEDIFF(
    Earliest,
    MIN(Fact_Pipeline_SampleData[ScheduledStart]),
    HOUR
)"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M016.Expression = @"VAR Earliest =
    CALCULATE(
        MIN(Fact_Pipeline_SampleData[ScheduledStart]),
        ALL(Fact_Pipeline_SampleData)
    )
RETURN
DATEDIFF(
    Earliest,
    MIN(Fact_Pipeline_SampleData[ScheduledStart]),
    HOUR
)";
    measure_M016.DisplayFolder = "05 Floating Bar";
    measure_M016.Description = "Starting position of floating bar timeline.";
    measure_M016.FormatString = "#,##0";
    measure_M016.IsHidden = false;

    measure_M016.SetAnnotation("Owner", "BI Team");
    measure_M016.SetAnnotation("Category", "Floating Bar");
    measure_M016.SetAnnotation("Version", "1.0.0");
    measure_M016.SetAnnotation("DisplayOrder", "10");
    measure_M016.SetAnnotation("Template", "Minimum");
    measure_M016.SetAnnotation("Status", "Active");
    measure_M016.SetAnnotation("Tags", "FloatingBar;Timeline");
    measure_M016.SetAnnotation("Synonyms", "Bar Start;Timeline Start");
    measure_M016.SetAnnotation("KPI", "False");
    measure_M016.SetAnnotation("DataType", "Whole Number");
    measure_M016.SetAnnotation("LastUpdated", "2026-08-01");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Floating Bar Duration
// ID: M017
// Category: Floating Bar
// ----------------------------------------------------------

var table_M017 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M017 != null)
{
    var measure_M017 = table_M017.Measures.FirstOrDefault(m => m.Name == "Floating Bar Duration");

    if(measure_M017 == null)
    {
        measure_M017 = table_M017.AddMeasure(
            "Floating Bar Duration",
            @"AVERAGE(Fact_Pipeline_SampleData[DurationHours])"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M017.Expression = @"AVERAGE(Fact_Pipeline_SampleData[DurationHours])";
    measure_M017.DisplayFolder = "05 Floating Bar";
    measure_M017.Description = "Length of floating bar in hours.";
    measure_M017.FormatString = "#,##0.00";
    measure_M017.IsHidden = false;

    measure_M017.SetAnnotation("Owner", "BI Team");
    measure_M017.SetAnnotation("Category", "Floating Bar");
    measure_M017.SetAnnotation("Version", "1.0.0");
    measure_M017.SetAnnotation("DisplayOrder", "20");
    measure_M017.SetAnnotation("Template", "Average");
    measure_M017.SetAnnotation("Status", "Active");
    measure_M017.SetAnnotation("Tags", "FloatingBar;Runtime");
    measure_M017.SetAnnotation("Synonyms", "Bar Width;Duration");
    measure_M017.SetAnnotation("KPI", "False");
    measure_M017.SetAnnotation("DataType", "Decimal Number");
    measure_M017.SetAnnotation("LastUpdated", "2026-08-01");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Floating Bar Status
// ID: M018
// Category: Floating Bar
// ----------------------------------------------------------

var table_M018 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M018 != null)
{
    var measure_M018 = table_M018.Measures.FirstOrDefault(m => m.Name == "Floating Bar Status");

    if(measure_M018 == null)
    {
        measure_M018 = table_M018.AddMeasure(
            "Floating Bar Status",
            @"IF(MAX(Fact_Pipeline_SampleData[SLAStatus])=""Breached"",""Breach"",""Within SLA"")"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M018.Expression = @"IF(MAX(Fact_Pipeline_SampleData[SLAStatus])=""Breached"",""Breach"",""Within SLA"")";
    measure_M018.DisplayFolder = "05 Floating Bar";
    measure_M018.Description = "Status label used for floating bar conditional formatting.";
    measure_M018.FormatString = "";
    measure_M018.IsHidden = false;

    measure_M018.SetAnnotation("Owner", "BI Team");
    measure_M018.SetAnnotation("Category", "Floating Bar");
    measure_M018.SetAnnotation("Version", "1.0.0");
    measure_M018.SetAnnotation("DisplayOrder", "30");
    measure_M018.SetAnnotation("Template", "Status");
    measure_M018.SetAnnotation("Status", "Active");
    measure_M018.SetAnnotation("Tags", "FloatingBar;SLA");
    measure_M018.SetAnnotation("Synonyms", "Bar Status");
    measure_M018.SetAnnotation("KPI", "False");
    measure_M018.SetAnnotation("DataType", "Text");
    measure_M018.SetAnnotation("DependsOn", "SLA Breach %");
    measure_M018.SetAnnotation("LastUpdated", "2026-08-01");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// SLA Breach Color
// ID: M019
// Category: Floating Bar
// ----------------------------------------------------------

var table_M019 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M019 != null)
{
    var measure_M019 = table_M019.Measures.FirstOrDefault(m => m.Name == "SLA Breach Color");

    if(measure_M019 == null)
    {
        measure_M019 = table_M019.AddMeasure(
            "SLA Breach Color",
            @"IF(MAX(Fact_Pipeline_SampleData[SLAStatus])=""Breached"", ""#FF0000"", ""#00B050"")"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M019.Expression = @"IF(MAX(Fact_Pipeline_SampleData[SLAStatus])=""Breached"", ""#FF0000"", ""#00B050"")";
    measure_M019.DisplayFolder = "05 Floating Bar";
    measure_M019.Description = "Color code for floating bar conditional formatting.";
    measure_M019.FormatString = "";
    measure_M019.IsHidden = true;

    measure_M019.SetAnnotation("Owner", "BI Team");
    measure_M019.SetAnnotation("Category", "Floating Bar");
    measure_M019.SetAnnotation("Version", "1.0.0");
    measure_M019.SetAnnotation("DisplayOrder", "40");
    measure_M019.SetAnnotation("Template", "Color");
    measure_M019.SetAnnotation("Status", "Active");
    measure_M019.SetAnnotation("Tags", "FloatingBar;ConditionalFormatting");
    measure_M019.SetAnnotation("Synonyms", "Bar Color");
    measure_M019.SetAnnotation("KPI", "False");
    measure_M019.SetAnnotation("DataType", "Text");
    measure_M019.SetAnnotation("DependsOn", "SLA Breach %");
    measure_M019.SetAnnotation("LastUpdated", "2026-08-01");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Average Start Delay
// ID: M020
// Category: Runtime
// ----------------------------------------------------------

var table_M020 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M020 != null)
{
    var measure_M020 = table_M020.Measures.FirstOrDefault(m => m.Name == "Average Start Delay");

    if(measure_M020 == null)
    {
        measure_M020 = table_M020.AddMeasure(
            "Average Start Delay",
            @"AVERAGE(Fact_Pipeline_SampleData[StartDelayMinutes])"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M020.Expression = @"AVERAGE(Fact_Pipeline_SampleData[StartDelayMinutes])";
    measure_M020.DisplayFolder = "03 Runtime";
    measure_M020.Description = "Average delay before pipeline start.";
    measure_M020.FormatString = "#,##0.00";
    measure_M020.IsHidden = false;

    measure_M020.SetAnnotation("Owner", "BI Team");
    measure_M020.SetAnnotation("Category", "Runtime");
    measure_M020.SetAnnotation("Version", "1.0.0");
    measure_M020.SetAnnotation("DisplayOrder", "50");
    measure_M020.SetAnnotation("Template", "Average");
    measure_M020.SetAnnotation("Status", "Active");
    measure_M020.SetAnnotation("Tags", "Delay");
    measure_M020.SetAnnotation("Synonyms", "Start Delay");
    measure_M020.SetAnnotation("KPI", "False");
    measure_M020.SetAnnotation("DataType", "Decimal");
    measure_M020.SetAnnotation("LastUpdated", "2026-08-01");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Average End Delay
// ID: M021
// Category: Runtime
// ----------------------------------------------------------

var table_M021 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M021 != null)
{
    var measure_M021 = table_M021.Measures.FirstOrDefault(m => m.Name == "Average End Delay");

    if(measure_M021 == null)
    {
        measure_M021 = table_M021.AddMeasure(
            "Average End Delay",
            @"AVERAGE(Fact_Pipeline_SampleData[EndDelayMinutes])"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M021.Expression = @"AVERAGE(Fact_Pipeline_SampleData[EndDelayMinutes])";
    measure_M021.DisplayFolder = "03 Runtime";
    measure_M021.Description = "Average delay after scheduled completion.";
    measure_M021.FormatString = "#,##0.00";
    measure_M021.IsHidden = false;

    measure_M021.SetAnnotation("Owner", "BI Team");
    measure_M021.SetAnnotation("Category", "Runtime");
    measure_M021.SetAnnotation("Version", "1.0.0");
    measure_M021.SetAnnotation("DisplayOrder", "60");
    measure_M021.SetAnnotation("Template", "Average");
    measure_M021.SetAnnotation("Status", "Active");
    measure_M021.SetAnnotation("Tags", "Delay");
    measure_M021.SetAnnotation("Synonyms", "End Delay");
    measure_M021.SetAnnotation("KPI", "False");
    measure_M021.SetAnnotation("DataType", "Decimal");
    measure_M021.SetAnnotation("LastUpdated", "2026-08-01");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Success Rate KPI
// ID: M022
// Category: SLA
// ----------------------------------------------------------

var table_M022 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M022 != null)
{
    var measure_M022 = table_M022.Measures.FirstOrDefault(m => m.Name == "Success Rate KPI");

    if(measure_M022 == null)
    {
        measure_M022 = table_M022.AddMeasure(
            "Success Rate KPI",
            @"[Success Rate %]"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M022.Expression = @"[Success Rate %]";
    measure_M022.DisplayFolder = "02 SLA";
    measure_M022.Description = "KPI for success rate with target and status.";
    measure_M022.FormatString = "0.00%";
    measure_M022.IsHidden = false;

    measure_M022.SetAnnotation("Owner", "BI Team");
    measure_M022.SetAnnotation("Category", "SLA");
    measure_M022.SetAnnotation("Version", "1.0.0");
    measure_M022.SetAnnotation("DisplayOrder", "40");
    measure_M022.SetAnnotation("Template", "Ratio");
    measure_M022.SetAnnotation("Status", "Active");
    measure_M022.SetAnnotation("Tags", "Executive");
    measure_M022.SetAnnotation("Synonyms", "Success Rate KPI");
    measure_M022.SetAnnotation("KPI", "True");
    measure_M022.SetAnnotation("DataType", "Percentage");
    measure_M022.SetAnnotation("DependsOn", "Success Rate %");
    measure_M022.SetAnnotation("LastUpdated", "2026-08-01");
    measure_M022.SetAnnotation("KPIConfig", "@{TargetExpression=0.95; StatusExpression=IF([Success Rate %]>=0.95,1,0); TrendExpression=[Success Rate %]}");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ----------------------------------------------------------
// Last Refresh
// ID: M023
// Category: System
// ----------------------------------------------------------

var table_M023 = Model.Tables["Fact_Pipeline_SampleData"];

if(table_M023 != null)
{
    var measure_M023 = table_M023.Measures.FirstOrDefault(m => m.Name == "Last Refresh");

    if(measure_M023 == null)
    {
        measure_M023 = table_M023.AddMeasure(
            "Last Refresh",
            @"FORMAT(NOW(),""dd MMM yyyy hh:mm AM/PM"")"
        );

        created++;
    }
    else
    {
        updated++;
    }

    measure_M023.Expression = @"FORMAT(NOW(),""dd MMM yyyy hh:mm AM/PM"")";
    measure_M023.DisplayFolder = "00 System";
    measure_M023.Description = "Last data refresh timestamp.";
    measure_M023.FormatString = "";
    measure_M023.IsHidden = false;

    measure_M023.SetAnnotation("Owner", "BI Team");
    measure_M023.SetAnnotation("Category", "System");
    measure_M023.SetAnnotation("Version", "1.0.0");
    measure_M023.SetAnnotation("DisplayOrder", "5");
    measure_M023.SetAnnotation("Template", "Text");
    measure_M023.SetAnnotation("Status", "Active");
    measure_M023.SetAnnotation("Tags", "System");
    measure_M023.SetAnnotation("Synonyms", "Refresh Time");
    measure_M023.SetAnnotation("KPI", "False");
    measure_M023.SetAnnotation("DataType", "Text");
    measure_M023.SetAnnotation("LastUpdated", "2026-08-02");
}
else
{
    Console.WriteLine("Table 'Fact_Pipeline_SampleData' not found.");
}
// ==========================================================
// Completed
// ==========================================================

Console.WriteLine("Measures created: " + created);
Console.WriteLine("Measures updated: " + updated);
Console.WriteLine("Measures skipped: " + skipped);

