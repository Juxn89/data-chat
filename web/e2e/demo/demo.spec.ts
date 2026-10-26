import { expect, test, type Locator, type Page } from "@playwright/test";
import { execFileSync } from "node:child_process";
import { mkdir, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { baseURL } from "../servers";

// The walkthrough for the README, the portfolio and the Workana listing, recorded against the real model
// (playwright.demo.config.ts): upload the sample spreadsheet, confirm how to read it, ask in Spanish, follow up, see
// the SQL and the history. It writes the video, its poster and the screenshots to docs/demo.
//
// Frames come from Chromium's screencast (1280×720 at device scale 1.5), with their timestamps; ffmpeg, in a container
// (pinned image, no network), turns them into a 1920×1080 MP4, so nothing is installed.

const out = path.resolve(__dirname, "../../../docs/demo");
const sample = path.resolve(__dirname, "../../../samples/ventas-demo.xlsx");
const ffmpegImage = "jrottenberg/ffmpeg:9.0.1-ubuntu2404@sha256:7e8154ec4779d335054de5e173f1ef52032abaa12ac7416cc7f0c88f4973bd14";
const viewport = { width: 1280, height: 720 };
const scale = 1.5;
const fps = 30;

declare global {
	interface Window {
		demoCaption?: (text: string) => void;
	}
}

// A pointer that follows the mouse and a caption at the bottom.
async function addOverlay(page: Page) {
	await page.addInitScript(() => {
		const ensure = (id: string, style: string) => {
			let element = document.getElementById(id);
			if (!element) {
				element = document.createElement("div");
				element.id = id;
				element.setAttribute("style", style);
				document.body.appendChild(element);
			}
			return element;
		};
		const pointer = () =>
			ensure(
				"demo-pointer",
				"position:fixed;z-index:2147483647;width:22px;height:22px;margin:-11px 0 0 -11px;border-radius:50%;background:rgba(190,18,60,.25);border:2px solid #be123c;pointer-events:none;transition:transform .12s",
			);
		window.addEventListener("mousemove", (event) => {
			pointer().style.left = `${event.clientX}px`;
			pointer().style.top = `${event.clientY}px`;
		});
		window.addEventListener("mousedown", () => (pointer().style.transform = "scale(.6)"));
		window.addEventListener("mouseup", () => (pointer().style.transform = "scale(1)"));
		window.demoCaption = (text: string) => {
			const caption = ensure(
				"demo-caption",
				"position:fixed;z-index:2147483646;left:50%;bottom:22px;transform:translateX(-50%);width:max-content;max-width:min(980px,92vw);padding:12px 22px;border-radius:12px;background:rgba(28,25,23,.93);color:#fff;font:500 19px/1.45 system-ui,sans-serif;text-align:center;box-shadow:0 12px 32px rgba(0,0,0,.28);pointer-events:none",
			);
			caption.textContent = text;
			caption.style.display = text ? "block" : "none";
		};
	});
}

async function say(page: Page, text: string, holdMs: number) {
	await page.evaluate((caption) => window.demoCaption?.(caption), text);
	await page.waitForTimeout(holdMs);
}

// Screenshots without the overlay: they stand on their own in the listing.
async function shot(page: Page, file: string) {
	const hidden = ["demo-caption", "demo-pointer"];
	await page.evaluate((ids) => ids.forEach((id) => document.getElementById(id)?.style.setProperty("visibility", "hidden")), hidden);
	await page.screenshot({ path: file });
	await page.evaluate((ids) => ids.forEach((id) => document.getElementById(id)?.style.removeProperty("visibility")), hidden);
}

// One JPEG per composited frame, with its timestamp: the screencast only sends what changed.
async function record(page: Page, dir: string) {
	const frames: { file: string; at: number }[] = [];
	const cdp = await page.context().newCDPSession(page);
	cdp.on("Page.screencastFrame", async ({ data, metadata, sessionId }) => {
		const file = `f${String(frames.length).padStart(5, "0")}.jpg`;
		frames.push({ file, at: metadata.timestamp ?? Date.now() / 1000 });
		await writeFile(path.join(dir, file), Buffer.from(data, "base64"));
		await cdp.send("Page.screencastFrameAck", { sessionId }).catch(() => undefined);
	});
	const size = { maxWidth: viewport.width * scale, maxHeight: viewport.height * scale };
	await cdp.send("Page.startScreencast", { format: "jpeg", quality: 92, ...size, everyNthFrame: 1 });
	return async () => {
		await cdp.send("Page.stopScreencast");
		return frames;
	};
}

// A concat list that covers [start, end]: the first frame from the start, each frame until the next one.
function concatList(frames: { file: string; at: number }[], start: number, end: number) {
	const lines = frames.map((frame, i) => {
		const from = i === 0 ? start : frame.at;
		return `file '${frame.file}'\nduration ${((frames[i + 1]?.at ?? end) - from).toFixed(4)}`;
	});
	return `${lines.join("\n")}\nfile '${frames.at(-1)?.file}'\n`;
}

// Types a question the way a person does and waits for the answer card.
async function ask(page: Page, question: string): Promise<Locator> {
	const input = page.getByLabel("Pregunta sobre tus ventas…");
	await input.click();
	await input.pressSequentially(question, { delay: 40 });
	await page.getByRole("button", { name: "Preguntar", exact: true }).click();
	const card = page.locator("article").filter({ hasText: question });
	await expect(card.getByText("Lo que entendí")).toBeVisible();
	await card.scrollIntoViewIfNeeded();
	return card;
}

test("demo", async ({ browser }) => {
	await mkdir(out, { recursive: true });
	const frames = test.info().outputPath("frames");
	await rm(frames, { recursive: true, force: true });
	await mkdir(frames, { recursive: true });

	const page = await (await browser.newContext({ viewport, deviceScaleFactor: scale, locale: "es-ES", baseURL })).newPage();
	await addOverlay(page);
	await page.goto("/");
	await page.waitForLoadState("networkidle");
	await page.mouse.move(640, 400);

	const stop = await record(page, frames);
	const started = Date.now() / 1000;

	await say(page, "Una distribuidora sube su Excel de ventas (37,636 líneas) y le pregunta en español.", 4500);
	await shot(page, path.join(out, "01-inicio.png"));
	await page.locator('input[type="file"]').setInputFiles(sample);
	await expect(page.getByRole("heading", { name: "Confirma cómo leer tus datos" })).toBeVisible();

	await say(page, "Propone qué columnas se suman y por cuáles se agrupa; la persona las nombra como las llama y confirma.", 0);
	await page.locator('input[type="date"]').fill("2026-10-15");
	const total = page.getByRole("textbox", { name: "Nombre total" });
	await total.click();
	await total.fill("");
	await total.pressSequentially("Ventas", { delay: 70 });
	await total.blur();
	await page.waitForTimeout(3500);
	await shot(page, path.join(out, "02-capa-semantica.png"));
	await page.getByRole("button", { name: "Confirmar y preguntar" }).click();
	await expect(page.getByLabel("Pregunta sobre tus ventas…")).toBeVisible();

	const ranking = await ask(page, "¿Cuánto vendió cada sucursal el último trimestre?");
	// The poster is this moment, taken from the video: the question, the chart and the summary.
	const posterAt = Date.now() / 1000 + 1.5;
	await say(page, "Claude Haiku no escribe SQL: traduce la pregunta a métricas y dimensiones, y el servidor valida y ejecuta.", 6000);
	await shot(page, path.join(out, "03-ranking.png"));

	await say(page, "El resumen solo se muestra si cada cifra que cita está en el resultado.", 0);
	await ranking.getByText("Ver el SQL").click();
	await ranking.locator("pre").scrollIntoViewIfNeeded();
	await page.waitForTimeout(5000);
	await shot(page, path.join(out, "04-sql.png"));
	await ranking.getByText("Ver el SQL").click();

	await ask(page, "Ventas por mes en los últimos 12 meses");
	await say(page, "Una serie en el tiempo se dibuja como línea; un ranking, como barras.", 5000);
	await shot(page, path.join(out, "05-tendencia.png"));

	await say(page, "Las preguntas de seguimiento heredan el contexto.", 0);
	await ask(page, "¿Y solo en Centro?");
	await page.waitForTimeout(4500);

	await say(page, "Y compara periodos: el último trimestre frente al mismo del año pasado.", 0);
	await ask(page, "Ventas por categoría del último trimestre frente al mismo del año pasado");
	await page.waitForTimeout(5500);
	await shot(page, path.join(out, "06-comparacion.png"));

	await page.getByRole("tab", { name: "Historial" }).click();
	await expect(page.getByRole("heading", { name: "Lo que se ha preguntado" })).toBeVisible();
	await say(page, "Cada pregunta queda registrada con su costo, bajo un tope de gasto diario de IA.", 5500);
	await shot(page, path.join(out, "07-historial.png"));

	await say(page, "Pregúntale a tus ventas · .NET 10 · Claude Haiku · github.com/Juxn89/data-chat", 5000);
	const ended = Date.now() / 1000;
	const recorded = await stop();

	const start = recorded[0]?.at ?? started;
	await writeFile(path.join(frames, "frames.txt"), concatList(recorded, start, ended));
	execFileSync(
		"docker",
		[
			"run", "--rm", "--network", "none", "--read-only", "--tmpfs", "/tmp",
			"-v", `${frames}:/in`, "-v", `${out}:/out`, ffmpegImage,
			"-y", "-loglevel", "error",
			"-f", "concat", "-safe", "0", "-i", "/in/frames.txt",
			"-vf", `fps=${fps},scale=1920:1080:flags=lanczos,setsar=1,format=yuv420p`,
			"-c:v", "libx264", "-preset", "slow", "-crf", "20", "-movflags", "+faststart",
			"/out/demo.mp4",
		],
		{ stdio: "inherit" },
	);
	execFileSync(
		"docker",
		[
			"run", "--rm", "--network", "none", "--read-only", "--tmpfs", "/tmp", "-v", `${out}:/out`, ffmpegImage,
			"-y", "-loglevel", "error", "-ss", (posterAt - start).toFixed(2), "-i", "/out/demo.mp4", "-frames:v", "1", "-q:v", "3", "/out/poster.jpg",
		],
		{ stdio: "inherit" },
	);
	console.log(`${recorded.length} frames, ${(ended - start).toFixed(1)} s → ${path.join(out, "demo.mp4")}`);
});
