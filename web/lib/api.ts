// The API's contract, as the .NET side serialises it (camelCase, enums as names, decimals as numbers, dates ISO).

export type ColumnType = "Text" | "Integer" | "Decimal" | "Date";
export type Aggregation = "Sum" | "Average" | "Min" | "Max" | "Count" | "CountDistinct";
export type ChartKind = "Kpi" | "Line" | "Bar" | "Pivot" | "Table";
export type ResultColumnKind = "Time" | "Dimension" | "Metric" | "PreviousMetric";

export type Column = { ordinal: number; name: string; type: ColumnType; distinctCount: number; samples: string[] };

export type DatasetSummary = { id: string; name: string; sourceFile: string; rowCount: number; columnCount: number; importedAt: string };

export type Dataset = {
	id: string;
	name: string;
	sourceFile: string;
	rowCount: number;
	importedAt: string;
	columns: Column[];
	ambiguousDateColumns: string[];
};

// An aggregate has aggregation and column (by ordinal; none for a row count); a ratio, numerator and denominator.
export type MetricDto = {
	key: string;
	name: string;
	aggregation?: Aggregation | null;
	column?: number | null;
	numerator?: string | null;
	denominator?: string | null;
};

export type DimensionDto = { key: string; name: string; column: number };

export type SemanticLayer = {
	datasetId: string;
	confirmed: boolean;
	timeColumn: number | null;
	asOf: string | null;
	metrics: MetricDto[];
	dimensions: DimensionDto[];
	columns: Column[];
};

export type ResultColumn = { key: string; name: string; kind: ResultColumnKind };
export type Cell = string | number | null;

export type QueryAnswer = {
	columns: ResultColumn[];
	rows: Cell[][];
	sql: string;
	parameters: { name: string; value: string }[];
	period: { start: string; end: string } | null;
	comparedWith: { start: string; end: string } | null;
	truncated: boolean;
	chart: ChartKind;
};

export type Usage = { model: string; inputTokens: number; outputTokens: number; costUsd: number; calls: number };

export type AskAnswer = {
	questionId: string;
	conversationId: string;
	interpretation: string;
	clarification: string | null;
	answer: QueryAnswer | null;
	narrative: string | null;
	usage: Usage;
};

export type Feedback = "Helpful" | "Wrong";
export type Outcome = "Answered" | "Clarified" | "Failed";

export type QuestionSummary = {
	id: string;
	conversationId: string;
	text: string;
	interpretation: string;
	outcome: Outcome;
	errorCode: string | null;
	clarification: string | null;
	rowCount: number;
	costUsd: number;
	latencyMs: number;
	summaryShown: boolean;
	summaryDiscarded: boolean;
	feedback: Feedback | null;
	feedbackNote: string | null;
	askedAt: string;
};

export type History = { items: QuestionSummary[]; spentTodayUsd: number; dailyBudgetUsd: number };

/** A request the API refused, with its stable code (docs/errors.md) and, for invalid layers, every issue. */
export class ApiProblem extends Error {
	constructor(
		readonly status: number,
		readonly code: string,
		message: string,
		readonly issues: string[] = [],
	) {
		super(message);
	}
}

const base = "/api/v1";

async function send<T>(path: string, init?: RequestInit): Promise<T> {
	const response = await fetch(`${base}${path}`, { cache: "no-store", ...init });
	if (!response.ok) {
		const problem = (await response.json().catch(() => null)) as ({ code?: string; title?: string } & Record<string, unknown>) | null;
		const issues = Object.entries(problem ?? {})
			.filter(([key]) => /^issue\d+$/.test(key))
			.map(([, value]) => String(value));
		throw new ApiProblem(response.status, problem?.code ?? "unknown", problem?.title ?? response.statusText, issues);
	}

	return (await response.json()) as T;
}

const json = (body: unknown): RequestInit => ({
	headers: { "Content-Type": "application/json" },
	body: JSON.stringify(body),
});

export const api = {
	datasets: () => send<DatasetSummary[]>("/datasets"),
	dataset: (id: string) => send<Dataset>(`/datasets/${id}`),
	upload: (file: File, name?: string) => {
		const form = new FormData();
		form.append("file", file, file.name);
		if (name) {
			form.append("name", name);
		}

		return send<Dataset>("/datasets", { method: "POST", body: form });
	},
	layer: (id: string) => send<SemanticLayer>(`/datasets/${id}/semantic-layer`),
	confirm: (id: string, layer: Pick<SemanticLayer, "timeColumn" | "asOf" | "metrics" | "dimensions">) =>
		send<SemanticLayer>(`/datasets/${id}/semantic-layer`, { method: "PUT", ...json(layer) }),
	ask: (id: string, text: string, conversationId: string | null) =>
		send<AskAnswer>(`/datasets/${id}/questions`, { method: "POST", ...json({ text, conversationId }) }),
	history: (id: string) => send<History>(`/datasets/${id}/questions`),
	feedback: async (questionId: string, feedback: Feedback, note?: string) => {
		const response = await fetch(`${base}/questions/${questionId}/feedback`, { method: "POST", ...json({ feedback, note }) });
		if (!response.ok) {
			throw new ApiProblem(response.status, "feedback.failed", response.statusText);
		}
	},
};
