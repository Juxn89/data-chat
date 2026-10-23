namespace DataChat.SampleData;

/// <summary>A label in both of the sample's languages.</summary>
/// <param name="Es">Spanish.</param>
/// <param name="En">English.</param>
public sealed record Label(string Es, string En)
{
	/// <summary>The label in a language.</summary>
	/// <param name="language">The language.</param>
	/// <returns>The text.</returns>
	public string In(SampleLanguage language) => language == SampleLanguage.Es ? Es : En;
}

/// <summary>The languages the sample dataset is written in: same figures, translated headers and names.</summary>
public enum SampleLanguage
{
	/// <summary>Spanish: <c>ventas-demo</c>.</summary>
	Es,

	/// <summary>English: <c>sales-demo</c>.</summary>
	En,
}

/// <summary>A branch of the distributor.</summary>
/// <param name="Name">A proper name, the same in both languages.</param>
/// <param name="InvoicePrefix">The prefix of its invoice numbers.</param>
/// <param name="InvoicesPerDay">Its average number of invoices on an ordinary day.</param>
public sealed record Branch(string Name, string InvoicePrefix, double InvoicesPerDay);

/// <summary>A product with its list price in USD.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Price">The list price before the January 2026 increase.</param>
public sealed record Product(Label Name, decimal Price);

/// <summary>A category: how often a sale line falls in it, how many units a line carries, and its products.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Weight">Its share of sale lines in the first month, before any growth.</param>
/// <param name="MaxUnits">The most units on one line.</param>
/// <param name="Products">Its products.</param>
public sealed record Category(Label Name, double Weight, int MaxUnits, IReadOnlyList<Product> Products);

/// <summary>
/// "Distribuidora Demo": a consumer-goods distributor with four branches in Managua (plan §0). Names and prices are
/// invented; what matters is that they read like a real small business's sales.
/// </summary>
public static class SalesCatalog
{
	/// <summary>The branch whose sales fall in the last quarter, the pattern the demo asks about.</summary>
	public const string DecliningBranch = "Las Colinas";

	/// <summary>The four branches, from the busiest to the quietest.</summary>
	public static readonly IReadOnlyList<Branch> Branches =
	[
		new("Centro", "CE", 9.0),
		new("Altamira", "AL", 7.0),
		new("Carretera a Masaya", "CM", 6.0),
		new(DecliningBranch, "LC", 5.0),
	];

	/// <summary>The category that grows month after month.</summary>
	public static readonly Label GrowingCategory = new("Snacks", "Snacks");

