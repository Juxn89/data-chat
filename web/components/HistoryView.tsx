"use client";

import { CheckCircle2, CircleHelp, MessageSquareWarning, ThumbsUp, TriangleAlert } from "lucide-react";
import { useEffect, useState } from "react";
import { api, type History, type Outcome } from "@/lib/api";
import { formatNumber } from "@/lib/format";
import { useI18n } from "@/lib/i18n";

const outcomeStyle: Record<Outcome, { icon: typeof CheckCircle2; className: string }> = {
	Answered: { icon: CheckCircle2, className: "bg-brand-soft text-brand-dark" },
	Clarified: { icon: CircleHelp, className: "bg-info-soft text-info" },
	Failed: { icon: TriangleAlert, className: "bg-danger-soft text-danger" },
};

/** What has been asked of a dataset: totals, today's AI spend against the cap, and every question with its outcome. */
export function HistoryView({ datasetId }: { datasetId: string }) {
	const { locale, t } = useI18n();
	const [history, setHistory] = useState<History | null>(null);

	useEffect(() => {
		let active = true;
		void api.history(datasetId).then((h) => active && setHistory(h));
		return () => {
			active = false;
		};
	}, [datasetId]);

	if (!history) {
		return <p className="text-muted">{t.dataset.loading}</p>;
	}

	const items = history.items;
	const count = (outcome: Outcome) => items.filter((q) => q.outcome === outcome).length;
	const average = items.length > 0 ? items.reduce((sum, q) => sum + q.costUsd, 0) / items.length : 0;
	const share = Math.min(1, history.dailyBudgetUsd > 0 ? history.spentTodayUsd / history.dailyBudgetUsd : 0);
	const time = new Intl.DateTimeFormat(locale === "es" ? "es-NI" : "en-US", { dateStyle: "short", timeStyle: "short" });

	const tile = (label: string, value: string) => (
		<div className="rounded-lg border border-line bg-card p-4">
			<p className="text-xs text-muted">{label}</p>
			<p className="tabular mt-1 text-2xl font-bold tracking-tight">{value}</p>
		</div>
	);

	return (
		<div className="space-y-6">
			<h2 className="text-lg font-semibold">{t.history.title}</h2>

			<div className="grid gap-3 sm:grid-cols-3 lg:grid-cols-6">
				{tile(t.history.questions, String(items.length))}
				{tile(t.history.answered, String(count("Answered")))}
				{tile(t.history.askedBack, String(count("Clarified")))}
				{tile(t.history.failed, String(count("Failed")))}
				{tile(t.history.averageCost, `USD ${average.toFixed(5)}`)}
				{tile(t.history.wrong, String(items.filter((q) => q.feedback === "Wrong").length))}
			</div>

			<div className="space-y-2 rounded-lg border border-line bg-card p-4">
				<div className="flex flex-wrap items-baseline justify-between gap-2">
					<p className="text-sm font-semibold">{t.history.budget}</p>
					<p className="tabular text-sm text-muted">
						{t.history.budgetOf(formatNumber(history.spentTodayUsd, locale, 4), formatNumber(history.dailyBudgetUsd, locale, 2))}
					</p>
				</div>
				<div
					role="meter"
					aria-label={t.history.budget}
					aria-valuemin={0}
					aria-valuemax={history.dailyBudgetUsd}
					aria-valuenow={history.spentTodayUsd}
					className="h-2 overflow-hidden rounded-full bg-paper"
				>
					<div className="h-full rounded-full bg-brand" style={{ width: `${Math.max(share * 100, share > 0 ? 1 : 0)}%` }} />
				</div>
				<p className="text-xs text-muted">{t.history.budgetHint}</p>
			</div>

			{items.length === 0 ? (
				<p className="text-muted">{t.history.empty}</p>
			) : (
				<div className="overflow-x-auto rounded-lg border border-line bg-card">
					<table className="w-full text-sm">
						<thead className="bg-paper text-left text-xs uppercase tracking-wide text-muted">
							<tr>
								<th scope="col" className="px-3 py-2 font-semibold">{t.history.when}</th>
								<th scope="col" className="px-3 py-2 font-semibold">{t.history.question}</th>
								<th scope="col" className="px-3 py-2 font-semibold">{t.history.outcome}</th>
								<th scope="col" className="px-3 py-2 text-right font-semibold">{t.history.rows}</th>
								<th scope="col" className="px-3 py-2 text-right font-semibold">{t.history.cost}</th>
								<th scope="col" className="px-3 py-2 font-semibold">{t.history.opinion}</th>
							</tr>
						</thead>
						<tbody>
							{items.map((q) => {
								const style = outcomeStyle[q.outcome];
								return (
									<tr key={q.id} className="border-t border-line align-top">
										<td className="tabular whitespace-nowrap px-3 py-2 text-muted">{time.format(new Date(q.askedAt))}</td>
										<td className="px-3 py-2">
											<p className="font-medium">{q.text}</p>
											<p className="text-xs text-muted">{q.clarification ?? q.interpretation}</p>
											{q.summaryDiscarded ? <p className="text-xs text-danger">{t.history.summaryDiscarded}</p> : null}
										</td>
										<td className="px-3 py-2">
											<span className={`inline-flex items-center gap-1 whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-semibold ${style.className}`}>
												<style.icon className="size-3.5" aria-hidden />
												{t.history.outcomes[q.outcome]}
											</span>
										</td>
										<td className="tabular px-3 py-2 text-right">{q.outcome === "Answered" ? q.rowCount : "—"}</td>
										<td className="tabular px-3 py-2 text-right">{q.costUsd.toFixed(5)}</td>
										<td className="px-3 py-2">
											{q.feedback === "Helpful" ? (
												<span className="inline-flex items-center gap-1 text-xs text-up">
													<ThumbsUp className="size-3.5" aria-hidden />
													{t.feedback.helpful}
												</span>
											) : q.feedback === "Wrong" ? (
												<span className="inline-flex flex-col text-xs text-brand">
													<span className="inline-flex items-center gap-1">
														<MessageSquareWarning className="size-3.5" aria-hidden />
														{t.feedback.wrong}
													</span>
													{q.feedbackNote ? <span className="text-muted">“{q.feedbackNote}”</span> : null}
												</span>
											) : (
												<span className="text-muted">—</span>
											)}
										</td>
									</tr>
								);
							})}
						</tbody>
					</table>
				</div>
			)}
		</div>
	);
}
