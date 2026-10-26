import type { PlaywrightTestConfig } from "@playwright/test";

// Ports of their own, so a run never lands on the dev servers (5130 and 3030).
const apiPort = 5197;
const webPort = 3031;

export const baseURL = `http://localhost:${webPort}`;

/**
 * The real API (import, semantic layer, questions, history) and a production build of the web app. Each run gets its
 * own database, so the only datasets are the demo's and the one the test uploads.
 * @param translator How questions are read: a script (the end-to-end test) or the real model (the demo).
 */
export function servers(translator: Record<string, string>): PlaywrightTestConfig["webServer"] {
	const run = (process.env.E2E_RUN ??= `${Date.now()}`);
	const database =
		process.env.E2E_DATABASE ?? `Host=localhost;Port=5434;Username=postgres;Password=postgres;Database=datachat_e2e_${run}`;

	return [
		{
			command: "dotnet run --project ../src/DataChat.Api --no-launch-profile",
			url: `http://localhost:${apiPort}/health`,
			env: {
				ASPNETCORE_ENVIRONMENT: "Development",
				ASPNETCORE_URLS: `http://localhost:${apiPort}`,
				ConnectionStrings__datachatdb: database,
				...translator,
			},
			timeout: 180_000,
		},
		{
			// The rewrite to the API is fixed at build time, hence API_URL here.
			command: `pnpm build && pnpm exec next start --port ${webPort}`,
			url: baseURL,
			env: { API_URL: `http://localhost:${apiPort}`, NEXT_TELEMETRY_DISABLED: "1" },
			timeout: 180_000,
		},
	];
}
