# 0002 · The model answers with a JSON Schema generated per dataset

- Status: accepted
- Date: 26 Oct 2026

## Context

The model's answer has to be a semantic query (ADR 0001) that names this dataset's metrics and dimensions. Asking
for "valid JSON" in a prompt and parsing whatever comes back invites keys that do not exist, misspelled enum
values and malformed periods, each one a retry or a confusing error.

## Decision

Every question is sent with structured outputs (`output_config.format`) and a JSON Schema built from the dataset's
confirmed layer (`QuestionSchema.For`):

- `metrics`, `dimensions`, `filters[].dimension` and `sort.by` are enums of this layer's keys; a dataset without
  dates gets `null` as the only value for grain, period and comparison.
- Periods are an enum of named periods or two `date`-formatted strings; comparisons and grains are enums.
- The answer is `{ interpretation, clarification, query }`: the model either reads the question or asks back.

What a schema cannot express (at most two groupings, a comparison needs a period, a sort by something asked for)
is checked by `SemanticQuery.Validate`; a rejected query gets one retry with the reasons, as in invoice-extractor.

## Consequences

- The model cannot name a column it has not been given: the schema rejects it before the server sees it.
- The schema changes when the layer changes, so the API compiles it again (a one-time cost per schema, cached by
  the API for 24 hours). Layers change rarely; questions are many.
- The model sees the layer's names, keys, formulas and up to 20 values per dimension, never physical columns or
  rows (a test checks the description it gets).
- First run on the evals (26 Oct 2026): 86.7 %. The failures were not wrong numbers but an extra constant column
  ("Snacks by month" also grouped by category) and two columns in another order. The server now drops groupings a
  filter keeps to one value, the scoring compares rows as sets of cells, and the prompt says so: 75/75 on the second
  run.
