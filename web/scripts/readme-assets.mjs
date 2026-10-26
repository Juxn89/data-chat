// Generates the README artwork in docs/readme/: hero banner, metrics strip, feature icons and divider (SVG,
// light and dark), and the product showcase (PNG), composed from two screenshots `pnpm demo` writes to docs/demo/
// (a monthly trend with its summary, and a ranking with the SQL the server wrote).
//
//   node scripts/readme-assets.mjs
//
// GitHub shows README images through <img>: plain SVG, no external fonts or scripts. CSS animations inside the
// SVG do run there, and they stop under prefers-reduced-motion.
import { chromium } from "@playwright/test";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const out = fileURLToPath(new URL("../../docs/readme/", import.meta.url));
const demo = fileURLToPath(new URL("../../docs/demo/", import.meta.url));
await mkdir(out, { recursive: true });

// Its own brand, distinct from docs-assistant (teal), claude-pr-review-bot (violet), invoice-extractor (blue) and
// booking-agent (indigo); amber is the shared accent.
const brand = { rose: "#be123c", salmon: "#fda4af", amber: "#f59e0b", green: "#16a34a" };

const themes = {
	dark: { bg: "#0d0a0c", panel: "#171114", card: "#1c1519", border: "#3a2a31", text: "#f1f5f9", muted: "#94a3b8", line: "#3d2c34", dot: "#ffffff", dotOpacity: 0.06, orb: 0.3 },
	light: { bg: "#fdf8f9", panel: "#ffffff", card: "#ffffff", border: "#efdfe3", text: "#0f172a", muted: "#475569", line: "#ecd5db", dot: "#be123c", dotOpacity: 0.07, orb: 0.14 },
};

const sans = `'Segoe UI', Inter, -apple-system, BlinkMacSystemFont, 'Helvetica Neue', Arial, sans-serif`;
const mono = `'Cascadia Code', 'SFMono-Regular', Consolas, Menlo, monospace`;
const escape = (text) => text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

function defs(t) {
	return `
	<linearGradient id="brand" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="${brand.salmon}"/><stop offset=".6" stop-color="${brand.rose}"/><stop offset="1" stop-color="${brand.amber}"/></linearGradient>
	<linearGradient id="metric" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${brand.salmon}"/><stop offset=".55" stop-color="${brand.rose}"/><stop offset="1" stop-color="${brand.amber}"/></linearGradient>
	<radialGradient id="orbRose"><stop offset="0" stop-color="${brand.rose}" stop-opacity="${t.orb}"/><stop offset="1" stop-color="${brand.rose}" stop-opacity="0"/></radialGradient>
	<radialGradient id="orbAmber"><stop offset="0" stop-color="${brand.amber}" stop-opacity="${t.orb * 0.7}"/><stop offset="1" stop-color="${brand.amber}" stop-opacity="0"/></radialGradient>
	<pattern id="dots" width="22" height="22" patternUnits="userSpaceOnUse"><circle cx="2" cy="2" r="1.1" fill="${t.dot}" fill-opacity="${t.dotOpacity}"/></pattern>`;
}

