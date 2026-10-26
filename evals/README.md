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
- Each question also has its **gold query**: the semantic query a perfect translation gives, written by hand. The
  oracle runs it through the app itself (import of the sample, its confirmed layer, the compiler, the read-only
  role) and it must give the expected answer too. That checks the compiler against SQL written independently, on
  every shape the questions need, and it is what a model's translation is diagnosed against from day 5.
- Some expected answers are computed a second time with LINQ over the generator's lines
  (`tests/DataChat.SampleData.Tests/ExpectedAnswersTests.cs`), so a wrong range or filter in a reference SQL does
  not go unnoticed.

## Commands

```bash
dotnet run --project evals/DataChat.Evals -- oracle     # reference SQL and gold queries give every expected answer (CI)
dotnet run --project evals/DataChat.Evals -- snapshot   # fills in the answer of new questions; flags a LIMIT that cuts a tie
dotnet run --project evals/DataChat.Evals -- run --allow-paid [--only q01,en03]   # Claude Haiku through the app (costs money)
```

All three start a throwaway Postgres with Testcontainers, so Docker has to be running. `oracle` runs the whole chat
path with a translator that returns each question's gold query, so CI covers it without a key. `run` reads the key
from the environment or the repo's `.env`, checks the shared ledger first and stops before the session's cap
(`workana-data-chat`, USD 0.50), appends one row with what it spent, and writes `results/<model>.json`.

## Scoring

An answer is correct when its rows hold the expected values: rows and columns in any order, since "sales by branch
and category" is right however it is laid out, but a top-N must keep the same rows. `sameAsGold` reports how often
the model's query is exactly the hand-written one; it is a diagnosis, not the score, because two different queries
can answer the same question.

## Results

| Run | Accuracy (ES / EN) | Same query as gold | Average cost | Average latency |
|---|---|---|---|---|
| [First run](results/claude-haiku-5-5.first-run.json), 26 Oct 2026 | 86.7 % (88.3 % / 80.0 %) | 80.0 % | USD 0.00045 | 1.7 s |
| [Current](results/claude-haiku-5-5.json), 26 Oct 2026 | **100 % (75/75)** | 90.7 % | USD 0.00047 | 1.7 s |

The first run's ten misses had right numbers in the wrong shape: eight grouped by a dimension the question had
already filtered to one value ("Snacks by month" with a constant Snacks column), two laid out the same columns in
another order. English questions also got Spanish interpretations. Three changes, none of them to the questions'
answers: the server drops a grouping a filter keeps to one value, scoring compares rows as sets of cells, and the
prompt states both rules and the interpretation's language. One question (q14 and its twin) was reworded because it
was genuinely ambiguous: "Bebidas y Lácteos por sucursal" can mean their sum per branch.

## Status

| Set | Questions | Since |
|---|---|---|
| Spanish, with expected answers | 60 of 60 | days 1-2 (23 and 26 Oct 2026) |
| English, on the English dataset | 15 of 15, each the twin of a Spanish one with the same figures (a test checks it) | day 2 (26 Oct 2026) |
| Ambiguous (should ask back), follow-ups, adversarial | 0 | day 6 |
