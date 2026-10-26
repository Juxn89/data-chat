"use client";

import { MessageSquareWarning, ThumbsUp } from "lucide-react";
import { useState } from "react";
import { api, type Feedback } from "@/lib/api";
import { useI18n } from "@/lib/i18n";

/**
 * "Me sirvió" or "esto no es lo que pregunté", with an optional note on what was meant. It is how a misreading the
 * evals did not cover shows up in the history (plan §2).
 */
export function FeedbackBar({ questionId }: { questionId: string }) {
	const { t } = useI18n();
	const [state, setState] = useState<"idle" | "writing" | "sent">("idle");
	const [note, setNote] = useState("");

	const send = async (feedback: Feedback, text?: string) => {
		try {
			await api.feedback(questionId, feedback, text);
			setState("sent");
		} catch {
			setState("idle");
		}
	};

	if (state === "sent") {
		return <p className="text-xs text-muted">{t.feedback.thanks}</p>;
	}

	if (state === "writing") {
		return (
			<form
				onSubmit={(event) => {
					event.preventDefault();
					void send("Wrong", note);
				}}
				className="flex flex-col gap-2 sm:flex-row"
			>
				<input
					value={note}
					onChange={(e) => setNote(e.target.value)}
					maxLength={500}
					autoFocus
					placeholder={t.feedback.notePlaceholder}
					aria-label={t.feedback.notePlaceholder}
					className="min-w-0 flex-1 rounded-md border border-line px-3 py-1.5 text-sm focus:border-brand focus:outline-none"
				/>
				<div className="flex gap-2">
					<button type="submit" className="rounded-md bg-ink px-3 py-1.5 text-sm font-semibold text-white">
						{t.feedback.send}
					</button>
					<button type="button" onClick={() => setState("idle")} className="rounded-md px-3 py-1.5 text-sm text-muted hover:text-ink">
						{t.feedback.cancel}
					</button>
				</div>
			</form>
		);
	}

	return (
		<div className="flex flex-wrap gap-2">
			<button
				type="button"
				onClick={() => void send("Helpful")}
				className="inline-flex items-center gap-1.5 rounded-full border border-line px-3 py-1 text-xs text-muted hover:border-up hover:text-up"
			>
				<ThumbsUp className="size-3.5" aria-hidden />
				{t.feedback.helpful}
			</button>
			<button
				type="button"
				onClick={() => setState("writing")}
				className="inline-flex items-center gap-1.5 rounded-full border border-line px-3 py-1 text-xs text-muted hover:border-brand hover:text-brand"
			>
				<MessageSquareWarning className="size-3.5" aria-hidden />
				{t.feedback.wrong}
			</button>
		</div>
	);
}
