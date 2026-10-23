namespace DataChat.Architecture.Tests;

using System.Reflection;
using DataChat.Application.Common;
using DataChat.Domain.Periods;
using DataChat.Infrastructure;

/// <summary>
/// Assembly OBJECTS, not names: ArchUnitNET's ResideInAssembly(string) matches the full assembly name, so a
/// simple name matches nothing and every rule passes vacuously. Learned in talent-mcp.
/// </summary>
internal static class Layers
{
	public static readonly Assembly Domain = typeof(PeriodCalendar).Assembly;

	public static readonly Assembly Application = typeof(Error).Assembly;

	public static readonly Assembly Infrastructure = typeof(DependencyInjection).Assembly;

	public static readonly Assembly Api = typeof(Program).Assembly;
}