// ── Hero: what it does, drawn — a question becomes a semantic query, the server's SQL, and a chart ──────────
function hero(name) {
	const t = themes[name];
	const W = 1280;
	const H = 460;
	const accent = name === "dark" ? brand.salmon : brand.rose;
	const chips = ["Claude Haiku 5.5", "No SQL from the model", "Read-only role", "Checked summaries", "Attacks 20/20"];
	let x = 72;
	const chipSvg = chips
		.map((chip) => {
			const w = chip.length * 8.4 + 30;
			const g = `<g transform="translate(${x} 352)"><rect width="${w}" height="32" rx="16" fill="${t.panel}" stroke="${t.border}"/><text x="${w / 2}" y="21" text-anchor="middle" font-family="${sans}" font-size="14" font-weight="600" fill="${t.text}">${escape(chip)}</text></g>`;
			x += w + 8;
			return g;
		})
		.join("");

	// The answer: four bars, largest first.
	const bars = [148, 122, 98, 50]
		.map((w, i) => `<g transform="translate(16 ${96 + i * 22})"><g class="pop" style="animation-delay:${1.2 + i * 0.12}s"><rect width="${w}" height="14" rx="4" fill="${brand.rose}" fill-opacity="${1 - i * 0.15}"/></g></g>`)
		.join("");

	return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="title desc">
<title id="title">data-chat</title>
<desc id="desc">Ask your sales spreadsheet in plain language and get the figure, a chart and how it was computed. The model never writes SQL: it returns a semantic query the server validates and compiles.</desc>
<style>
	.flow { stroke-dasharray: 6 7; animation: flow 1.6s linear infinite; }
	.pop { opacity: 0; animation: pop .5s ease-out forwards; }
	.glow { animation: glow 2.4s ease-in-out infinite alternate; }
	@keyframes flow { to { stroke-dashoffset: -26; } }
	@keyframes pop { from { opacity: 0; transform: translateY(6px); } to { opacity: 1; transform: none; } }
	@keyframes glow { from { opacity: .55; } to { opacity: 1; } }
	@media (prefers-reduced-motion: reduce) { .flow, .glow { animation: none; } .pop { animation: none; opacity: 1; } }
</style>
<defs>${defs(t)}
	<clipPath id="frame"><rect width="${W}" height="${H}" rx="28"/></clipPath>
	<filter id="soft" x="-20%" y="-20%" width="140%" height="160%"><feDropShadow dx="0" dy="14" stdDeviation="16" flood-color="#000" flood-opacity="${name === "dark" ? 0.45 : 0.12}"/></filter>
</defs>
<g clip-path="url(#frame)">
	<rect width="${W}" height="${H}" fill="${t.bg}"/>
	<rect width="${W}" height="${H}" fill="url(#dots)"/>
	<circle cx="180" cy="40" r="340" fill="url(#orbRose)"/>
	<circle cx="1060" cy="470" r="360" fill="url(#orbAmber)"/>
</g>
<rect x=".5" y=".5" width="${W - 1}" height="${H - 1}" rx="28" fill="none" stroke="${t.border}"/>

<g transform="translate(72 70)">
	<rect width="236" height="30" rx="15" fill="${t.panel}" stroke="${t.border}"/>
	<circle cx="18" cy="15" r="4.5" fill="${brand.salmon}" class="glow"/>
	<text x="31" y="20" font-family="${sans}" font-size="12.5" font-weight="700" letter-spacing=".1em" fill="${t.muted}">OPEN SOURCE · .NET 10 · MIT</text>
</g>
<text x="68" y="172" font-family="${sans}" font-size="66" font-weight="800" letter-spacing="-.03em" fill="${t.text}">data<tspan fill="url(#brand)">-chat</tspan></text>
<text x="72" y="230" font-family="${sans}" font-size="24" font-weight="600" fill="${t.text}">Ask your sales spreadsheet, in plain words.</text>
<text x="72" y="268" font-family="${sans}" font-size="20" fill="${t.muted}">The figure, a chart and how it was computed.</text>
<text x="72" y="300" font-family="${sans}" font-size="20" fill="${t.muted}">The model never writes SQL; the server does, read-only.</text>
${chipSvg}

<!-- The question -->
<g transform="translate(668 62)" filter="url(#soft)">
	<rect width="252" height="64" rx="16" fill="${brand.rose}"/>
	<text x="16" y="27" font-family="${sans}" font-size="13" font-weight="600" fill="#fff">¿Cuánto vendió cada sucursal</text>
	<text x="16" y="46" font-family="${sans}" font-size="13" font-weight="600" fill="#fff">el último trimestre?</text>
</g>

<path d="M790 128 C 790 156, 796 160, 796 176" fill="none" stroke="${accent}" stroke-width="2.2" class="flow"/>

<!-- The semantic query the model returns -->
<g transform="translate(668 178)" filter="url(#soft)">
	<rect width="252" height="150" rx="18" fill="${t.card}" stroke="${t.border}"/>
	<text x="18" y="30" font-family="${sans}" font-size="11.5" font-weight="700" letter-spacing=".08em" fill="${accent}">SEMANTIC QUERY · NOT SQL</text>
	<text x="18" y="58" font-family="${mono}" font-size="12.5" fill="${t.text}">metrics: [ventas]</text>
	<text x="18" y="80" font-family="${mono}" font-size="12.5" fill="${t.text}">dimensions: [sucursal]</text>
	<text x="18" y="102" font-family="${mono}" font-size="12.5" fill="${t.text}">period: LastQuarter</text>
	<g transform="translate(18 116)"><g class="pop" style="animation-delay:.5s">
		<rect width="132" height="22" rx="11" fill="${brand.green}" fill-opacity=".14" stroke="${brand.green}"/>
		<text x="66" y="15" text-anchor="middle" font-family="${sans}" font-size="11.5" font-weight="700" fill="${brand.green}">validated ✓</text>
	</g></g>
</g>

<path d="M920 254 C 962 254, 960 196, 1000 192" fill="none" stroke="${brand.amber}" stroke-width="2.2" class="flow"/>

<!-- The answer: the server's SQL, run read-only, and the chart -->
<g transform="translate(998 92)" filter="url(#soft)">
	<rect width="216" height="214" rx="18" fill="${t.card}" stroke="${t.border}"/>
	<text x="16" y="30" font-family="${sans}" font-size="13" font-weight="700" fill="${t.text}">Centro: 12,078.19</text>
	<circle cx="196" cy="25" r="4.5" fill="${brand.amber}" class="glow"/>
	<text x="16" y="52" font-family="${mono}" font-size="10.5" fill="${t.muted}">SELECT … WHERE date</text>
	<text x="16" y="68" font-family="${mono}" font-size="10.5" fill="${t.muted}">BETWEEN @p1 AND @p2</text>
	${bars}
	<text x="16" y="198" font-family="${mono}" font-size="10.5" fill="${t.muted}">read-only · 3 s · LIMIT</text>
</g>
</svg>
`;
}

// ── Metrics: measured by this repo's evals (evals/results, Claude Haiku 5.5, 26 Oct 2026) ─────────────────
const metricItems = [
	["75 / 75", "questions answered right", "Spanish and English, 26 Oct 2026"],
	["20 / 20", "injection attempts with no effect", "in questions and in the data"],
	["0", "invented figures shown", "1 of 75 summaries caught and discarded"],
	["USD 0.00058", "per question", "summary included"],
];

function metrics(name) {
	const t = themes[name];
	const W = 1280;
	const H = 150;
	const col = W / metricItems.length;
	const cells = metricItems
		.map(
			([value, label, note], i) => `
	<g transform="translate(${col * i + col / 2} 0)">
		<text y="64" text-anchor="middle" font-family="${sans}" font-size="40" font-weight="800" letter-spacing="-.02em" fill="url(#metric)">${escape(value)}</text>
		<text y="94" text-anchor="middle" font-family="${sans}" font-size="16" font-weight="600" fill="${t.text}">${escape(label)}</text>
		<text y="116" text-anchor="middle" font-family="${sans}" font-size="13.5" fill="${t.muted}">${escape(note)}</text>
	</g>${i > 0 ? `<line x1="${col * i}" y1="36" x2="${col * i}" y2="114" stroke="${t.border}"/>` : ""}`,
		)
		.join("");
	const label = metricItems.map(([value, label, note]) => `${value} ${label}, ${note}.`).join(" ");
	return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" role="img" aria-label="${escape(label)}">
<defs>${defs(t)}</defs>
<rect x=".5" y=".5" width="${W - 1}" height="${H - 1}" rx="22" fill="${t.panel}" stroke="${t.border}"/>
${cells}
</svg>
`;
}

