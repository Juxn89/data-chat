"use client";

import { ArrowDownRight, ArrowUpRight } from "lucide-react";
import { Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { ResultTable } from "@/components/ResultTable";
import type { Cell, QueryAnswer, ResultColumn } from "@/lib/api";
import { change, formatCell, formatNumber } from "@/lib/format";
import { useI18n, type Locale } from "@/lib/i18n";

// Categorical slots in fixed order (globals.css); the compared period is a neutral, never a series.
const series = Array.from({ length: 8 }, (_, i) => `var(--chart-series-${i + 1})`);
const compared = "var(--chart-compared)";
const axis = { fontSize: 12, fill: "var(--chart-axis)" };

type Indexed = ResultColumn & { index: number };

function columnsOf(answer: QueryAnswer) {
	const indexed: Indexed[] = answer.columns.map((column, index) => ({ ...column, index }));
	return {
		time: indexed.find((c) => c.kind === "Time"),
		dimensions: indexed.filter((c) => c.kind === "Dimension"),
		metrics: indexed.filter((c) => c.kind === "Metric"),
		previous: (metric: Indexed) => indexed.find((c) => c.kind === "PreviousMetric" && c.key === metric.key),
	};
}

/** Draws the chart the API chose for the answer's shape (ADR 0004). */
export function AnswerChart({ answer }: { answer: QueryAnswer }) {
	switch (answer.chart) {
		case "Kpi":
			return <Kpis answer={answer} />;
		case "Line":
			return <Lines answer={answer} />;
		case "Bar":
			return <Bars answer={answer} />;
		case "Pivot":
			return <Pivot answer={answer} />;
		default:
			return <ResultTable answer={answer} />;
	}
}

function Kpis({ answer }: { answer: QueryAnswer }) {
	const { locale, t } = useI18n();
	const { metrics, previous } = columnsOf(answer);
	const row = answer.rows[0] ?? [];

	return (
		<div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
			{metrics.map((metric) => {
				const value = row[metric.index] ?? null;
				const before = previous(metric);
				const old = before ? (row[before.index] ?? null) : null;
				const delta = before ? change(value, old) : null;
				return (
					<div key={metric.index} className="rounded-lg border border-line p-4">
						<p className="text-sm text-muted">{metric.name}</p>
						<p className="tabular mt-1 text-3xl font-bold tracking-tight">{value === null ? t.chat.noData : formatCell(value, locale)}</p>
						{before ? (
							<p className="tabular mt-2 flex flex-wrap items-center gap-x-2 text-sm text-muted">
								<span>
									{t.chat.previous}: {formatCell(old, locale)}
								</span>
								{delta !== null ? <Delta value={delta} locale={locale} /> : null}
							</p>
						) : null}
					</div>
				);
			})}
		</div>
	);
}

/** A change with its sign, an arrow and a colour: never colour alone. */
function Delta({ value, locale }: { value: number; locale: Locale }) {
	const up = value >= 0;
	const Icon = up ? ArrowUpRight : ArrowDownRight;
	return (
		<span className={`inline-flex items-center gap-0.5 font-semibold ${up ? "text-up" : "text-down"}`}>
			<Icon className="size-4" aria-hidden />
			{up ? "+" : "−"}
			{formatNumber(Math.abs(value), locale, 1)} %
		</span>
	);
}

function Lines({ answer }: { answer: QueryAnswer }) {
	const { locale } = useI18n();
	const { time, dimensions, metrics } = columnsOf(answer);
	const dimension = dimensions[0];

	// One small chart per metric: two metrics never share an axis.
	return (
		<div className={`grid gap-6 ${metrics.length > 1 ? "lg:grid-cols-2" : ""}`}>
			{metrics.map((metric) => {
				const labels = unique(answer.rows.map((row) => String(row[time!.index])));
				const names = dimension ? unique(answer.rows.map((row) => String(row[dimension.index]))) : [metric.name];
				const data = labels.map((label) => {
					const point: Record<string, string | number | null> = { label };
					for (const row of answer.rows.filter((r) => String(r[time!.index]) === label)) {
						point[dimension ? String(row[dimension.index]) : metric.name] = numeric(row[metric.index] ?? null);
					}

					return point;
				});

				return (
					<figure key={metric.index} className="space-y-2">
						{metrics.length > 1 || dimension ? <figcaption className="text-sm font-semibold">{metric.name}</figcaption> : null}
						<ResponsiveContainer width="100%" height={280}>
							<LineChart data={data} margin={{ top: 8, right: 16, bottom: 0, left: 8 }}>
								<CartesianGrid stroke="var(--chart-grid)" vertical={false} />
								<XAxis dataKey="label" tick={axis} tickLine={false} axisLine={{ stroke: "var(--chart-grid)" }} />
								<YAxis tick={axis} tickLine={false} axisLine={false} width={72} tickFormatter={(v: number) => formatNumber(v, locale, 0)} />
								<Tooltip formatter={(value) => formatCell(numeric(value as Cell), locale)} />
								{names.length > 1 ? <Legend iconType="plainline" wrapperStyle={{ fontSize: 12 }} /> : null}
								{names.map((name, i) => (
									<Line
										key={name}
										dataKey={name}
										stroke={series[i % series.length]}
										strokeWidth={2}
										dot={{ r: 3, strokeWidth: 2, fill: "var(--chart-surface)" }}
										activeDot={{ r: 5 }}
										connectNulls
									/>
								))}
							</LineChart>
						</ResponsiveContainer>
					</figure>
				);
			})}
		</div>
	);
}

function Bars({ answer }: { answer: QueryAnswer }) {
	const { locale, t } = useI18n();
	const { dimensions, metrics, previous } = columnsOf(answer);
	const dimension = dimensions[0]!;
	const height = Math.max(160, answer.rows.length * (previous(metrics[0]!) ? 44 : 30) + 40);

	return (
		<div className={`grid gap-6 ${metrics.length > 1 ? "lg:grid-cols-2" : ""}`}>
			{metrics.map((metric) => {
				const before = previous(metric);
				const data = answer.rows.map((row) => ({
					label: String(row[dimension.index]),
					[metric.name]: numeric(row[metric.index] ?? null),
					...(before ? { [`${metric.name} (${t.chat.previous})`]: numeric(row[before.index] ?? null) } : {}),
				}));

				return (
					<figure key={metric.index} className="space-y-2">
						{metrics.length > 1 ? <figcaption className="text-sm font-semibold">{metric.name}</figcaption> : null}
						<ResponsiveContainer width="100%" height={height}>
							<BarChart data={data} layout="vertical" margin={{ top: 4, right: 24, bottom: 0, left: 8 }} barGap={2}>
								<CartesianGrid stroke="var(--chart-grid)" horizontal={false} />
								<XAxis type="number" tick={axis} tickLine={false} axisLine={false} tickFormatter={(v: number) => formatNumber(v, locale, 0)} />
								<YAxis type="category" dataKey="label" tick={axis} tickLine={false} axisLine={false} width={150} />
								<Tooltip cursor={{ fill: "rgba(0,0,0,0.04)" }} formatter={(value) => formatCell(numeric(value as Cell), locale)} />
								{before ? <Legend wrapperStyle={{ fontSize: 12 }} /> : null}
								<Bar dataKey={metric.name} fill={series[0]} radius={[0, 4, 4, 0]} maxBarSize={22} />
								{before ? <Bar dataKey={`${metric.name} (${t.chat.previous})`} fill={compared} radius={[0, 4, 4, 0]} maxBarSize={22} /> : null}
							</BarChart>
						</ResponsiveContainer>
					</figure>
				);
			})}
		</div>
	);
}

function Pivot({ answer }: { answer: QueryAnswer }) {
	const { locale } = useI18n();
	const { dimensions, metrics } = columnsOf(answer);
	const [down, across] = [dimensions[0]!, dimensions[1]!];
	const metric = metrics[0]!;
	const rows = unique(answer.rows.map((row) => String(row[down.index])));
	const columns = unique(answer.rows.map((row) => String(row[across.index])));
	const value = (r: string, c: string) =>
		answer.rows.find((row) => String(row[down.index]) === r && String(row[across.index]) === c)?.[metric.index] ?? null;

	return (
		<div className="space-y-2">
			<p className="text-sm font-semibold">{metric.name}</p>
			<div className="overflow-x-auto rounded-md border border-line">
				<table className="w-full text-sm">
					<thead className="bg-paper text-left text-xs uppercase tracking-wide text-muted">
						<tr>
							<th scope="col" className="px-3 py-2 font-semibold">
								{down.name} / {across.name}
							</th>
							{columns.map((c) => (
								<th key={c} scope="col" className="px-3 py-2 text-right font-semibold">
									{c}
								</th>
							))}
						</tr>
					</thead>
					<tbody>
						{rows.map((r) => (
							<tr key={r} className="border-t border-line">
								<th scope="row" className="px-3 py-1.5 text-left font-medium">
									{r}
								</th>
								{columns.map((c) => (
									<td key={c} className="tabular px-3 py-1.5 text-right">
										{formatCell(value(r, c), locale)}
									</td>
								))}
							</tr>
						))}
					</tbody>
				</table>
			</div>
		</div>
	);
}

function unique(values: string[]): string[] {
	return [...new Set(values)];
}

function numeric(value: Cell): number | null {
	return value === null ? null : Number(value);
}
