"use client";

import { MessagesSquare } from "lucide-react";
import Link from "next/link";
import { locales, setLocale, useI18n } from "@/lib/i18n";

export function Header() {
	const { locale, t } = useI18n();

	return (
		<header className="sticky top-0 z-20 border-b border-line bg-paper/90 backdrop-blur">
			<div className="mx-auto flex h-14 w-full max-w-6xl items-center gap-4 px-4 sm:px-6">
				<Link href="/" className="flex items-center gap-2.5">
					<span className="grid size-8 place-items-center rounded-md bg-brand text-white">
						<MessagesSquare className="size-4.5" aria-hidden />
					</span>
					<span className="font-semibold tracking-tight">{t.app.name}</span>
				</Link>
				<nav className="ml-auto flex items-center gap-3">
					<Link href="/" className="rounded-md px-3 py-1.5 text-sm font-medium text-muted hover:bg-black/5 hover:text-ink">
						{t.nav.datasets}
					</Link>
					<div role="group" aria-label={t.nav.language} className="flex rounded-md border border-line bg-card p-0.5">
						{locales.map((code) => (
							<button
								key={code}
								type="button"
								onClick={() => setLocale(code)}
								aria-pressed={locale === code}
								className={`rounded px-2 py-1 text-xs font-semibold uppercase ${locale === code ? "bg-ink text-white" : "text-muted hover:text-ink"}`}
							>
								{code}
							</button>
						))}
					</div>
				</nav>
			</div>
		</header>
	);
}
