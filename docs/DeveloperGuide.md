# Developer Guide — Version 1.0.0

This guide describes the current template-first PBIP generation architecture and the measure metadata workflow.

## Current stabilization status

The repository has closed the major PBIP/Desktop stabilization issues that previously blocked reliable UAT:

- The authoritative semantic-model template artifacts and required `.platform` files are restored.
- The report build uses the real authoritative report template for materialization and testing rather than a reduced synthetic report fixture.
- The exact 28-visual template inventory is preserved and enforced.
- Every published `visual.json` is parsed before release.
- `WriteFromTemplate()` remains a true recursive template copier.
- No empty or fake `reportExtensions.json` is synthesized. A real extension definition is copied only when present in the authoritative template.
- The Desktop `UpdateModelExtensions` failure associated with the synthetic extension artifact is closed; current UAT loads the visuals and report successfully.

One validation issue remains: the July 2026 `visualContainer/2.10.0` schema analyzer does not recognize the legitimate `visual.sortDefinition` and `visual.visualContainerObjects.columnHeaders` properties emitted by the authoritative template. The fix belongs only in the analyzer's 2.10.0 compatibility definition and must retain strict `additionalProperties: false` elsewhere.

## Authoritative sources

- Report template: `pbip/Pipeline_SLA_Tracker.Report/`
- Semantic-model template: `pbip/Pipeline_SLA_Tracker.SemanticModel/`
- Measure contract: `scripts/metadata/MeasureDefinitions.json`
- TE2.28 generated script: `scripts/GenerateMeasures.csx`
- Generator: `scripts/tools/GenerateMetadata.ps1`
- Build: `build/build.ps1`

## Measure workflow

`MeasureDefinitions.json` is the source of truth. `GenerateMetadata.ps1` produces `GenerateMeasures.csx`, which can be executed in Tabular Editor 2.28 for manual materialization and inspection.

The automated build does not depend on Tabular Editor. Its authoritative generator materializes the required inline measures into the generated semantic model and validates the result against the metadata contract.

When changing a measure:

1. Update `scripts/metadata/MeasureDefinitions.json`.
2. Regenerate `scripts/GenerateMeasures.csx`.
3. Validate the measure in TE2.28 if performing manual model work.
4. Run the automated build.
5. Update tests/docs when behavior changes.

## Template-first report architecture

The report template remains authoritative. The build regenerates from that template rather than reconstructing the report from a blank definition.

The build then validates:

- PBIR definition schema/version.
- Dataset path resolution.
- DataFolder portability.
- Expected relationship.
- Measure count and placement.
- Exact visual JSON inventory.
- JSON syntax and BOM absence.
- Final published-artifact integrity.

## PBIR visual integrity

Version 1 expects exactly 28 `visual.json` files in the published report artifact. Every file is parsed as JSON. This prevents malformed visual metadata from passing CI and failing later in Power BI Desktop.

The visual gate is intentionally strict. The two known 2.10.0 properties, `sortDefinition` and `columnHeaders`, must be handled by the schema compatibility definition rather than by altering the authoritative visual files or disabling additional-property validation.

## UAT boundary

Automated semantic validation cannot prove all Power BI Desktop interactions. Desktop validation remains required for:

- KPI hierarchy/field rendering.
- Slicer filtering.
- Page and visual rendering.
- Registered image/resource display.

## Development rules

- Keep generated artifacts reproducible.
- Do not commit machine-specific `DataFolder` paths.
- Preserve template-managed report resources.
- Avoid manual edits to generated `GenerateMeasures.csx`; change the JSON contract instead.
- Keep CI validation pointed at the exact artifact that will be released.
- Do not synthesize report extensions when no real extension definition exists.
- Treat the authoritative template as the compatibility reference for Desktop-emitted PBIR structures.
