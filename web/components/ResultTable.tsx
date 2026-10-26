"use client";

import type { QueryAnswer } from "@/lib/api";
import { formatCell } from "@/lib/format";
import { useI18n } from "@/lib/i18n";

/** The rows as they are: always available, whatever the chart (the accessible view of every answer). */
export function ResultTable({ answer }: { answer: QueryAnswer }) {
	const { locale, t } = useI18n();
	const header = (index: number) => {
		const column = answer.columns[index]!;
		if (column.kind === "Time") {
			return t.chat.timeColumn;
		}

		return column.kind === "PreviousMetric" ? `${column.name} (${t.chat.previous})` : column.name;
	};

	return (
		<div className="overflow-x-auto rounded-md border border-line">
			<table className="w-full text-sm">
				<thead className="bg-paper text-left text-xs uppercase tracking-wide text-muted">
					<tr>
						{answer.columns.map((column, i) => (
							<th key={i} scope="col" className={`px-3 py-2 font-semibold ${column.kind.endsWith("Metric") ? "text-right" : ""}`}>
								{header(i)}
							</th>
						))}
					</tr>
				</thead>
				<tbody>
					{answer.rows.map((row, r) => (
						<tr key={r} className="border-t border-line">
							{row.map((cell, c) => (
								<td key={c} className={`px-3 py-1.5 ${typeof cell === "number" ? "tabular text-right" : ""}`}>
									{formatCell(cell, locale)}
								</td>
							))}
						</tr>
					))}
				</tbody>
			</table>
		</div>
	);
}
