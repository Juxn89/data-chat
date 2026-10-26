# 0001 · The model returns a semantic query; the server writes the SQL

- Status: accepted
- Date: 26 Oct 2026

## Context

"Ask your data in plain language" is usually built as text-to-SQL: the model sees the schema and writes the
query. That has two problems a small business cannot see coming:

- **Security.** The SQL is written from text anybody can type, and from a spreadsheet anybody can upload. A
  prompt injection in a question, or in a column header, ends up in the query.
- **Silent wrong answers.** A join on the wrong key, a `SUM` over a column that does not add up, a date range off
  by a day: the query runs and returns a confident, wrong number.

## Decision

The model never writes SQL. It translates a question into a **semantic query** (`SemanticQuery`): metrics,
dimensions, filters, a time grain, a named period or two dates, a comparison, a sort and a limit, all by **key**
from the dataset's **semantic layer**. The layer says what each metric is (`SUM` of a column, a count of distinct
invoices, a ratio of two metrics) and which columns are dimensions; a person confirms it after the import.

1. The JSON Schema the model answers with is generated per dataset, so its enums only allow the layer's keys (day 5).
2. `SemanticQuery.Validate` checks the query against the layer: the metrics and dimensions exist, at most two
   groupings, a comparison has a period, a sort is by something asked for, the limit is bounded.
3. The compiler (day 4) emits identifiers only from the layer (`ds_<id>`, `c_nnn`) and passes every value as a
   parameter.
4. The SQL runs under a role that can only read the dataset tables, with a statement timeout and a forced `LIMIT`.

## Consequences

- The worst a manipulated question can produce is another valid query over the same data. The adversarial
  evals (day 6) measure that.
- Wrong answers come from a wrong translation, not from wrong SQL, and the translation is visible: each answer
  shows how it was read ("I added Sales, grouped by Branch, last quarter") and the SQL.
- Less expressive than SQL: one table per dataset, no arbitrary joins, aggregates the layer defines. Questions
  outside that are better asked back (`clarification_needed`) than answered badly.
- Ratios are computed over totals (sales over invoices), not as an average of per-row ratios, which is what an
  "average ticket" means.
