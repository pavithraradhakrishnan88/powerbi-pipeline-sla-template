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

## Current stabilization status

The following previously pending stabilization issues are closed and are part of the known-good template behavior:

- Semantic-model template artifacts required for Desktop loading were restored, including the authoritative `en-US.tmdl`, required `LocalDateTable_*.tmdl` files, and platform metadata.
- PBIP artifact preparation/copy now preserves the required `.platform` files for the report and semantic model.
- The report is materialized from the authoritative report template rather than reconstructed from a reduced/generated visual tree.
- The generated report preserves the authoritative 28-visual inventory and validates the exact published artifact.
- Every published `visual.json` is parsed as JSON and the 28/28 visual artifact gate remains mandatory.
- The report-extension path no longer synthesizes an empty/fake `reportExtensions.json`. If the authoritative template contains a real extension definition, `WriteFromTemplate()` copies it naturally.
- The test/template path is aligned with the actual authoritative report template; production validation is not weakened to accommodate a reduced fixture.
- Power BI Desktop UAT now reaches the usable report state: visuals load and the report is interactive. The former `ModelAuthoringHostService.UpdateModelExtensions` failure caused by the synthetic extension artifact is closed.

### Remaining validation item

The only remaining known issue is a schema-analyzer compatibility mismatch for the July 2026 Desktop `visualContainer/2.10.0` format. The authoritative template legitimately contains `visual.sortDefinition` and `visual.visualContainerObjects.columnHeaders`, while the analyzer currently reports them as additional properties.

The required resolution is limited to the analyzer/schema compatibility definition: explicitly allow those two properties for the corresponding 2.10.0 format while retaining strict `additionalProperties: false` everywhere else. Do not modify the two authoritative `visual.json` files, downgrade their `$schema`, or add a blanket additional-properties exception.

## Successful Version 1 baseline

The released Version 1 baseline is the PR #11 resolved integration that was successfully validated before being merged to `main`.

The successful changes leading to that baseline were:

- Restored the known-good floating-bar visual template state so report visuals are preserved rather than regenerated destructively.
- Restored the PR #10 exact Fact template and limited changes to the required Floating Bar DAX contracts.
- Restored the DataFolder-backed Fact partition so the PBIP remains portable between local Desktop use and CI.
- Corrected the Floating Bar semantic expressions while preserving the authoritative template.
- Removed the stale `Breached` SLA-domain assertion and aligned Floating Bar UAT with semantic mapping rather than brittle DAX text shape.
- Validated generated semantic expressions against the published artifact.
- Applied the recursive visual-preservation gate to the conflict-resolved integration branch.
- Preserved the semantic and UAT fixes while resolving PR #10 into PR #11.
- Added/fixed local Desktop parameter preparation required by the final PR #11 baseline.

The successful PR #11 head was `412864da6f9cffdfcef5df0998daca29b99bead4`. It passed the Validate and Build workflows before being merged to `main`.

### Explicitly excluded from the baseline

**PR #12 is not part of the successful Version 1 baseline.** PR #12 was an experimental relationship-validator change created from `main` rather than from the resolved PR #11 integration state. It was closed without being merged after its validation exposed incompatibility with the existing model-generation/test fixture path.

Do not use the PR #12 relationship-validator experiment as the basis for modifying the Version 1 semantic model. The successful baseline is the PR #11 integration that was validated and then merged to `main`.

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

## PBIP artifact materialization for Desktop validation

When validating a packaged/generated PBIP artifact locally, use the same materialization boundary used by CI. Run the PowerShell script from the extracted artifact directory, or pass the artifact directory explicitly:

```powershell
Set-Location "C:\path\to\extracted\artifact"
powershell.exe -ExecutionPolicy Bypass -File ".\build\Materialize-PbipArtifact.ps1" -ArtifactRoot "C:\path\to\extracted\artifact"
```

If the script is already copied into the artifact directory, run:

```powershell
powershell.exe -ExecutionPolicy Bypass -File ".\Materialize-PbipArtifact.ps1" -ArtifactRoot "C:\path\to\extracted\artifact"
```

The script resolves the shared `DataFolder` expression, materializes CSV `File.Contents(...)` references to the artifact-local `data` directory, verifies the Fact and `Dim_Category` sources, rejects remaining runner-specific paths, rejects UTF-8 BOMs in TMDL, and emits the `DATA-MATERIALIZATION-GATE|PASS` result before Desktop validation.

For the repository copy, the script is:

```text
build/Materialize-PbipArtifact.ps1
```

Do not modify `build.ps1`, PBIP generation, templates, report files, semantic-model generation, or artifact-integrity gates to perform this materialization. It is intentionally isolated to `Materialize-PbipArtifact.ps1`.

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
