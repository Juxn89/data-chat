# Errors

Every expected failure is an RFC 9457 problem document with a stable `code`. The `type` links here.

| Code | Status | When |
|---|---|---|
| <a id="import-empty"></a>`import.empty` | 400 | No file, or an empty one |
| <a id="import-too_large"></a>`import.too_large` | 400 | The file is over the size limit (10 MB) |
| <a id="import-unsupported_format"></a>`import.unsupported_format` | 400 | Not a `.csv`, `.txt` or `.xlsx` file |
| <a id="import-unreadable"></a>`import.unreadable` | 400 | A damaged or protected workbook |
| <a id="import-no_header"></a>`import.no_header` | 400 | No row in the first 20 looks like a header with data under it |
| <a id="import-too_many_rows"></a>`import.too_many_rows` | 400 | More than 200,000 data rows |
| <a id="import-too_many_columns"></a>`import.too_many_columns` | 400 | More than 50 columns |
| <a id="dataset-not_found"></a>`dataset.not_found` | 404 | No dataset with that id |
| <a id="semantic-invalid"></a>`semantic.invalid` | 400 | A semantic layer that does not fit its dataset; each issue (`issue1`, `issue2`…) has its own code, e.g. `semantic.unknown_column`, `semantic.invalid_aggregation`, `semantic.invalid_ratio`, `semantic.duplicate_key` |
| <a id="semantic-not_confirmed"></a>`semantic.not_confirmed` | 409 | Questions wait until a person confirms the dataset's semantic layer |
| <a id="query-invalid"></a>`query.invalid` | 400 | A semantic query that does not fit the layer; each issue (`issue1`…) has its code, e.g. `query.unknown_metric`, `query.too_many_groups`, `query.compare_without_period` |
| <a id="query-invalid_filter"></a>`query.invalid_filter` | 400 | A filter on a whole-number dimension with a value that is not a whole number |
| <a id="query-timeout"></a>`query.timeout` | 503 | The question ran past the statement timeout (3 s) |
| <a id="question-invalid"></a>`question.invalid` | 400 | An empty question, or one longer than 500 characters |
| <a id="question-untranslatable"></a>`question.untranslatable` | 400 | The model's query was still invalid after one retry with the reasons |
| <a id="ai-not_configured"></a>`ai.not_configured` | 503 | No API key: questions need the model |
| <a id="ai-daily_budget"></a>`ai.daily_budget` | 503 | Today's budget for the model is spent (USD 1 by default) |
| <a id="ai-failed"></a>`ai.failed` | 503 | The model declined, was cut off, or answered with neither a query nor a question back |
