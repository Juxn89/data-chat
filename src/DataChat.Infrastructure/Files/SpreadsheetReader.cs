namespace DataChat.Infrastructure.Files;

using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using DataChat.Application.Features.Datasets;
using DataChat.Domain.Datasets;
using NodaTime;

/// <summary>Reads the first sheet of a CSV or Excel (.xlsx) file. The extension decides; nothing else is guessed from it.</summary>
internal sealed class SpreadsheetReader : ISpreadsheetReader
{
	private const string Csv = ".csv";

	private const string Text = ".txt";

	private const string Excel = ".xlsx";

	/// <summary>A semicolon-separated file is usually from a locale that writes 1.234,56.</summary>
	private const string Semicolon = ";";

	private static readonly string[] Delimiters = [",", Semicolon, "\t", "|"];

	private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	public async Task<(RawSheet? Sheet, ReadFailure? Failure)> ReadAsync(Stream content, string fileName, int maxRows, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(content);
		using var buffer = new MemoryStream();
		await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
		buffer.Position = 0;

		return Path.GetExtension(fileName).ToLowerInvariant() switch
		{
			Csv or Text => ReadCsv(buffer.ToArray(), maxRows),
			Excel => ReadExcel(buffer, maxRows),
			_ => (null, ReadFailure.UnsupportedFormat),
		};
	}

	/// <summary>UTF-8 when the bytes are valid UTF-8, Latin-1 otherwise: what Excel on Windows saves as "CSV".</summary>
	private static (RawSheet?, ReadFailure?) ReadCsv(byte[] bytes, int maxRows)
	{
		string text;
		try
		{
			text = StrictUtf8.GetString(bytes);
		}
		catch (DecoderFallbackException)
		{
			text = Encoding.Latin1.GetString(bytes);
		}

		var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
		{
			HasHeaderRecord = false,
			DetectDelimiter = true,
			DetectDelimiterValues = Delimiters,
			BadDataFound = null,
			MissingFieldFound = null,
			IgnoreBlankLines = false,
		};

		using var csv = new CsvReader(new StringReader(text.TrimStart('﻿')), configuration);
		var rows = new List<IReadOnlyList<RawCell>>();
		while (csv.Read())
		{
			if (rows.Count == maxRows)
			{
				return (null, ReadFailure.TooManyRows);
			}

			rows.Add(Enumerable.Range(0, csv.Parser.Count).Select(i => RawCell.FromText(csv.Parser[i])).ToArray());
		}

		return (new RawSheet(rows, PreferCommaDecimal: csv.Parser.Delimiter == Semicolon), null);
	}

	private static (RawSheet?, ReadFailure?) ReadExcel(Stream stream, int maxRows)
	{
		XLWorkbook workbook;
		try
		{
			workbook = new XLWorkbook(stream);
		}
		catch (Exception exception) when (exception is InvalidDataException or FormatException or ArgumentException or InvalidOperationException
			or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException)
		{
			return (null, ReadFailure.Unreadable);
		}

		using (workbook)
		{
			var sheet = workbook.Worksheets.First();
			var used = sheet.RangeUsed();
			if (used is null)
			{
				return (new RawSheet([], PreferCommaDecimal: false), null);
			}

			var lastColumn = used.LastColumn().ColumnNumber();
			var rows = new List<IReadOnlyList<RawCell>>();
			for (var r = 1; r <= used.LastRow().RowNumber(); r++)
			{
				if (rows.Count == maxRows)
				{
					return (null, ReadFailure.TooManyRows);
				}

				var row = sheet.Row(r);
				rows.Add(Enumerable.Range(1, lastColumn).Select(c => Cell(row.Cell(c).Value)).ToArray());
			}

			return (new RawSheet(rows, PreferCommaDecimal: false), null);
		}
	}

	/// <summary>Excel types numbers and dates itself; those are taken as they are, never re-parsed from their display text.</summary>
	private static RawCell Cell(XLCellValue value) => value.Type switch
	{
		XLDataType.Blank => RawCell.Empty,
		XLDataType.Number => RawCell.FromNumber((decimal)value.GetNumber()),
		XLDataType.DateTime => RawCell.FromDate(LocalDate.FromDateTime(value.GetDateTime())),
		XLDataType.Text => RawCell.FromText(value.GetText()),
		_ => RawCell.FromText(value.ToString(CultureInfo.InvariantCulture)),
	};
}