	/// <summary>The six categories with ten products each.</summary>
	public static readonly IReadOnlyList<Category> Categories =
	[
		new(new("Bebidas", "Beverages"), 0.24, 6,
		[
			new(new("Agua purificada 1 L", "Purified water 1 L"), 0.65m),
			new(new("Agua purificada 5 L", "Purified water 5 L"), 1.90m),
			new(new("Gaseosa cola 2 L", "Cola soda 2 L"), 1.75m),
			new(new("Gaseosa naranja 2 L", "Orange soda 2 L"), 1.70m),
			new(new("Jugo de naranja 1 L", "Orange juice 1 L"), 2.10m),
			new(new("Jugo de manzana 1 L", "Apple juice 1 L"), 2.20m),
			new(new("Té frío limón 500 ml", "Lemon iced tea 500 ml"), 0.95m),
			new(new("Bebida energética 250 ml", "Energy drink 250 ml"), 1.40m),
			new(new("Café molido 400 g", "Ground coffee 400 g"), 4.80m),
			new(new("Cacao en polvo 300 g", "Cocoa powder 300 g"), 3.60m),
		]),
		new(new("Lácteos", "Dairy"), 0.18, 4,
		[
			new(new("Leche entera 1 L", "Whole milk 1 L"), 1.15m),
			new(new("Leche deslactosada 1 L", "Lactose-free milk 1 L"), 1.35m),
			new(new("Yogur natural 1 kg", "Plain yogurt 1 kg"), 2.90m),
			new(new("Yogur de fresa 200 g", "Strawberry yogurt 200 g"), 0.85m),
			new(new("Queso seco 500 g", "Dry cheese 500 g"), 3.40m),
			new(new("Queso crema 250 g", "Cream cheese 250 g"), 2.25m),
			new(new("Crema ácida 500 ml", "Sour cream 500 ml"), 1.95m),
			new(new("Mantequilla 250 g", "Butter 250 g"), 2.60m),
			new(new("Leche en polvo 400 g", "Powdered milk 400 g"), 5.20m),
			new(new("Cuajada 1 lb", "Fresh curd cheese 1 lb"), 2.40m),
		]),
		new(new("Panadería", "Bakery"), 0.16, 5,
		[
			new(new("Pan de molde blanco", "White sandwich bread"), 1.85m),
			new(new("Pan de molde integral", "Whole wheat sandwich bread"), 2.15m),
			new(new("Pan dulce (6 u.)", "Sweet bread (6 pcs)"), 1.50m),
			new(new("Rosquillas 300 g", "Corn rosquillas 300 g"), 2.30m),
			new(new("Galletas de vainilla", "Vanilla cookies"), 1.20m),
			new(new("Galletas de chocolate", "Chocolate cookies"), 1.35m),
			new(new("Tortillas de harina (10 u.)", "Flour tortillas (10 pcs)"), 1.60m),
			new(new("Pan para hamburguesa (8 u.)", "Hamburger buns (8 pcs)"), 1.95m),
			new(new("Queque de vainilla", "Vanilla pound cake"), 3.10m),
			new(new("Tostadas 200 g", "Toasts 200 g"), 1.45m),
		]),
		new(GrowingCategory, 0.12, 6,
		[
			new(new("Papas fritas 150 g", "Potato chips 150 g"), 1.25m),
			new(new("Plátano frito 120 g", "Plantain chips 120 g"), 0.95m),
			new(new("Maní salado 200 g", "Salted peanuts 200 g"), 1.40m),
			new(new("Palomitas 100 g", "Popcorn 100 g"), 0.90m),
			new(new("Barra de cereal", "Cereal bar"), 0.55m),
			new(new("Chocolate en barra 90 g", "Chocolate bar 90 g"), 1.65m),
			new(new("Gomitas 100 g", "Gummies 100 g"), 0.80m),
			new(new("Nachos 200 g", "Tortilla chips 200 g"), 1.55m),
			new(new("Mezcla de frutos secos 150 g", "Trail mix 150 g"), 2.75m),
			new(new("Chicharrones 80 g", "Pork rinds 80 g"), 0.85m),
		]),
		new(new("Limpieza", "Cleaning"), 0.17, 3,
		[
			new(new("Detergente en polvo 1 kg", "Laundry powder 1 kg"), 2.95m),
			new(new("Detergente líquido 2 L", "Liquid detergent 2 L"), 5.40m),
			new(new("Cloro 1 galón", "Bleach 1 gallon"), 2.35m),
			new(new("Lavaplatos en crema 500 g", "Dishwashing paste 500 g"), 1.45m),
			new(new("Desinfectante 1 L", "Disinfectant 1 L"), 2.10m),
			new(new("Limpiavidrios 500 ml", "Glass cleaner 500 ml"), 1.80m),
			new(new("Esponjas (3 u.)", "Sponges (3 pcs)"), 1.10m),
			new(new("Bolsas de basura (10 u.)", "Trash bags (10 pcs)"), 1.65m),
			new(new("Papel higiénico (4 rollos)", "Toilet paper (4 rolls)"), 2.40m),
			new(new("Servilletas (100 u.)", "Napkins (100 pcs)"), 0.95m),
		]),
		new(new("Cuidado personal", "Personal care"), 0.13, 3,
		[
			new(new("Jabón de tocador (3 u.)", "Bath soap (3 pcs)"), 1.90m),
			new(new("Champú 400 ml", "Shampoo 400 ml"), 3.80m),
			new(new("Acondicionador 400 ml", "Conditioner 400 ml"), 3.95m),
			new(new("Pasta dental 100 ml", "Toothpaste 100 ml"), 1.70m),
			new(new("Cepillo dental", "Toothbrush"), 1.25m),
			new(new("Desodorante en barra", "Stick deodorant"), 2.85m),
			new(new("Crema corporal 400 ml", "Body lotion 400 ml"), 4.20m),
			new(new("Toallas húmedas (80 u.)", "Wet wipes (80 pcs)"), 2.15m),
			new(new("Alcohol en gel 250 ml", "Hand sanitizer 250 ml"), 1.85m),
			new(new("Afeitadoras desechables (3 u.)", "Disposable razors (3 pcs)"), 2.50m),
		]),
	];
}
