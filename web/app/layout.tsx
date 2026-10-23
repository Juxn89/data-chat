import type { Metadata } from "next";
import type { ReactNode } from "react";
import "./globals.css";

export const metadata: Metadata = {
	title: "Data chat",
	description: "Ask your sales data in plain language; the model never writes SQL.",
};

export default function RootLayout({ children }: { children: ReactNode }) {
	return (
		<html lang="es">
			<body className="min-h-screen antialiased">
				<main className="mx-auto w-full max-w-5xl px-4 py-16 sm:px-6">{children}</main>
			</body>
		</html>
	);
}
