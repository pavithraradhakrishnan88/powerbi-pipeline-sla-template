using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    public static class ReportDefinitionTransformer
    {
        public static ReportDefinitionDocument CreateDefault(string projectBaseName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(projectBaseName);

            return new ReportDefinitionDocument
            {
                Name = projectBaseName + " Report",
                Pages = new List<ReportPageDefinition>
                {
                    new ReportPageDefinition { PageId = "pipeline-overview", Name = "PipelineOverview", DisplayName = "Pipeline Overview", Order = 0, Canvas = new CanvasDefinition { Width = 1280, Height = 720 } },
                    new ReportPageDefinition { PageId = "sla-analysis", Name = "SLAAnalysis", DisplayName = "SLA Analysis", Order = 1, Canvas = new CanvasDefinition { Width = 1280, Height = 720 } }
                },
                VisualPositions = new List<VisualPositionDefinition>
                {
                    new VisualPositionDefinition { PageId = "pipeline-overview", VisualId = "KpiCardTotal", X = 40, Y = 40, Width = 280, Height = 130 },
                    new VisualPositionDefinition { PageId = "sla-analysis", VisualId = "TrendLine", X = 40, Y = 200, Width = 900, Height = 420 }
                },
                Slicers = new List<SlicerDefinition>
                {
                    new SlicerDefinition { PageId = "pipeline-overview", Field = "Fact_Pipeline_SampleData[Status]", Type = "dropdown", X = 980, Y = 40, Width = 240, Height = 60 }
                },
                Themes = new List<ThemeDefinition> { new ThemeDefinition { Name = "Pipeline Theme", Path = "PipelineTheme.json" } },
                Navigation = new NavigationDefinition { DefaultPage = "pipeline-overview", Menu = new List<string> { "pipeline-overview", "sla-analysis" } },
                Bookmarks = new List<BookmarkDefinition> { new BookmarkDefinition { BookmarkId = "default-view", Name = "Default View", PageId = "pipeline-overview", IsDefault = true } }
            };
        }
    }

    public sealed class ReportDefinitionDocument
    {
        public string Name { get; set; } = "Report";
        public List<ReportPageDefinition> Pages { get; set; } = new List<ReportPageDefinition>();
        public List<VisualPositionDefinition> VisualPositions { get; set; } = new List<VisualPositionDefinition>();
        public List<SlicerDefinition> Slicers { get; set; } = new List<SlicerDefinition>();
        public List<ThemeDefinition> Themes { get; set; } = new List<ThemeDefinition>();
        public NavigationDefinition Navigation { get; set; } = new NavigationDefinition();
        public List<BookmarkDefinition> Bookmarks { get; set; } = new List<BookmarkDefinition>();
    }

    public sealed class ReportPageDefinition
    {
        public string PageId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int Order { get; set; }
        public CanvasDefinition Canvas { get; set; } = new CanvasDefinition();
    }

    public sealed class CanvasDefinition
    {
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public sealed class VisualPositionDefinition
    {
        public string PageId { get; set; } = string.Empty;
        public string Page { get; set; } = string.Empty;
        public string VisualId { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        // When rebuilding page.json from an existing PBIR report, retain the
        // complete authoritative visual container instead of synthesizing a new visual.
        public JsonObject? VisualContainer { get; set; }
    }

    public sealed class SlicerDefinition
    {
        public string PageId { get; set; } = string.Empty;
        public string Page { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public sealed class ThemeDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    public sealed class NavigationDefinition
    {
        public string DefaultPage { get; set; } = string.Empty;
        public List<string> Menu { get; set; } = new List<string>();
    }

    public sealed class BookmarkDefinition
    {
        public string BookmarkId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PageId { get; set; } = string.Empty;
        public string Page { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
    }
}