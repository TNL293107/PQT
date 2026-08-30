using System.Globalization;
using System.Text;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Universes;

/// <summary>
/// A universe source backed by two CSV files in a directory.
/// </summary>
/// <remarks>
/// <para>
/// The reference implementation, for the reason every other file source here
/// exists: Vietnamese index membership is published as review notices rather
/// than served on an endpoint, and any system that wants the history ends up
/// transcribing it.
/// </para>
/// <para>
/// Two files rather than one, because they carry two different kinds of claim.
/// <c>universes.csv</c> is the operator saying what a set is and which span of
/// its history this directory is supposed to contain;
/// <c>universe-memberships.csv</c> is the history itself. Folding the coverage
/// claim into the membership rows would make it something derived from them,
/// which is exactly what it must not be.
/// </para>
/// <para>
/// Dates are parsed invariantly, so the same directory means the same history
/// on every machine.
/// </para>
/// </remarks>
/// <param name="directoryPath">The directory holding the two files.</param>
internal sealed class FileUniverseMembershipProvider(string directoryPath)
    : IUniverseMembershipProvider
{
    /// <summary>The code membership from this source is attributed to.</summary>
    public const string ProviderCode = "FILE";

    /// <summary>The file naming the universes and the span each one claims.</summary>
    public const string UniverseFileName = "universes.csv";

    /// <summary>The file holding the membership history.</summary>
    public const string MembershipFileName = "universe-memberships.csv";

    private const string CodeColumn = "code";
    private const string NameColumn = "name";
    private const string KindColumn = "kind";
    private const string CoverageFromColumn = "coverage_from";
    private const string CoverageUntilColumn = "coverage_until";

    private const string UniverseCodeColumn = "universe_code";
    private const string SymbolColumn = "symbol";
    private const string EffectiveFromColumn = "effective_from";
    private const string EffectiveToColumn = "effective_to";
    private const string AnnouncedOnColumn = "announced_on";

    private static readonly string[] RequiredUniverseColumns = [CodeColumn, NameColumn, KindColumn];

    private static readonly string[] RequiredMembershipColumns =
        [UniverseCodeColumn, SymbolColumn, EffectiveFromColumn];

    /// <inheritdoc />
    public SourceCode Code { get; } = SourceCode.Create(ProviderCode);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderUniverse>> ListUniversesAsync(
        CancellationToken cancellationToken = default)
    {
        var lines = await ReadLinesAsync(UniverseFileName, cancellationToken).ConfigureAwait(false);
        var header = ReadHeader(lines[0], RequiredUniverseColumns, UniverseFileName);
        var rows = new List<ProviderUniverse>(lines.Length - 1);

        for (var index = 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            var fields = lines[index].Split(',');
            var lineNumber = index + 1;

            rows.Add(new ProviderUniverse(
                Read(header, fields, CodeColumn) ?? string.Empty,
                Read(header, fields, NameColumn) ?? string.Empty,
                Read(header, fields, KindColumn) ?? string.Empty,
                ReadDate(header, fields, CoverageFromColumn, lineNumber, UniverseFileName),
                ReadDate(header, fields, CoverageUntilColumn, lineNumber, UniverseFileName)));
        }

        return rows;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderUniverseMembership>> ListMembershipsAsync(
        CancellationToken cancellationToken = default)
    {
        var lines = await ReadLinesAsync(MembershipFileName, cancellationToken).ConfigureAwait(false);
        var header = ReadHeader(lines[0], RequiredMembershipColumns, MembershipFileName);
        var rows = new List<ProviderUniverseMembership>(lines.Length - 1);

        for (var index = 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            var fields = lines[index].Split(',');
            var lineNumber = index + 1;

            var from = ReadDate(header, fields, EffectiveFromColumn, lineNumber, MembershipFileName)
                ?? throw new MarketDataProviderException(
                    $"Line {lineNumber} of {MembershipFileName} has no {EffectiveFromColumn}.");

            rows.Add(new ProviderUniverseMembership(
                Read(header, fields, UniverseCodeColumn) ?? string.Empty,
                Read(header, fields, SymbolColumn) ?? string.Empty,
                from,
                ReadDate(header, fields, EffectiveToColumn, lineNumber, MembershipFileName),
                ReadDate(header, fields, AnnouncedOnColumn, lineNumber, MembershipFileName)));
        }

        return rows;
    }

    private async Task<string[]> ReadLinesAsync(string fileName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directoryPath, fileName);

        if (!File.Exists(path))
        {
            throw new MarketDataProviderException(
                $"No {fileName} exists in the configured universe directory.");
        }

        string[] lines;

        try
        {
            lines = await File.ReadAllLinesAsync(path, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            throw new MarketDataProviderException(
                $"{fileName} could not be read.", isTransient: true, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new MarketDataProviderException(
                $"{fileName} is not readable.", isTransient: false, exception);
        }

        return lines.Length > 0
            ? lines
            : throw new MarketDataProviderException($"{fileName} is empty.");
    }

    private static Dictionary<string, int> ReadHeader(
        string line,
        string[] required,
        string fileName)
    {
        var columns = line.Split(',');
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < columns.Length; index++)
        {
            byName[columns[index].Trim()] = index;
        }

        var missing = required.Where(column => !byName.ContainsKey(column)).ToList();

        return missing.Count == 0
            ? byName
            : throw new MarketDataProviderException(
                $"{fileName} is missing the column(s): {string.Join(", ", missing)}.");
    }

    private static string? Read(Dictionary<string, int> header, string[] fields, string column)
    {
        if (!header.TryGetValue(column, out var index) || index >= fields.Length)
        {
            return null;
        }

        var value = fields[index].Trim();

        return value.Length == 0 ? null : value;
    }

    private static DateOnly? ReadDate(
        Dictionary<string, int> header,
        string[] fields,
        string column,
        int lineNumber,
        string fileName)
    {
        var raw = Read(header, fields, column);

        if (raw is null)
        {
            return null;
        }

        return DateOnly.TryParseExact(
            raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            // Refused whole rather than skipped. A date this system cannot read
            // is the difference between a security being in an index and not,
            // and guessing at it would be a guess about what a strategy could
            // have held.
            : throw new MarketDataProviderException(
                $"Line {lineNumber} of {fileName} has an unreadable {column} '{raw}'.");
    }
}
