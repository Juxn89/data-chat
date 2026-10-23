// Day 1: the shell the AppHost serves. Import, the semantic layer and the chat arrive on days 7-8.
export default function Home() {
	return (
		<section className="space-y-4">
			<p className="text-sm font-semibold uppercase tracking-wide text-brand">Distribuidora Demo</p>
			<h1 className="text-4xl font-bold">Pregúntale a tus ventas</h1>
			<p className="max-w-2xl text-lg text-muted">
				Sube tu Excel y pregunta en español o en inglés. Recibes la cifra, el gráfico y cómo se calculó. El modelo
				nunca escribe SQL.
			</p>
		</section>
	);
}
