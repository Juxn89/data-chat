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
