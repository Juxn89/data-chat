"use client";

import { Database, ShieldCheck } from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { UploadPanel } from "@/components/UploadPanel";
import { api, type DatasetSummary } from "@/lib/api";
import { formatDate, formatNumber } from "@/lib/format";
import { useI18n } from "@/lib/i18n";

export default function Home() {
	const { locale, t } = useI18n();
	const [datasets, setDatasets] = useState<DatasetSummary[] | null>(null);

	const load = useCallback(() => {
		api.datasets().then(setDatasets).catch(() => setDatasets([]));
	}, []);

	useEffect(load, [load]);

	return (
		<div className="space-y-10 pt-10">
			<section className="space-y-3">
				<h1 className="text-3xl font-bold tracking-tight sm:text-4xl">{t.app.name}</h1>
				<p className="max-w-2xl text-lg text-muted">{t.app.tagline}</p>
				<p className="flex max-w-2xl items-start gap-2 text-sm text-muted">
					<ShieldCheck className="mt-0.5 size-4 shrink-0 text-brand" aria-hidden />
					{t.app.promise}
				</p>
			</section>

			<div className="grid gap-6 lg:grid-cols-[1fr_380px]">
				<section aria-labelledby="datasets" className="space-y-3">
					<h2 id="datasets" className="text-sm font-semibold uppercase tracking-wide text-muted">
						{t.home.title}
					</h2>
					{datasets?.length === 0 ? <p className="text-muted">{t.home.empty}</p> : null}
					<ul className="space-y-3">
						{datasets?.map((dataset) => (
							<li key={dataset.id}>
								<Link
									href={`/datasets/${dataset.id}`}
									className="flex items-center gap-4 rounded-lg border border-line bg-card p-4 transition-colors hover:border-brand/40 hover:bg-brand-soft/30"
								>
									<span className="grid size-10 shrink-0 place-items-center rounded-md bg-brand-soft text-brand">
										<Database className="size-5" aria-hidden />
									</span>
									<span className="min-w-0 flex-1">
										<span className="block truncate font-semibold">{dataset.name}</span>
										<span className="block truncate text-sm text-muted">
											{dataset.sourceFile} · {t.home.rows(formatNumber(dataset.rowCount, locale))} · {t.home.columns(dataset.columnCount)} ·{" "}
											{formatDate(dataset.importedAt.slice(0, 10), locale)}
										</span>
									</span>
									<span className="text-sm font-medium text-brand">{t.home.open}</span>
								</Link>
							</li>
						))}
					</ul>
				</section>
				<UploadPanel onImported={load} />
			</div>
		</div>
	);
}
