# 0005 · Periods are named by the model and resolved by the server

- Status: accepted
- Date: 23 Oct 2026

## Context

Most questions about sales carry a period: "last quarter", "the last six months", "this year against last year".
A model asked to turn those into dates does the arithmetic itself, and gets it wrong exactly where nobody looks:
in the first days of a quarter, at the turn of a year, in February. In booking-agent the same lesson came from
"next Tuesday": relative dates are resolved by the server, never by the model.

## Decision

The semantic query names a period from a closed list (`NamedPeriod`: this or last month, quarter, six or twelve
months, year) or gives explicit dates, and names what it is compared with (`Comparison`: the period before, the
same period last year). `PeriodCalendar` in the domain turns the names into dates against the dataset's "today".
The JSON Schema the model answers with only allows those names.

"Last quarter" means the last complete quarter; "this quarter" runs up to today. A period of whole months compares
with whole months (February with all of January); one that ends on a month's last day still does a year earlier
(29 Feb 2024 becomes 28 Feb 2023).

## Consequences

- The same question gives the same dates every time, and the dates are tested with a fake "today".
- The model cannot express a period outside the list other than with explicit dates. That is deliberate: an odd
  period is better asked about (`clarification_needed`) than guessed.
