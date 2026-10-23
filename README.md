# data-chat

Ask your sales spreadsheet questions in plain Spanish or English and get the figure, a chart and how it was
computed. The model never writes SQL: it returns a semantic query validated against the dataset's own schema, and
the server compiles that to parameterized SQL run by a read-only role.

Work in progress (day 1 of 10). The full README, with the measured results, comes with the last day.

```bash
dotnet run --project src/DataChat.AppHost   # Postgres, the API and the web app on http://localhost:3030
dotnet test                                 # needs Docker (Testcontainers)
dotnet run --project evals/DataChat.Evals -- oracle
```

MIT licence.
