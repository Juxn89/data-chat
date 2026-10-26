# 0004 · The chart is chosen from the answer's shape, not by the model

- Status: accepted
- Date: 26 Oct 2026

## Context

An answer needs a chart: a total, a series over months, branches side by side. The model could pick one, but then
the same question could draw differently from one day to the next, and a chart picked without looking at the rows
can misrepresent them: a line through categories, a pie of a comparison.

## Decision

`Charts.Choose` in the application picks the chart from the answer's columns and rows, and the API returns it with
every answer (`chart`); the web app only draws it.

| Shape of the answer | Chart |
|---|---|
| Metrics only, or one period of a time grain | `Kpi`: one figure per metric, with the compared period and the change |
| A time grain | `Line`: one line per metric |
| A time grain and a dimension with up to 8 values | `Line`: one line per value |
| A dimension, up to 30 values | `Bar`, side by side with the compared period when there is one |
| Two dimensions | `Pivot`: one down, one across |
| Anything else, or no rows | `Table` |

## Consequences

- Deterministic and tested: the same answer always draws the same way.
- The model's only say is the query; it cannot ask for a misleading chart.
- Some answers would read better another way (a stacked bar, say); the table is always there, one click away.
