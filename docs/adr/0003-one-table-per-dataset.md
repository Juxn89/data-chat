# 0003 · One physical table per dataset, with columns named by position

- Status: accepted
- Date: 26 Oct 2026

## Context

Every uploaded spreadsheet has its own columns. They could be stored as rows of key-value pairs (EAV), as one
JSONB document per row, or as a table of their own. The questions are aggregations over tens of thousands of rows
(sums by month, by branch), and the names of the columns come from a file anybody can write.

## Decision

Each import creates a table `ds_<id>` with typed columns (`date`, `bigint`, `numeric`, `text`) named `c_001`,
`c_002`… by position. The header's text is kept only as the column's display name, in `dataset_columns`. The
table, its rows (binary `COPY`), an index per date column and the description are written in one transaction.

## Consequences

- Aggregations are plain SQL over typed columns, indexed by date: no casts from JSON, no pivots from EAV.
- A header like `monto"); DROP TABLE datasets; --` is a name people see, never an identifier: the compiler only
  emits `ds_<id>` and `c_nnn`, which the domain builds. A test imports exactly that header.
- A failed import leaves nothing behind.
- The read-only role that runs the questions (day 4) is granted `SELECT` on these tables only.
- Many datasets mean many tables. For the MVP's scale that is fine; a tenant per schema would be the next step.
