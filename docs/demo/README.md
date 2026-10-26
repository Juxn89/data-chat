# Demo

`demo.mp4` is 85 seconds, 1920×1080. A distributor uploads its sales spreadsheet, confirms how to read it and asks
in Spanish. It was recorded on 26 Oct 2026 against Claude Haiku 5.5, as is: the interpretations and the summaries are
the model's.

| File | Shows |
|---|---|
| `demo.mp4` | The whole flow: the upload, the proposed metrics and dimensions, a ranking by branch, the SQL the server wrote, a monthly trend, a follow-up narrowed to one branch, a comparison with last year, and the history with each question's cost |
| `poster.jpg` | The video's poster, taken from it: the first answer, with its checked summary and its bar chart |
| `01-inicio.png` | The home page: the datasets and the upload |
| `02-capa-semantica.png` | The proposed layer: "Total" renamed to "Ventas", before confirming |
| `03-ranking.png` | "¿Cuánto vendió cada sucursal el último trimestre?": what was understood, the summary and the bars |
| `04-sql.png` | The same answer with its SQL: written by the server, values as parameters, read-only |
| `05-tendencia.png` | Sales by month over the last 12 months, as a line |
| `06-comparacion.png` | Sales by category, last quarter against the same quarter last year |
| `07-historial.png` | The history: four questions answered, USD 0.0006 on average, today's spend against the cap |

## Recording it again

```bash
cd web
pnpm e2e:deps                     # Postgres (5434) for the run
DEMO_SCRIPTED=1 pnpm demo         # rehearsal with a script: free
pnpm demo                         # the real take: ANTHROPIC_API_KEY from the environment or .env, ~USD 0.0024
```

The script (`web/e2e/demo/demo.spec.ts`) drives one browser page and records it with Chromium's screencast. ffmpeg,
in a pinned container image with no network, turns the frames into the MP4, so nothing is installed on the host. Each
run uses its own database. The reference date is set to 15 Oct 2026 in the layer, so "el último trimestre" is the
third quarter of 2026, as in the evals.
