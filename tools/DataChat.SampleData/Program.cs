using DataChat.SampleData;

// Regenerates samples/: dotnet run --project tools/DataChat.SampleData -- [output directory]. The Excel files are
// committed; a test checks that they hold exactly what this generator produces, so they cannot drift apart.
var output = args.Length > 0 ? args[0] : "samples";
Directory.CreateDirectory(output);

var lines = SalesGenerator.Generate();
foreach (var language in Enum.GetValues<SampleLanguage>())
{
	SampleFiles.WriteExcel(lines, language, Path.Combine(output, $"{SampleFiles.BaseName(language)}.xlsx"));
}

Console.WriteLine($"{lines.Count} sale lines, {lines.Select(static line => line.Invoice).Distinct().Count()} invoices, written to {Path.GetFullPath(output)}");
