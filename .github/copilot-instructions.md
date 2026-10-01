This repository is a self-contained Power BI PBIP template, not a deployed web application.

- Work from the files in this repository first.
- Do not browse to external deployments or infer a live site unless the user explicitly asks for that validation.
- Prefer local report definitions under `pbip/`, build scripts under `build/`, and documentation under `docs/` when investigating issues.
- Avoid opening large binary assets in `pbip/Pipeline_SLA_Tracker.Report/StaticResources/RegisteredResources/` unless the task specifically requires inspecting those files.

## Power BI Report Authoring

For PBIR/PBIP report-layer work, use Microsoft's Power BI Report Authoring skill and its report-authoring guidance.

- Skill: `skills/powerbi-report-cli/SKILL.md`
- Authoring guidance: `skills/powerbi-report-cli/references/authoring.md`
- CLI reference: `skills/powerbi-report-cli/references/authoring/powerbi-report-author-cli.md`
- Workflows/examples: `skills/powerbi-report-cli/references/authoring/authoring-workflows.md`

Use this guidance only for report-layer work: pages, visuals, visual queries/roles, filters, slicers, interactions, navigation, formatting, themes, and report validation.

Authoring rules:
1. Inspect the existing PBIR before modifying it.
2. Do not guess PBIR JSON, visual roles, properties, selectors, or enum values.
3. Use the Power BI report-authoring CLI metadata as the source of truth for PBIR authoring.
4. Make the smallest surgical change required.
5. Preserve the existing semantic model, TMDL, measures, relationships, data paths, CI gates, and stabilized Desktop-loading fixes unless explicitly required by the task.
6. Do not regenerate the PBIP from scratch when modifying an existing report.
7. Validate the affected report artifacts after a logical PBIR change and verify the result in Power BI Desktop when rendering/activation behavior is involved.

Project boundary:
`Existing PBIP → inspect PBIR → surgical PBIR change → validate → Desktop verification`

Do not introduce a new generator, PBIP architecture, semantic-model rebuild, or report-generation pipeline unless explicitly requested.
