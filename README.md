# Power BI Pipeline SLA Tracker Template

A source-controlled Power BI PBIP/PBIR template for monitoring pipeline execution, SLA compliance, runtime, failures, and operational health.

**Current release: Version 1.0.0**

## Version 1 highlights

- Template-first PBIP generation from the checked-in report and semantic-model templates.
- Authoritative measure definitions in `scripts/metadata/MeasureDefinitions.json`.
- Tabular Editor 2.28-compatible generated measure script at `scripts/GenerateMeasures.csx`.
- Automated inline measure materialization during the repository build.
- Three report pages: Home, Executive Overview, and SLA Exceptions.
- Floating-bar SLA visualization and SLA compliance KPIs.
- Portable `DataFolder` resolution; the build does not embed a CI-runner data path.
- PBIR validation plus a hard 28/28 visual JSON artifact gate.
- Registered report resources/images carried through the published artifact.

## Repository structure

```text
powerbi-pipeline-sla-template/
├── data/                         # Sample CSV data
├── model/                        # Source model/measure metadata
├── pbip/                         # Authoritative Power BI project
├── powerquery/                   # Power Query definitions
├── scripts/
│   ├── metadata/MeasureDefinitions.json
│   ├── tools/GenerateMetadata.ps1
│   └── GenerateMeasures.csx
├── src/                          # DAX/supporting source
├── build/                        # Validation/build/release scripts
├── docs/                         # Current project documentation
├── Pipeline_SLA_Tracker_Build_Guide.md
└── .github/workflows/            # CI/CD
```

## Build Version 1

Prerequisites:

- Power BI Desktop
- PowerShell 7+
- Git
- .NET SDK
- Tabular Editor 2.28 for the manual measure-generation/inspection workflow

Generate the Tabular Editor script from the measure contract:

```powershell
.\scripts\tools\GenerateMetadata.ps1
```

Run the automated build:

```powershell
.\build\validate.ps1
.\build\build.ps1
```

The automated build is not dependent on a CI installation of Tabular Editor. It materializes the authoritative measure definitions itself, while `GenerateMeasures.csx` remains the supported Tabular Editor 2.28 workflow for manual inspection/materialization.

## Power BI Desktop validation

Open:

```text
pbip/Pipeline_SLA_Tracker.pbip
```

After a successful build, verify in Desktop that:

1. KPI fields and hierarchy render correctly.
2. Slicers visibly filter the intended visuals.
3. All three report pages and visuals render without errors.
4. Registered images/resources display.
5. SLA and floating-bar visuals show expected values.

CI semantic validation does not by itself prove Desktop rendering or interaction.

## Documentation

- [Getting Started](docs/getting-started.md)
- [Architecture](docs/architecture.md)
- [Developer Guide](docs/DeveloperGuide.md)
- [Metadata Schema](docs/MetadataSchema.md)
- [User Guide](docs/UserGuide.md)
- [Configuration](docs/Configuration.md)
- [Testing](docs/Testing.md)
- [Build Guide](Pipeline_SLA_Tracker_Build_Guide.md)
- [Changelog](CHANGELOG.md)

## Release acceptance

Version 1 is releasable when the repository validation/build pipeline is green, the exact published artifact passes the 28/28 visual JSON gate, and a Power BI Desktop smoke test confirms slicer behavior, page/visual rendering, and resource display.

See `CHANGELOG.md` for the release history.

## License

MIT License. Commercial resale of the template package, branding, documentation, or marketplace distribution requires separate authorization.
