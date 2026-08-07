# Developer Guide

This document describes the architecture, extensibility points, coding conventions, and implementation guidance for the AI Metadata Generator introduced in Phase 4.

## AI Module Architecture

The AI module layer should be implemented as composable analyzers over a shared metadata context.

Recommended components:

- MetadataContext: in-memory object graph passed across analyzers
- AnalyzerStage: ordered stage interface with deterministic input/output
- AnalyzerRegistry: central registration and execution order
- DiagnosticsSink: structured warnings/errors with object targets

## Metadata Object Model

Use a stable object model that maps directly to metadata.json:

- Document: version, timestamp, tables, relationships, diagnostics
- Table: name, kind, columns, optional description
- Column: dataType, nullable, semanticRole, optional hints
- Relationship: from, to, cardinality, confidence
- Diagnostic: code, severity, message, target

## Extending Metadata Generators

1. Add fields as optional first.
2. Preserve backward compatibility in serializers.
3. Add fixture updates and regression snapshots.
4. Add validation rules for new fields before enabling strict gating.

## Creating New AI Analyzers

1. Implement analyzer stage interface.
2. Declare stage order and dependencies.
3. Keep analyzer side effects isolated to metadata context updates.
4. Emit diagnostics instead of throwing for soft-confidence outcomes.
5. Add targeted unit tests and one integration assertion.

## Coding Conventions for AI Modules

- Deterministic output for same input data
- No hidden global state
- Prefer pure functions for scoring logic
- Use explicit confidence thresholds
- Keep diagnostics machine-readable and stable
- Use Arrange/Act/Assert tests with fixture-backed datasets