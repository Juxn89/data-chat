# Evals

How well the app answers questions about the sample dataset, measured rather than promised (plan §5).

## The questions

`questions.json` holds each question with a **reference SQL** written by hand against a reference table, and the
answer that SQL gives on the sample. The sample's "today" is 15 Oct 2026; its sales run from 1 Apr 2025 to 30 Sep
2026, so "last quarter" is Q3 2026.

- The reference tables are `sales_es` and `sales_en`: the generator's lines loaded straight into Postgres, with
  plain English columns (`date, invoice, branch, category, product, units, unit_price, total`). `{sales}` in a
  question's SQL is the table in the question's language.
- Time labels follow the app's formats: months `2026-09`, quarters `2026-Q3`, years `2026`, days `2026-09-30`.
- Answers are compared by value, row by row; column names are for people.
- Some expected answers are computed a second time with LINQ over the generator's lines
  (`tests/DataChat.SampleData.Tests/ExpectedAnswersTests.cs`), so a wrong range or filter in a reference SQL does
  not go unnoticed.

## Commands

```bash
dotnet run --project evals/DataChat.Evals -- oracle     # the reference SQL still gives every expected answer (CI)
dotnet run --project evals/DataChat.Evals -- snapshot   # fills in the answer of new questions; flags a LIMIT that cuts a tie
```

Both start a throwaway Postgres with Testcontainers, so Docker has to be running. Neither calls a model.

## Status

| Set | Questions | Since |
|---|---|---|
| Spanish, with expected answers | 60 of 60 | days 1-2 (23 and 26 Oct 2026) |
| English, on the English dataset | 15 of 15, each the twin of a Spanish one with the same figures (a test checks it) | day 2 (26 Oct 2026) |
| Ambiguous (should ask back), follow-ups, adversarial | 0 | days 5-6 |
