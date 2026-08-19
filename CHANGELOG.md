# Changelog

All notable changes to this project are documented here. The project follows semantic versioning.

## [Unreleased] - 2026-08-19

### Stabilized / Closed

- Restored the authoritative semantic-model template artifacts required for reliable Power BI Desktop loading, including the localization/date-table and platform metadata files.
- Corrected artifact preparation so the report and semantic-model `.platform` files are preserved in the PBIP build artifact.
- Standardized report generation/testing on the actual authoritative report template instead of a reduced synthetic test fixture.
- Preserved the exact 28/28 visual inventory and continued parsing every published `visual.json` as JSON.
- Kept the final artifact gate strict and pointed at the exact package that is published.
- Removed unconditional synthetic `reportExtensions.json` generation from the normal generated-report path.
- Removed `reportExtensions.json` from unconditional generated-output required-file validation.
- Preserved `WriteFromTemplate()` as a true template copier so a real future extension definition is copied only when it exists in the authoritative template.
- Closed the Power BI Desktop `ModelAuthoringHostService.UpdateModelExtensions` failures caused by fake/empty extension artifacts; current UAT reaches the usable report state with visuals loading and report interactions working.

### Remaining

- The only known remaining validation issue is schema compatibility for the July 2026 Power BI Desktop `visualContainer/2.10.0` format. The authoritative template legitimately contains `visual.sortDefinition` and `visual.visualContainerObjects.columnHeaders`, while the analyzer reports them as additional properties.
- The required follow-up is limited to the analyzer/schema compatibility definition: explicitly allow those two properties for 2.10.0 while retaining strict `additionalProperties: false` everywhere else.
- The two authoritative `visual.json` files, their `$schema`, the visual tree, and the 28/28 production gate must not be changed to suppress these warnings.

## [1.0.0] - 2026-08-16

### Added

- Version 1 template-first PBIP/PBIR Pipeline SLA Tracker.
- Authoritative `scripts/metadata/MeasureDefinitions.json` measure contract.
- Tabular Editor 2.28-compatible `scripts/GenerateMeasures.csx` workflow.
- Automated inline measure materialization during the repository build.
- Portable `DataFolder` resolution for the PBIP artifact.
- PBIR schema/version and dataset-path validation.
- Hard 28/28 published visual JSON gate with JSON parsing and BOM validation.
- Final artifact validation against the exact package intended for publication.
- Power BI Desktop UAT guidance covering KPI hierarchy, slicers, visual rendering, and registered resources.
- Current documentation for configuration, development, metadata, testing, architecture, getting started, and user operation.

### Changed

- Documentation now reflects the current three-page report: Home, Executive Overview, and SLA Exceptions.
- Documentation no longer describes the obsolete `reports/PipelineDashboard.pbip` path.
- Documentation now distinguishes the manual Tabular Editor workflow from the deterministic automated CI build.
- Release guidance now requires Desktop validation in addition to semantic CI validation.
- The successful Version 1 baseline is documented as the PR #11 resolved integration that passed Validate and Build before merge to `main`.
- The baseline records restoration of the known-good floating-bar visual template, the PR #10 Fact template, the DataFolder-backed Fact partition, and the required Floating Bar DAX contracts.
- The baseline records removal of the stale `Breached` SLA-domain assertion, semantic Floating Bar UAT validation, published-artifact semantic validation, recursive visual preservation, and local Desktop parameter preparation.

### Fixed

- Release documentation is aligned with the current template-first build and artifact gates.
- Measure workflow documentation now points to `MeasureDefinitions.json` as the source of truth.
- Documentation explicitly records that PR #12's relationship-validator experiment was closed without merge and is **not part of the successful Version 1 baseline**.

## Version History

| Version | Date | Description |
|---|---|---|
| 1.0.0 | 2026-08-16 | Version 1 Pipeline SLA Tracker release |
