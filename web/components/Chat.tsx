"use client";

import { RotateCcw, Send, Sparkles } from "lucide-react";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { AnswerCard } from "@/components/AnswerCard";
import { api, ApiProblem, type AskAnswer, type SemanticLayer } from "@/lib/api";
import { useI18n } from "@/lib/i18n";

type Turn = { question: string; answer?: AskAnswer; error?: string };

/**
 * The conversation about one dataset. Every answer carries a conversation id; the next question is sent with it, so
 * "¿y el trimestre anterior?" is read against the question before. "New conversation" drops it.
 */
export function Chat({ datasetId, layer }: { datasetId: string; layer: SemanticLayer }) {
	const { t } = useI18n();
	const [turns, setTurns] = useState<Turn[]>([]);
	const [conversation, setConversation] = useState<string | null>(null);
	const [text, setText] = useState("");
	const [busy, setBusy] = useState(false);
	const end = useRef<HTMLDivElement>(null);

	useEffect(() => {
		end.current?.scrollIntoView({ behavior: "smooth", block: "end" });
	}, [turns, busy]);

	const ask = async (question: string) => {
		const trimmed = question.trim();
		if (!trimmed || busy) {
			return;
		}

		setBusy(true);
		setText("");
		setTurns((all) => [...all, { question: trimmed }]);
		try {
			const answer = await api.ask(datasetId, trimmed, conversation);
			setConversation(answer.conversationId);
			setTurns((all) => all.map((turn, i) => (i === all.length - 1 ? { ...turn, answer } : turn)));
		} catch (problem) {
			const message = t.errors[problem instanceof ApiProblem ? problem.code : "unknown"] ?? t.errors.unknown ?? "";
			setTurns((all) => all.map((turn, i) => (i === all.length - 1 ? { ...turn, error: message } : turn)));
		} finally {
			setBusy(false);
		}
	};

	const submit = (event: FormEvent) => {
		event.preventDefault();
		void ask(text);
	};

	// Suggestions built from this dataset's own names, so they always fit it.
	const metric = layer.metrics[0]?.name;
	const dimension = layer.dimensions[0]?.name;
	const suggestions = [
		metric && dimension ? t.chat.suggest.top(metric, dimension) : null,
		metric && layer.timeColumn ? t.chat.suggest.byMonth(metric) : null,
		metric && dimension && layer.timeColumn ? t.chat.suggest.compare(metric, dimension) : null,
	].filter((s): s is string => s !== null);

	return (
		<div className="space-y-6">
			{turns.length === 0 ? (
				<div className="space-y-3">
					<p className="flex items-center gap-2 text-sm font-semibold text-muted">
						<Sparkles className="size-4 text-brand" aria-hidden />
						{t.chat.suggestions}
					</p>
					<div className="flex flex-wrap gap-2">
						{suggestions.map((suggestion) => (
							<button
								key={suggestion}
								type="button"
								onClick={() => void ask(suggestion)}
								className="rounded-full border border-line bg-card px-3.5 py-1.5 text-left text-sm hover:border-brand hover:text-brand"
							>
								{suggestion}
							</button>
						))}
					</div>
				</div>
			) : null}

			<div className="space-y-6">
				{turns.map((turn, index) => (
					<AnswerCard key={index} question={turn.question} answer={turn.answer} error={turn.error} pending={busy && index === turns.length - 1} />
				))}
				<div ref={end} />
			</div>

			<form onSubmit={submit} className="sticky bottom-0 space-y-2 bg-paper/95 pt-3 pb-4 backdrop-blur">
				{conversation ? <p className="text-xs text-muted">{t.chat.followUp}</p> : null}
				<div className="flex gap-2 rounded-xl border border-line bg-card p-2 shadow-sm">
					<input
						value={text}
						onChange={(e) => setText(e.target.value)}
						placeholder={t.chat.placeholder}
						maxLength={500}
						aria-label={t.chat.placeholder}
						className="min-w-0 flex-1 rounded-md px-3 py-2 text-sm focus:outline-none"
					/>
					{conversation ? (
						<button
							type="button"
							onClick={() => {
								setConversation(null);
								setTurns([]);
							}}
							className="inline-flex items-center gap-1.5 rounded-md px-3 text-sm text-muted hover:bg-black/5 hover:text-ink"
						>
							<RotateCcw className="size-4" aria-hidden />
							<span className="hidden sm:inline">{t.chat.newConversation}</span>
						</button>
					) : null}
					<button
						type="submit"
						disabled={busy || !text.trim()}
						className="inline-flex items-center gap-2 rounded-md bg-brand px-4 py-2 text-sm font-semibold text-white hover:bg-brand-dark disabled:opacity-50"
					>
						<Send className="size-4" aria-hidden />
						{t.chat.send}
					</button>
				</div>
			</form>
		</div>
	);
}
