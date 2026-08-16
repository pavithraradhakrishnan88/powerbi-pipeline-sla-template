# Measure Script Workflow — Version 1.0.0

`MeasureDefinitions.json` is the source of truth for Version 1 measures.

## Files

```text
scripts/metadata/MeasureDefinitions.json  # authoritative definitions
scripts/tools/GenerateMetadata.ps1        # generator
scripts/GenerateMeasures.csx              # generated TE2.28 script
```

## Generate

```powershell
.\scripts\tools\GenerateMetadata.ps1
```

## Run in Tabular Editor 2.28

Open the Version 1 semantic model in Tabular Editor 2.28 and execute:

```text
scripts/GenerateMeasures.csx
```

The script creates/updates the defined measures, folders, formats, and annotations.

## Automated build

The repository build does not require Tabular Editor on the CI runner. `build/build.ps1` materializes the authoritative measure set directly and validates the resulting inline measures against `MeasureDefinitions.json`.

If a measure changes, edit `MeasureDefinitions.json`, regenerate the script, and rerun the build. Do not hand-edit the generated script as the source of truth.
