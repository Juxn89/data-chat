import { expect, test, type Locator, type Page } from "@playwright/test";
import { readFile } from "node:fs/promises";
import path from "node:path";

// The product in one go, as a shop owner uses it: upload a spreadsheet, confirm how to read it, ask, follow up, see
// the SQL, export the answer and find it all in the history. The translator plays a script; everything else is real.
const sales = path.resolve(__dirname, "fixtures/ventas-e2e.csv");

async function ask(page: Page, question: string): Promise<Locator> {
	await page.getByLabel("Pregunta sobre tus ventas…").fill(question);
	await page.getByRole("button", { name: "Preguntar", exact: true }).click();
	const card = page.locator("article").filter({ hasText: question });
	await expect(card.getByText("Lo que entendí")).toBeVisible();
	return card;
}

async function rows(card: Locator): Promise<string[]> {
	await card.getByRole("button", { name: "Ver tabla" }).click();
	return card.locator("tbody tr").allInnerTexts();
}

test("an uploaded spreadsheet answers questions with a chart, its SQL and a CSV, and they land in the history", async ({ page }) => {
	await page.goto("/");
	await page.locator('input[type="file"]').setInputFiles(sales);
	await expect(page).toHaveURL(/\/datasets\/[0-9a-f-]{36}/);

	// The proposal: Total is a metric, Sucursal and Categoría are dimensions; nothing can be asked until it is confirmed.
	await expect(page.getByRole("heading", { name: "Confirma cómo leer tus datos" })).toBeVisible();
	await expect(page.getByRole("tab", { name: "Preguntar" })).toBeDisabled();
	await page.getByRole("button", { name: "Confirmar y preguntar" }).click();

	// A ranking: a bar chart, the table on demand, the SQL the server wrote, and the CSV the way Excel in Spanish reads it.
	const ranking = await ask(page, "Ventas por sucursal en el tercer trimestre");
	await expect(ranking.locator("svg.recharts-surface").first()).toBeVisible();
	expect((await rows(ranking)).map((row) => row.replace(/\s+/g, " "))).toEqual(["Altamira 3,384", "Centro 3,332.25"]);
	await ranking.getByText("Ver el SQL").click();
	await expect(ranking.locator("pre")).toContainText("SELECT");
	await expect(ranking.getByText(/Parámetros: .*2026-07-01/)).toBeVisible();
	const downloading = page.waitForEvent("download");
	await ranking.getByRole("button", { name: "Descargar CSV" }).click();
	const file = await downloading;
	expect(file.suggestedFilename()).toBe("ventas-por-sucursal-en-el-tercer-trimestre.csv");
	expect(await readFile(await file.path(), "utf8")).toBe("﻿Sucursal;Total\r\nAltamira;3384\r\nCentro;3332,25\r\n");
	await ranking.getByRole("button", { name: "Me sirvió" }).click();
	await expect(ranking.getByText("Gracias")).toBeVisible();

	// A trend by month, then a follow-up that keeps it and narrows it to one branch.
	const byMonth = await ask(page, "Ventas por mes en el tercer trimestre");
	expect((await rows(byMonth)).map((row) => row.split(/\s+/).at(-1))).toEqual(["2,038.75", "2,488.75", "2,188.75"]);
	await expect(page.getByText("Puedes seguir preguntando sobre lo mismo")).toBeVisible();
	const centro = await ask(page, "¿Y solo en Centro?");
	expect((await rows(centro)).map((row) => row.split(/\s+/).at(-1))).toEqual(["927.25", "1,277.75", "1,127.25"]);

	// Too vague to guess: it asks back instead of answering.
	const vague = await ask(page, "¿Cuál fue la mejor?");
	await expect(vague.getByText("Necesito un dato más")).toBeVisible();
	await expect(vague.locator("svg.recharts-surface")).toHaveCount(0);

	// The history: four questions, three answered, one asked back, the opinion given, nothing spent on a script.
	await page.getByRole("tab", { name: "Historial" }).click();
	await expect(page.getByRole("heading", { name: "Lo que se ha preguntado" })).toBeVisible();
	const tile = (label: string) => page.locator("p", { hasText: new RegExp(`^${label}$`) }).locator("..");
	await expect(tile("Preguntas")).toContainText("4");
	await expect(tile("Respondidas")).toContainText("3");
	await expect(tile("Pidió aclaración")).toContainText("1");
	const first = page.getByRole("row").filter({ hasText: "Ventas por sucursal en el tercer trimestre" });
	await expect(first).toContainText("Respondida");
	await expect(first).toContainText("Me sirvió");
});