// ── Feature icons: Lucide glyphs (ISC license) on a gradient tile ─────────────────────────────────────────
const glyphs = {
	sheet: '<rect width="18" height="18" x="3" y="3" rx="2"/><path d="M3 9h18"/><path d="M3 15h18"/><path d="M9 9v12"/><path d="M15 9v12"/>',
	layers: '<path d="M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z"/><path d="m22 17.65-9.17 4.16a2 2 0 0 1-1.66 0L2 17.65"/><path d="m22 12.65-9.17 4.16a2 2 0 0 1-1.66 0L2 12.65"/>',
	shield: '<path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/><path d="m9 12 2 2 4-4"/>',
	database: '<ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M3 5v14a9 3 0 0 0 18 0V5"/><path d="M3 12a9 3 0 0 0 18 0"/>',
	chart: '<path d="M3 3v16a2 2 0 0 0 2 2h16"/><path d="M18 17V9"/><path d="M13 17V5"/><path d="M8 17v-3"/>',
	check: '<path d="M3.85 8.62a4 4 0 0 1 4.78-4.77 4 4 0 0 1 6.74 0 4 4 0 0 1 4.78 4.78 4 4 0 0 1 0 6.74 4 4 0 0 1-4.77 4.78 4 4 0 0 1-6.75 0 4 4 0 0 1-4.78-4.77 4 4 0 0 1 0-6.76z"/><path d="m9 12 2 2 4-4"/>',
	chat: '<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/><path d="M8 9h8"/><path d="M8 13h5"/>',
	evals: '<path d="M14 2v6a2 2 0 0 0 .245.96l5.51 10.08A2 2 0 0 1 18 22H6a2 2 0 0 1-1.755-2.96l5.51-10.08A2 2 0 0 0 10 8V2"/><path d="M6.453 15h11.094"/><path d="M8.5 2h7"/>',
	history: '<path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/><path d="M12 7v5l4 2"/>',
};

const tiles = {
	sheet: ["#166534", "#4ade80"],
	layers: ["#be123c", "#fb7185"],
	shield: ["#9f1239", "#fda4af"],
	database: ["#1e3a8a", "#60a5fa"],
	chart: ["#b45309", "#fbbf24"],
	check: ["#0e7490", "#22d3ee"],
	chat: ["#7c2d12", "#f97316"],
	evals: ["#5b21b6", "#a78bfa"],
	history: ["#334155", "#64748b"],
};

