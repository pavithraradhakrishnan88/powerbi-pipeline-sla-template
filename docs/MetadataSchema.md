# Metadata Schema — Version 1.0.0

## Purpose

This document describes the metadata contract used to define the Version 1 semantic-model measures and supporting model metadata.

## Measure definition contract

The authoritative measure file is:

```text
scripts/metadata/MeasureDefinitions.json
```

Each measure definition contains fields such as:

- `MeasureID`
- `Name`
- `Table`
- `Folder`
- `DisplayOrder`
- `Expression`
- `Format`
- `Description`
- `Category`
- `KPI`
- `Hidden`
- `DataType`
- `DependsOn`
- `Template`
- `Tags`
- `Synonyms`
- `Owner`
- `Version`
- `Status`
- `LastUpdated`

The generated `scripts/GenerateMeasures.csx` script is a derived artifact for Tabular Editor 2.28 and must be regenerated when the JSON contract changes.

## Semantic-model materialization

The Version 1 automated build materializes measures inline on `Fact_Pipeline_SampleData`. It does not require a generated `_Measures.tmdl` table.

The build checks that the number of inline measures matches the number of definitions in `MeasureDefinitions.json`.

## Relationship metadata

The Version 1 category relationship is:

```text
Fact_Pipeline_SampleData[Category]
    -> Dim_Category[CategoryName]
```

with many-to-one cardinality from fact to dimension.

## DataFolder metadata

`DataFolder` is a semantic-model expression used by the fact-table partition. It must remain portable and resolve relative to the PBIP artifact context.

A CI runner's absolute filesystem path is not a valid Version 1 artifact value.

## Report metadata

The report uses PBIR definition metadata and separate `page.json`/`visual.json` files. Version 1 requires the final published artifact to contain exactly 28 visual JSON files, each valid JSON and free of UTF-8 BOMs.

## Extension guidance

When changing the contract:

1. Make the change in the source metadata.
2. Regenerate derived scripts.
3. Update model/report consumers as needed.
4. Add or update automated tests.
5. Run the complete build gate.
6. Perform Power BI Desktop UAT when report behavior changes.
