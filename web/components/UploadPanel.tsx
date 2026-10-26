"use client";

import { FileSpreadsheet, Upload } from "lucide-react";
import { useRouter } from "next/navigation";
import { useRef, useState, type DragEvent } from "react";
import { api, ApiProblem } from "@/lib/api";
import { useI18n } from "@/lib/i18n";

/** Uploads one CSV or Excel file and goes to its semantic layer, the next step. */
export function UploadPanel({ onImported }: { onImported: () => void }) {
	const { t } = useI18n();
	const router = useRouter();
	const input = useRef<HTMLInputElement>(null);
	const [busy, setBusy] = useState(false);
	const [error, setError] = useState<string | null>(null);
	const [dragging, setDragging] = useState(false);

	const upload = async (file: File | undefined) => {
		if (!file || busy) {
			return;
		}

		setBusy(true);
		setError(null);
		try {
			const dataset = await api.upload(file);
			onImported();
			const warning = dataset.ambiguousDateColumns.length > 0 ? `?dates=${encodeURIComponent(dataset.ambiguousDateColumns.join(", "))}` : "";
			router.push(`/datasets/${dataset.id}${warning}`);
		} catch (problem) {
			setError(t.errors[problem instanceof ApiProblem ? problem.code : "unknown"] ?? t.errors.unknown ?? null);
		} finally {
			setBusy(false);
		}
	};

	const drop = (event: DragEvent) => {
		event.preventDefault();
		setDragging(false);
		void upload(event.dataTransfer.files[0]);
	};

	return (
		<section aria-labelledby="upload" className="space-y-3">
			<h2 id="upload" className="text-sm font-semibold uppercase tracking-wide text-muted">
				{t.upload.title}
			</h2>
			<div
				onDragOver={(event) => {
					event.preventDefault();
					setDragging(true);
				}}
				onDragLeave={() => setDragging(false)}
				onDrop={drop}
				className={`flex flex-col items-center gap-3 rounded-lg border-2 border-dashed p-8 text-center transition-colors ${
					dragging ? "border-brand bg-brand-soft/40" : "border-line bg-card"
				}`}
			>
				<FileSpreadsheet className="size-8 text-brand" aria-hidden />
				<p className="font-medium">{t.upload.drop}</p>
				<p className="text-sm text-muted">{t.upload.hint}</p>
				<button
					type="button"
					disabled={busy}
					onClick={() => input.current?.click()}
					className="inline-flex items-center gap-2 rounded-md bg-brand px-4 py-2 text-sm font-semibold text-white hover:bg-brand-dark disabled:opacity-60"
				>
					<Upload className="size-4" aria-hidden />
					{busy ? t.upload.uploading : t.upload.choose}
				</button>
				<input
					ref={input}
					type="file"
					accept=".csv,.txt,.xlsx"
					className="sr-only"
					onChange={(event) => void upload(event.target.files?.[0])}
				/>
				{error ? (
					<p role="alert" className="rounded-md bg-danger-soft px-3 py-2 text-sm text-danger">
						{error}
					</p>
				) : null}
			</div>
		</section>
	);
}
