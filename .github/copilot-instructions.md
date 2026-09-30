This repository is a self-contained Power BI PBIP template, not a deployed web application.

- Work from the files in this repository first.
- Do not browse to external deployments or infer a live site unless the user explicitly asks for that validation.
- Prefer local report definitions under `pbip/`, build scripts under `build/`, and documentation under `docs/` when investigating issues.
- Avoid opening large binary assets in `pbip/Pipeline_SLA_Tracker.Report/StaticResources/RegisteredResources/` unless the task specifically requires inspecting those files.


## Microsoft Fabric Skills alignment

Use Microsoft's [skills-for-fabric](https://github.com/microsoft/skills-for-fabric) as external authoring guidance, not as a replacement for this repository's architecture.

Reference these eight Microsoft assets when relevant:
- `powerbi-report-cli/SKILL.md` — PBIR/report-authoring responsibility boundary.
- `authoring.md` — PBIR authoring workflow and validation.
- `authoring-part-03.md` — valid visual roles and validation.
- `authoring-part-04.md` — PBIR anti-patterns and schema discipline.
- `version-control.md` — branch/validate/review discipline.
- `screenshot-review.md` — rendered-output/Desktop acceptance.
- `semantic-model-authoring/SKILL.md` — semantic-model/TMDL responsibility boundary.
- `tmdl-guidelines.md` — TMDL syntax/reference guidance.

Repository-specific rules remain authoritative: the checked-in PBIP/PBIR and semantic-model templates, `MeasureDefinitions.json`, deterministic generation/materialization, artifact gates, CI workflow, and Power BI Desktop UAT.

Do not introduce new architecture, replace the existing generation workflow, or modify PBIP/PBIR/TMDL/visual/CI assets merely for Microsoft Skills alignment. For visual-role or schema questions, do not guess; use the Microsoft guidance and validate the smallest actual change required. Preserve known-good Desktop-loading behavior.
