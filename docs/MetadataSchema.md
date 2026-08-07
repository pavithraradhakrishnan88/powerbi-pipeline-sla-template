# Metadata Schema

## Purpose

This document defines the Phase 4 metadata contract used by the AI Metadata Generator. The goal is to provide a stable, testable schema that downstream modules (model generation, relationship inference, report generation, and validation) can consume without ambiguity.

## Metadata Schema Overview

The metadata document is a JSON object that includes:

- Document-level context (version, generation timestamp)
- Table definitions with column metadata
- Relationship definitions with confidence scores
- Optional diagnostics generated during enrichment and validation

Recommended top-level shape:

- version
- generatedAtUtc
- tables
- relationships
- diagnostics

## Metadata Object Descriptions

### Document

- version: Semantic version of the metadata schema
- generatedAtUtc: UTC timestamp for traceability
- tables: Array of table objects
- relationships: Array of relationship objects
- diagnostics: Array of validation or inference notes

### Table

- name: Logical table name
- kind: Table classification such as fact or dimension
- description: Optional text summary
- columns: Array of column objects

### Column

- name: Column name
- dataType: Normalized data type (string, int64, decimal, dateTime, boolean)
- nullable: True when missing values are allowed
- semanticRole: Optional role hint (identifier, measure, attribute, date)
- formatHint: Optional display format hint
- isPrimaryKeyCandidate: Optional key candidate flag
- isForeignKeyCandidate: Optional foreign key candidate flag

### Relationship

- from: Source column in Table.Column format
- to: Target column in Table.Column format
- cardinality: Relationship cardinality (for example manyToOne)
- confidence: Numeric score between 0 and 1

### Diagnostic

- code: Stable diagnostic identifier
- severity: info, warning, or error
- message: Human-readable detail
- target: Optional metadata path or object reference

## Relationships

Relationship records should point to fully qualified column references and include confidence for observability. A relationship is valid when:

- Source and target references resolve to existing columns.
- Cardinality value is in the allowed set.
- Confidence is within [0, 1].

Low-confidence relationships should remain visible as diagnostics even when excluded from final model generation.

## Example metadata.json

```json
{
  "version": "1.0",
  "generatedAtUtc": "2026-08-07T00:00:00Z",
  "tables": [
    {
      "name": "Fact_Pipeline_SampleData",
      "kind": "fact",
      "description": "Pipeline execution events",
      "columns": [
        {
          "name": "PipelineID",
          "dataType": "string",
          "nullable": false,
          "semanticRole": "identifier",
          "isPrimaryKeyCandidate": false,
          "isForeignKeyCandidate": false
        },
        {
          "name": "CategoryId",
          "dataType": "int64",
          "nullable": false,
          "semanticRole": "identifier",
          "isPrimaryKeyCandidate": false,
          "isForeignKeyCandidate": true
        },
        {
          "name": "DurationHours",
          "dataType": "decimal",
          "nullable": false,
          "semanticRole": "measure",
          "formatHint": "0.00"
        }
      ]
    },
    {
      "name": "Dim_Category",
      "kind": "dimension",
      "columns": [
        {
          "name": "CategoryId",
          "dataType": "int64",
          "nullable": false,
          "semanticRole": "identifier",
          "isPrimaryKeyCandidate": true
        },
        {
          "name": "CategoryName",
          "dataType": "string",
          "nullable": false,
          "semanticRole": "attribute"
        }
      ]
    }
  ],
  "relationships": [
    {
      "from": "Fact_Pipeline_SampleData.CategoryId",
      "to": "Dim_Category.CategoryId",
      "cardinality": "manyToOne",
      "confidence": 0.98
    }
  ],
  "diagnostics": []
}
```

## Extension Guidance

When extending the schema:

- Add optional fields first; avoid removing or renaming existing fields.
- Version the schema when introducing behavior-affecting changes.
- Keep type normalization stable to prevent downstream generator churn.
- Add fixture updates and snapshot tests for every schema extension.
- Document new fields with expected defaults and validation rules.