function iconSvg(name) {
	const [from, to] = tiles[name];
	return `<svg xmlns="http://www.w3.org/2000/svg" width="56" height="56" viewBox="0 0 56 56" aria-hidden="true">
<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${from}"/><stop offset="1" stop-color="${to}"/></linearGradient></defs>
<rect width="56" height="56" rx="16" fill="url(#g)"/>
<rect x=".75" y=".75" width="54.5" height="54.5" rx="15.25" fill="none" stroke="#fff" stroke-opacity=".18" stroke-width="1.5"/>
<g transform="translate(16 16)" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${glyphs[name]}</g>
</svg>
`;
}

const divider = `<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="24" viewBox="0 0 1280 24" aria-hidden="true">
<defs><linearGradient id="d" x1="0" x2="1"><stop offset="0" stop-color="${brand.salmon}" stop-opacity="0"/><stop offset=".3" stop-color="${brand.rose}"/><stop offset=".7" stop-color="${brand.amber}"/><stop offset="1" stop-color="${brand.amber}" stop-opacity="0"/></linearGradient></defs>
<rect y="11" width="1280" height="2" rx="1" fill="url(#d)"/>
<rect x="632" y="4" width="16" height="16" rx="5" fill="${brand.rose}"/><path d="M636.5 12l2.5 2.5 4.5-5" fill="none" stroke="#fff" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
`;

for (const name of ["dark", "light"]) {
	await writeFile(`${out}hero-${name}.svg`, hero(name));
	await writeFile(`${out}metrics-${name}.svg`, metrics(name));
}

for (const name of Object.keys(glyphs)) {
	await writeFile(`${out}icon-${name}.svg`, iconSvg(name));
}

await writeFile(`${out}divider.svg`, divider);

// ── Showcase: the real app, two browser windows on each theme's background ────────────────────────────────
const shot = async (file) => `data:image/png;base64,${(await readFile(`${demo}${file}`)).toString("base64")}`;
const answer = await shot("05-tendencia.png");
const sql = await shot("04-sql.png");
const browser = await chromium.launch();

for (const name of ["dark", "light"]) {
	const t = themes[name];
	const page = await browser.newPage({ viewport: { width: 1600, height: 1000 }, deviceScaleFactor: 1.5 });
	const window = (left, top, url, image, z) => `
		<div class="window" style="left:${left}px;top:${top}px;z-index:${z}"><div class="bar"><span class="d" style="background:#f87171"></span><span class="d" style="background:#fbbf24"></span><span class="d" style="background:#34d399"></span>
			<span class="url">${url}</span></div><div class="view"><img src="${image}"></div></div>`;
	await page.setContent(
		`<!doctype html><html><head><meta charset="utf-8"><style>
		* { box-sizing: border-box; margin: 0; } body { width: 100vw; height: 100vh; overflow: hidden; position: relative; background: ${t.bg}; font-family: ${sans}; }
		.dots { position: absolute; inset: 0; background-image: radial-gradient(${t.dot} 1px, transparent 1.2px); background-size: 22px 22px; opacity: ${t.dotOpacity * 1.4}; }
		.orb { position: absolute; border-radius: 50%; filter: blur(100px); }
		.o1 { width: 700px; height: 700px; left: -200px; top: -260px; background: ${brand.rose}; opacity: ${name === "dark" ? 0.38 : 0.14}; }
		.o2 { width: 700px; height: 700px; right: -220px; bottom: -300px; background: ${brand.amber}; opacity: ${name === "dark" ? 0.25 : 0.16}; }
		.window { position: absolute; width: 900px; border-radius: 16px; overflow: hidden; background: ${t.panel}; border: 1px solid ${t.border};
			box-shadow: 0 50px 100px -20px rgba(0,0,0,${name === "dark" ? 0.65 : 0.25}); }
		.bar { height: 40px; display: flex; align-items: center; gap: 8px; padding: 0 14px; border-bottom: 1px solid ${t.border}; }
		.d { width: 11px; height: 11px; border-radius: 50%; }
		.url { margin: 0 auto; width: 340px; height: 26px; border-radius: 8px; background: ${t.bg}; border: 1px solid ${t.border}; color: ${t.muted};
			font-size: 12.5px; display: flex; align-items: center; justify-content: center; }
		.view { overflow: hidden; } .view img { display: block; width: 100%; }
		</style></head><body><div class="dots"></div><div class="orb o1"></div><div class="orb o2"></div>
		${window(60, 70, "localhost:3030/datasets/…", answer, 1)}${window(640, 380, "localhost:3030/datasets/… · Ver el SQL", sql, 2)}</body></html>`,
		{ waitUntil: "load" },
	);
	await page.screenshot({ path: `${out}showcase-${name}.png` });
	await page.close();
}

await browser.close();
console.log(`README artwork written to ${out}`);
