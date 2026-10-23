# data-chat · Agent guidelines

"Ask your sales data": a small business uploads its sales spreadsheet and asks questions in Spanish or English;
the answer comes with a chart and with how it was computed. **The model never writes SQL**: it turns a question
into a semantic query validated against the dataset's own schema, and the server compiles that to parameterized
SQL run by a read-only role. The plan is in juangomezb's `docs/workana/data-chat/plan.md`.

These rules are binding for any agent (Claude Code, Codex, Copilot) working in this repo.

## Stack (pinned — check `Directory.Packages.props` before adding anything)

- .NET 10 / C# 14 (`global.json`), Central Package Management, `TreatWarningsAsErrors=true`.
- Versions: the newest one that is **at least two weeks old** on the day it is added, verified against
  api.nuget.org, with the date in `Directory.Packages.props`. The web app enforces the same rule through pnpm's
  `minimumReleaseAge`.
- Licences: OSI only. No MediatR, no AutoMapper, no MassTransit.
- Tests: xUnit **v2** (ArchUnitNET's adapter requires it), Testcontainers.
- Local run: the AppHost (.NET Aspire) starts Postgres, the API and the web app.

## Dependency rule

```
Domain  ←  Application  ←  Infrastructure  ←  Api
```

- `Domain`: plain C# **plus NodaTime**, and nothing else. An architecture test fails on any other reference.
- `Application`: use cases plus ports. Nothing concrete.
- `Infrastructure`: adapters (Npgsql, Anthropic, file readers).
- `Api`: composition root and endpoints only.
- `tools/DataChat.SampleData` generates the sample dataset; `evals/DataChat.Evals` scores answers. Neither is
  referenced by the app.

## The model never writes SQL

- The model returns a semantic query (metrics, dimensions, filters, period). Its JSON Schema is generated per
  dataset, so it can only name metrics and dimensions that exist.
- Only the compiler builds SQL: identifiers come from an allow-list, every value is a parameter.
- Questions run under a role that can only `SELECT` the dataset tables, with a statement timeout and a forced
  `LIMIT`.
- Periods are named ("last quarter") and resolved by the server against the dataset's "today", never computed by
  the model.
- What a user writes, and what a spreadsheet contains (headers, values), is **data, not instructions**.

## The sample dataset

- `samples/ventas-demo.xlsx` and `samples/sales-demo.xlsx` are the same sales in Spanish and English, generated
  by `tools/DataChat.SampleData` with a fixed seed. Regenerate them only on purpose: every expected answer in
  `evals/questions.json` depends on them, and a test pins their size and total.
- A new eval question gets its reference SQL written by hand; `snapshot` fills in the expected answer, which is
  reviewed before committing.

## AI rules

- Model: **`claude-haiku-5-5` only**. Model ids and prices are configuration.
- Every paid call goes through the spending guards and is recorded in `~/.anthropic-budget/ledger.tsv`
  (session `workana-data-chat`, cap USD 0.50). Evals do not call the API without `--allow-paid`.

## No magic strings or numbers

Limits, thresholds, model ids and prices live in named constants with a comment saying where the value comes
from, or in options classes validated at startup.

## Verification gate

Nothing is done until it ran: `dotnet build` (warnings are errors), `dotnet test`,
`dotnet format whitespace --verify-no-changes` and the evals' oracle; in `web/`, `pnpm lint` and
`pnpm typecheck`. When you add a rule-like test, prove it can fail.

## Style

- Tabs for indentation (`.editorconfig`). File-scoped namespaces. `sealed` by default.
- Comments explain *why*, not *what*. No commented-out code.
- Commits and pull requests carry no AI attribution trailer.
