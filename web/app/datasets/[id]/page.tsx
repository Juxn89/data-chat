"use client";

import { ArrowLeft, CalendarClock, History, MessagesSquare, SlidersHorizontal } from "lucide-react";
import Link from "next/link";
import { useParams, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { Chat } from "@/components/Chat";
import { HistoryView } from "@/components/HistoryView";
import { LayerEditor } from "@/components/LayerEditor";
import { api, type Dataset, type SemanticLayer } from "@/lib/api";
import { formatNumber } from "@/lib/format";
import { useI18n } from "@/lib/i18n";

type Tab = "ask" | "layer" | "history";

export default function DatasetPage() {
	const { id } = useParams<{ id: string }>();
	const ambiguousDates = useSearchParams().get("dates");
	const { locale, t } = useI18n();
	const [dataset, setDataset] = useState<Dataset | null>(null);
	const [layer, setLayer] = useState<SemanticLayer | null>(null);
	const [tab, setTab] = useState<Tab | null>(null);

	useEffect(() => {
		let active = true;
		void Promise.all([api.dataset(id), api.layer(id)]).then(([d, l]) => {
			if (active) {
				setDataset(d);
				setLayer(l);
			}
		});
		return () => {
			active = false;
		};
	}, [id]);

	if (!dataset || !layer) {
		return <p className="pt-10 text-muted">{t.dataset.loading}</p>;
	}

	// Questions open once the layer is confirmed; until then the editor is the only step.
	const current: Tab = tab === "history" ? "history" : layer.confirmed ? (tab ?? "ask") : "layer";
	const tabButton = (value: Tab, label: string, Icon: typeof MessagesSquare, enabled: boolean) => (
		<button
			type="button"
			role="tab"
			aria-selected={current === value}
			disabled={!enabled}
			onClick={() => setTab(value)}
			className={`inline-flex items-center gap-2 border-b-2 px-1 pb-2 text-sm font-semibold transition-colors disabled:opacity-40 ${
				current === value ? "border-brand text-ink" : "border-transparent text-muted hover:text-ink"
			}`}
		>
			<Icon className="size-4" aria-hidden />
			{label}
		</button>
	);

	return (
		<div className="space-y-6 pt-6">
			<div className="space-y-2">
				<Link href="/" className="inline-flex items-center gap-1 text-sm text-muted hover:text-ink">
					<ArrowLeft className="size-4" aria-hidden />
					{t.dataset.back}
				</Link>
				<h1 className="text-2xl font-bold tracking-tight">{dataset.name}</h1>
				<p className="text-sm text-muted">
					{dataset.sourceFile} · {t.home.rows(formatNumber(dataset.rowCount, locale))} · {t.home.columns(dataset.columns.length)}
				</p>
				{ambiguousDates ? (
					<p className="flex items-start gap-2 rounded-md bg-info-soft px-3 py-2 text-sm text-info">
						<CalendarClock className="mt-0.5 size-4 shrink-0" aria-hidden />
						{t.upload.ambiguousDates(ambiguousDates)}
					</p>
				) : null}
			</div>

			<div role="tablist" className="flex gap-6 border-b border-line">
				{tabButton("ask", t.dataset.ask, MessagesSquare, layer.confirmed)}
				{tabButton("layer", t.dataset.layer, SlidersHorizontal, true)}
				{tabButton("history", t.history.tab, History, true)}
			</div>

			{current === "history" ? <HistoryView datasetId={dataset.id} /> : null}
			{current === "ask" ? <Chat key={dataset.id} datasetId={dataset.id} layer={layer} /> : null}
			{current === "layer" ? (
				<LayerEditor
					layer={layer}
					onConfirmed={(confirmed) => {
						setLayer(confirmed);
						setTab("ask");
					}}
				/>
			) : null}
		</div>
	);
}
