"use client";

import { Check, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { api, ApiProblem, type Aggregation, type Column, type DimensionDto, type MetricDto, type SemanticLayer } from "@/lib/api";
import { useI18n } from "@/lib/i18n";

const aggregations: Aggregation[] = ["Sum", "Average", "Min", "Max", "Count", "CountDistinct"];

// The server's key rule (SemanticLayer.Validate): lowercase letters, digits and _, starting with a letter.
function keyFrom(name: string, taken: string[]): string {
	const folded = name
		.toLowerCase()
		.normalize("NFD")
		.replace(/[̀-ͯ]/g, "")
		.replace(/[^a-z0-9]+/g, "_")
		.replace(/^_+|_+$/g, "")
		.slice(0, 40);
	const base = /^[a-z]/.test(folded) ? folded : `m_${folded}`;
	let key = base;
	for (let n = 2; taken.includes(key); n++) {
		key = `${base.slice(0, 37)}_${n}`;
	}

	return key;
}

function fits(aggregation: Aggregation, column: Column): boolean {
	switch (aggregation) {
		case "Sum":
		case "Average":
			return column.type === "Integer" || column.type === "Decimal";
		case "Min":
		case "Max":
			return column.type !== "Text";
		default:
			return true;
	}
}

type Draft<T> = T & { isNew?: boolean };

/** The proposed semantic layer, editable: rename, change how a metric adds up, add ratios and dimensions, confirm. */
export function LayerEditor({ layer, onConfirmed }: { layer: SemanticLayer; onConfirmed: (layer: SemanticLayer) => void }) {
	const { t } = useI18n();
	const [timeColumn, setTimeColumn] = useState(layer.timeColumn);
	const [asOf, setAsOf] = useState(layer.asOf ?? "");
	const [metrics, setMetrics] = useState<Draft<MetricDto>[]>(layer.metrics);
	const [dimensions, setDimensions] = useState<Draft<DimensionDto>[]>(layer.dimensions);
	const [saving, setSaving] = useState(false);
	const [issues, setIssues] = useState<string[]>([]);

	const columns = layer.columns;
	const dateColumns = columns.filter((c) => c.type === "Date");
	const groupable = columns.filter((c) => c.type === "Text" || c.type === "Integer");
	const aggregates = metrics.filter((m) => m.aggregation);
	const keys = () => [...metrics.map((m) => m.key), ...dimensions.map((d) => d.key)];

	const updateMetric = (index: number, change: Partial<MetricDto>) =>
		setMetrics((all) =>
			all.map((metric, i) => {
				if (i !== index) {
					return metric;
				}

				const next = { ...metric, ...change };
				return metric.isNew && change.name !== undefined
					? { ...next, key: keyFrom(change.name, keys().filter((k) => k !== metric.key)) }
					: next;
			}),
		);

	const updateDimension = (index: number, change: Partial<DimensionDto>) =>
		setDimensions((all) =>
			all.map((dimension, i) => {
				if (i !== index) {
					return dimension;
				}

				const next = { ...dimension, ...change };
				return dimension.isNew && change.name !== undefined
					? { ...next, key: keyFrom(change.name, keys().filter((k) => k !== dimension.key)) }
					: next;
			}),
		);

	const confirm = async () => {
		setSaving(true);
		setIssues([]);
		try {
			const strip = <T extends { isNew?: boolean }>(draft: T) => {
				const rest = { ...draft };
				delete rest.isNew;
				return rest;
			};
			const confirmed = await api.confirm(layer.datasetId, {
				timeColumn,
				asOf: asOf || null,
				metrics: metrics.map(strip),
				dimensions: dimensions.map(strip),
			});
			onConfirmed(confirmed);
		} catch (problem) {
			setIssues(problem instanceof ApiProblem && problem.issues.length > 0 ? problem.issues.map((i) => i.replace(/^[\w.]+: /, "")) : [t.errors.unknown ?? ""]);
		} finally {
			setSaving(false);
		}
	};

	const input = "w-full rounded-md border border-line bg-card px-2.5 py-1.5 text-sm focus:border-brand focus:outline-none";
	const label = "text-xs font-semibold uppercase tracking-wide text-muted";

	return (
		<div className="grid gap-6 lg:grid-cols-[1fr_300px]">
			<div className="space-y-6">
				<div className="space-y-2">
					<div className="flex flex-wrap items-center gap-3">
						<h2 className="text-lg font-semibold">{t.layer.title}</h2>
						<span className={`rounded-full px-2 py-0.5 text-xs font-semibold ${layer.confirmed ? "bg-brand-soft text-brand-dark" : "bg-info-soft text-info"}`}>
							{layer.confirmed ? t.layer.confirmed : t.layer.proposal}
						</span>
					</div>
					<p className="max-w-3xl text-sm text-muted">{t.layer.intro}</p>
				</div>

				<div className="grid gap-4 rounded-lg border border-line bg-card p-4 sm:grid-cols-2">
					<label className="space-y-1">
						<span className={label}>{t.layer.time}</span>
						<select className={input} value={timeColumn ?? ""} onChange={(e) => setTimeColumn(e.target.value ? Number(e.target.value) : null)}>
							<option value="">{t.layer.noTime}</option>
							{dateColumns.map((c) => (
								<option key={c.ordinal} value={c.ordinal}>
									{c.name}
								</option>
							))}
						</select>
					</label>
					<label className="space-y-1">
						<span className={label}>{t.layer.asOf}</span>
						<input type="date" className={input} value={asOf} onChange={(e) => setAsOf(e.target.value)} />
						<span className="block text-xs text-muted">{t.layer.asOfHint}</span>
					</label>
				</div>

				<section className="space-y-3">
					<h3 className="font-semibold">{t.layer.metrics}</h3>
					{metrics.map((metric, index) => (
						<div key={index} className="grid items-end gap-3 rounded-lg border border-line bg-card p-3 sm:grid-cols-[1fr_1.4fr_auto]">
							<label className="space-y-1">
								<span className={label}>{t.layer.name}</span>
								<input className={input} value={metric.name} onChange={(e) => updateMetric(index, { name: e.target.value })} />
								<span className="block font-mono text-[11px] text-muted">{metric.key}</span>
							</label>
							{metric.aggregation ? (
								<div className="grid gap-2 sm:grid-cols-2">
									<label className="space-y-1">
										<span className={label}>{t.layer.formula}</span>
										<select
											className={input}
											value={metric.aggregation}
											onChange={(e) => {
												const aggregation = e.target.value as Aggregation;
												updateMetric(index, { aggregation, column: aggregation === "Count" ? null : metric.column ?? columns[0]?.ordinal ?? null });
											}}
										>
											{aggregations.map((a) => (
												<option key={a} value={a}>
													{t.layer.aggregations[a]}
												</option>
											))}
										</select>
									</label>
									{metric.aggregation !== "Count" ? (
										<label className="space-y-1">
											<span className={label}>{t.layer.column}</span>
											<select className={input} value={metric.column ?? ""} onChange={(e) => updateMetric(index, { column: Number(e.target.value) })}>
												{columns
													.filter((c) => fits(metric.aggregation!, c))
													.map((c) => (
														<option key={c.ordinal} value={c.ordinal}>
															{c.name} ({t.layer.types[c.type]})
														</option>
													))}
											</select>
										</label>
									) : null}
								</div>
							) : (
								<div className="grid items-end gap-2 sm:grid-cols-[1fr_auto_1fr]">
									<label className="space-y-1">
										<span className={label}>{t.layer.ratio}</span>
										<select className={input} value={metric.numerator ?? ""} onChange={(e) => updateMetric(index, { numerator: e.target.value })}>
											{aggregates.map((m) => (
												<option key={m.key} value={m.key}>
													{m.name}
												</option>
											))}
										</select>
									</label>
									<span className="pb-2 text-xs text-muted">{t.layer.divide}</span>
									<select className={input} value={metric.denominator ?? ""} onChange={(e) => updateMetric(index, { denominator: e.target.value })}>
										{aggregates.map((m) => (
											<option key={m.key} value={m.key}>
												{m.name}
											</option>
										))}
									</select>
								</div>
							)}
							<button
								type="button"
								aria-label={t.layer.remove}
								onClick={() => setMetrics((all) => all.filter((_, i) => i !== index))}
								className="rounded-md p-2 text-muted hover:bg-danger-soft hover:text-danger"
							>
								<Trash2 className="size-4" aria-hidden />
							</button>
						</div>
					))}
					<div className="flex flex-wrap gap-2">
						<AddButton
							label={t.layer.addMetric}
							onClick={() =>
								setMetrics((all) => [
									...all,
									{ key: keyFrom(t.layer.metrics, keys()), name: "", aggregation: "Sum", column: columns.find((c) => fits("Sum", c))?.ordinal ?? null, isNew: true },
								])
							}
						/>
						<AddButton
							label={t.layer.addRatio}
							onClick={() =>
								setMetrics((all) => [
									...all,
									{ key: keyFrom(t.layer.ratio, keys()), name: "", numerator: aggregates[0]?.key ?? "", denominator: aggregates[1]?.key ?? "", isNew: true },
								])
							}
						/>
					</div>
				</section>

				<section className="space-y-3">
					<h3 className="font-semibold">{t.layer.dimensions}</h3>
					{dimensions.map((dimension, index) => (
						<div key={index} className="grid items-end gap-3 rounded-lg border border-line bg-card p-3 sm:grid-cols-[1fr_1.4fr_auto]">
							<label className="space-y-1">
								<span className={label}>{t.layer.name}</span>
								<input className={input} value={dimension.name} onChange={(e) => updateDimension(index, { name: e.target.value })} />
								<span className="block font-mono text-[11px] text-muted">{dimension.key}</span>
							</label>
							<label className="space-y-1">
								<span className={label}>{t.layer.column}</span>
								<select className={input} value={dimension.column} onChange={(e) => updateDimension(index, { column: Number(e.target.value) })}>
									{groupable.map((c) => (
										<option key={c.ordinal} value={c.ordinal}>
											{c.name}
										</option>
									))}
								</select>
							</label>
							<button
								type="button"
								aria-label={t.layer.remove}
								onClick={() => setDimensions((all) => all.filter((_, i) => i !== index))}
								className="rounded-md p-2 text-muted hover:bg-danger-soft hover:text-danger"
							>
								<Trash2 className="size-4" aria-hidden />
							</button>
						</div>
					))}
					<AddButton
						label={t.layer.addDimension}
						onClick={() =>
							setDimensions((all) => [...all, { key: keyFrom(t.layer.dimensions, keys()), name: "", column: groupable[0]?.ordinal ?? 1, isNew: true }])
						}
					/>
				</section>

				{issues.length > 0 ? (
					<div role="alert" className="space-y-1 rounded-md bg-danger-soft px-4 py-3 text-sm text-danger">
						<p className="font-semibold">{t.layer.invalid}</p>
						<ul className="list-disc pl-5">
							{issues.map((issue) => (
								<li key={issue}>{issue}</li>
							))}
						</ul>
					</div>
				) : null}

				<button
					type="button"
					disabled={saving}
					onClick={() => void confirm()}
					className="inline-flex items-center gap-2 rounded-md bg-brand px-5 py-2.5 text-sm font-semibold text-white hover:bg-brand-dark disabled:opacity-60"
				>
					<Check className="size-4" aria-hidden />
					{saving ? t.layer.saving : t.layer.confirm}
				</button>
			</div>

			<aside className="space-y-3">
				<h3 className={label}>{t.layer.samples}</h3>
				<ul className="space-y-2">
					{columns.map((column) => (
						<li key={column.ordinal} className="rounded-lg border border-line bg-card p-3">
							<p className="text-sm font-semibold">
								{column.name} <span className="font-normal text-muted">· {t.layer.types[column.type]}</span>
							</p>
							<p className="text-xs text-muted">{t.layer.values(column.distinctCount)}</p>
							<p className="mt-1 line-clamp-2 text-xs text-muted">{column.samples.slice(0, 6).join(" · ")}</p>
						</li>
					))}
				</ul>
			</aside>
		</div>
	);
}

function AddButton({ label, onClick }: { label: string; onClick: () => void }) {
	return (
		<button
			type="button"
			onClick={onClick}
			className="inline-flex items-center gap-1.5 rounded-md border border-dashed border-line px-3 py-1.5 text-sm font-medium text-muted hover:border-brand hover:text-brand"
		>
			<Plus className="size-4" aria-hidden />
			{label}
		</button>
	);
}
