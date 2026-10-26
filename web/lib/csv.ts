import type { QueryAnswer } from "@/lib/api";
import type { Locale } from "@/lib/i18n";

// What a spreadsheet opened by double-click expects in each language: Excel in Spanish reads ";" and decimal commas,
// in English "," and decimal points. The byte-order mark makes it read UTF-8, so accents survive.
const settings: Record<Locale, { separator: string; decimal: string }> = {
	es: { separator: ";", decimal: "," },
	en: { separator: ",", decimal: "." },
};

/** An answer as CSV: the column names people see, numbers without thousands separators, nothing as empty. */
export function toCsv(answer: QueryAnswer, locale: Locale, previousLabel: string, timeLabel: string): string {
	const { separator, decimal } = settings[locale];
	const quote = (text: string) => (/[";,\n\r]/.test(text) || text.includes(separator) ? `"${text.replaceAll('"', '""')}"` : text);
	const header = answer.columns.map((column) =>
		quote(column.kind === "Time" ? timeLabel : column.kind === "PreviousMetric" ? `${column.name} (${previousLabel})` : column.name),
	);
	const rows = answer.rows.map((row) =>
		row.map((cell) => (cell === null ? "" : typeof cell === "number" ? String(cell).replace(".", decimal) : quote(cell))).join(separator),
	);

	return `﻿${[header.join(separator), ...rows].join("\r\n")}\r\n`;
}

/** Hands the browser a file to save. */
export function download(content: string, fileName: string): void {
	const url = URL.createObjectURL(new Blob([content], { type: "text/csv;charset=utf-8" }));
	const link = document.createElement("a");
	link.href = url;
	link.download = fileName;
	link.click();
	URL.revokeObjectURL(url);
}
