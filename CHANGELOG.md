# Changelog

All notable changes to this project are documented here. The project follows semantic versioning.

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

### Fixed

- Release documentation is aligned with the current template-first build and artifact gates.
- Measure workflow documentation now points to `MeasureDefinitions.json` as the source of truth.

## Version History

| Version | Date | Description |
|---|---|---|
| 1.0.0 | 2026-08-16 | Version 1 Pipeline SLA Tracker release |
