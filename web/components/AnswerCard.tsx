"use client";

import { CircleHelp, Code2, Download, Lightbulb, Table2 } from "lucide-react";
import { useState } from "react";
import { AnswerChart } from "@/components/AnswerChart";
import { FeedbackBar } from "@/components/FeedbackBar";
import { ResultTable } from "@/components/ResultTable";
import type { AskAnswer } from "@/lib/api";
import { download, toCsv } from "@/lib/csv";
import { formatDate } from "@/lib/format";
import { useI18n } from "@/lib/i18n";

/** A file name from the question: its words, plain, at most 60 characters. */
function fileName(question: string): string {
	return (
		question
			.normalize("NFD")
			.replace(/[̀-ͯ]/g, "")
			.replace(/[^\w]+/g, "-")
			.replace(/^-+|-+$/g, "")
			.toLowerCase()
			.slice(0, 60) || "respuesta"
	);
}

/** One question and its answer: how it was read, the summary, the chart, and the table and SQL on demand. */
export function AnswerCard({ question, answer, error, pending }: { question: string; answer?: AskAnswer; error?: string; pending: boolean }) {
	const { locale, t } = useI18n();
	const [showTable, setShowTable] = useState(false);
	const result = answer?.answer;

	return (
		<article className="space-y-3">
			<div className="flex justify-end">
				<p className="max-w-[85%] rounded-2xl rounded-br-sm bg-ink px-4 py-2.5 text-sm text-white">{question}</p>
			</div>

			{pending ? <p className="animate-pulse text-sm text-muted">{t.chat.thinking}</p> : null}
			{error ? (
				<p role="alert" className="rounded-md bg-danger-soft px-4 py-3 text-sm text-danger">
					{error}
				</p>
			) : null}

			{answer ? (
				<div className="space-y-4 rounded-xl border border-line bg-card p-4 sm:p-5">
					<p className="flex items-start gap-2 text-sm text-muted">
						<Lightbulb className="mt-0.5 size-4 shrink-0 text-brand" aria-hidden />
						<span>
							<span className="font-semibold text-ink">{t.chat.understood}: </span>
							{answer.interpretation}
						</span>
					</p>

					{answer.clarification ? (
						<p className="flex items-start gap-2 rounded-md bg-info-soft px-4 py-3 text-sm text-info">
							<CircleHelp className="mt-0.5 size-4 shrink-0" aria-hidden />
							<span>
								<span className="font-semibold">{t.chat.askedBack}: </span>
								{answer.clarification}
							</span>
						</p>
					) : null}

					{answer.narrative ? <p className="text-base leading-relaxed">{answer.narrative}</p> : null}

					{result ? (
						<>
							{result.period ? (
								<p className="text-xs text-muted">
									{t.chat.period(formatDate(result.period.start, locale), formatDate(result.period.end, locale))}
									{result.comparedWith
										? ` ${t.chat.compared(formatDate(result.comparedWith.start, locale), formatDate(result.comparedWith.end, locale))}`
										: ""}
								</p>
							) : null}

							{result.rows.length === 0 ? <p className="text-sm text-muted">{t.chat.empty}</p> : <AnswerChart answer={result} />}

							<div className="flex flex-wrap items-center gap-2 border-t border-line pt-3">
								{result.chart !== "Table" && result.chart !== "Pivot" ? (
									<button
										type="button"
										onClick={() => setShowTable((value) => !value)}
										className="inline-flex items-center gap-1.5 rounded-md px-2.5 py-1 text-sm text-muted hover:bg-black/5 hover:text-ink"
									>
										<Table2 className="size-4" aria-hidden />
										{showTable ? t.chat.hideTable : t.chat.table}
									</button>
								) : null}
								<details className="group w-full sm:w-auto">
									<summary className="inline-flex cursor-pointer list-none items-center gap-1.5 rounded-md px-2.5 py-1 text-sm text-muted hover:bg-black/5 hover:text-ink">
										<Code2 className="size-4" aria-hidden />
										{t.chat.sql}
									</summary>
									<div className="mt-2 space-y-2 rounded-md bg-ink p-3 text-xs text-white/90">
										<p className="text-white/60">{t.chat.sqlHint}</p>
										<pre className="overflow-x-auto whitespace-pre-wrap font-mono">{result.sql}</pre>
										{result.parameters.length > 0 ? (
											<p className="font-mono text-white/70">
												{t.chat.parameters}: {result.parameters.map((p) => `${p.name} = ${p.value}`).join(" · ")}
											</p>
										) : null}
									</div>
								</details>
								{result.rows.length > 0 ? (
									<button
										type="button"
										onClick={() => download(toCsv(result, locale, t.chat.previous, t.chat.timeColumn), `${fileName(question)}.csv`)}
										className="inline-flex items-center gap-1.5 rounded-md px-2.5 py-1 text-sm text-muted hover:bg-black/5 hover:text-ink"
									>
										<Download className="size-4" aria-hidden />
										{t.feedback.download}
									</button>
								) : null}
								<span className="ml-auto text-xs text-muted">{t.chat.cost(answer.usage.costUsd.toFixed(5))}</span>
							</div>

							{showTable ? <ResultTable answer={result} /> : null}
							{result.truncated ? <p className="text-xs text-muted">{t.chat.truncated}</p> : null}
						</>
					) : null}

					<FeedbackBar questionId={answer.questionId} />
				</div>
			) : null}
		</article>
	);
}
