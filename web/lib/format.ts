import type { Cell } from "@/lib/api";
import type { Locale } from "@/lib/i18n";

const tags: Record<Locale, string> = { es: "es-NI", en: "en-US" };

/** A figure the way people in the page's language read it: 12.078,19 in Spanish, 12,078.19 in English. */
export function formatNumber(value: number, locale: Locale, maximumFractionDigits = 2): string {
	return new Intl.NumberFormat(tags[locale], { maximumFractionDigits, minimumFractionDigits: Number.isInteger(value) ? 0 : 2 }).format(value);
}

/** A cell as text: numbers formatted, labels as they are, nothing as a dash. */
export function formatCell(value: Cell, locale: Locale): string {
	if (value === null) {
		return "—";
	}

	return typeof value === "number" ? formatNumber(value, locale) : value;
}

/** An ISO date, 2026-09-30, as 30 sep 2026 or Sep 30, 2026. */
export function formatDate(iso: string, locale: Locale): string {
	const [year, month, day] = iso.split("-").map(Number);
	return new Intl.DateTimeFormat(tags[locale], { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" }).format(
		new Date(Date.UTC(year ?? 1970, (month ?? 1) - 1, day ?? 1)),
	);
}

/** The percentage change from one figure to another, or null when there is nothing to compare with. */
export function change(current: Cell, previous: Cell): number | null {
	return typeof current === "number" && typeof previous === "number" && previous !== 0 ? ((current - previous) / previous) * 100 : null;
}
